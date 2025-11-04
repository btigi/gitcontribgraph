using LibGit2Sharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.CommandLine;
using System.IO;
using System.Text.Json;

var inputDirOption = new Option<string>(
    aliases: new[] { "--input", "-i" },
    description: "Path to the git repository directory")
{
    IsRequired = true
};

var outputFileOption = new Option<string>(
    aliases: new[] { "--output", "-o" },
    description: "Output filename for the PNG image",
    getDefaultValue: () => "output.png");

var startDateOption = new Option<string>(
    aliases: new[] { "--start-date", "-s" },
    description: "Start date for counting commits (format: YYYY-MM-DD)")
{
    ArgumentHelpName = "YYYY-MM-DD"
};

var endDateOption = new Option<string>(
    aliases: new[] { "--end-date", "-e" },
    description: "End date for counting commits (format: YYYY-MM-DD)")
{
    ArgumentHelpName = "YYYY-MM-DD"
};

var scanSubdirectoriesOption = new Option<bool>(
    aliases: new[] { "--scan-subdirectories", "-r" },
    description: "Recursively scan all git repositories in subdirectories and combine their data",
    getDefaultValue: () => false);

var userFilterOption = new Option<string>(
    aliases: new[] { "--user", "-u" },
    description: "Filter commits by user (matches name or email, case-insensitive)");

var coloursFileOption = new Option<string>(
    aliases: new[] { "--colours", "-c" },
    description: "Path to JSON file containing colour configuration (hex format)");

var rootCommand = new RootCommand("Generates a GitHub-style contribution graph from a git repository")
{
    inputDirOption,
    outputFileOption,
    startDateOption,
    endDateOption,
    scanSubdirectoriesOption,
    userFilterOption,
    coloursFileOption
};

rootCommand.SetHandler((string inputDir, string? outputFile, string? startDateStr, string? endDateStr, bool scanSubdirectories, string? userFilter, string? coloursFile) =>
{
    // Parse dates with defaults
    DateTime startDate = DateTime.Today.AddMonths(-12);
    DateTime endDate = DateTime.Today;

    if (!string.IsNullOrWhiteSpace(startDateStr))
    {
        if (!DateTime.TryParse(startDateStr, out startDate))
        {
            Console.WriteLine($"Error: Invalid start date format '{startDateStr}'. Expected YYYY-MM-DD.");
            Environment.Exit(1);
            return;
        }
        startDate = startDate.Date; // Ensure we use just the date part
    }

    if (!string.IsNullOrWhiteSpace(endDateStr))
    {
        if (!DateTime.TryParse(endDateStr, out endDate))
        {
            Console.WriteLine($"Error: Invalid end date format '{endDateStr}'. Expected YYYY-MM-DD.");
            Environment.Exit(1);
            return;
        }
        endDate = endDate.Date; // Ensure we use just the date part
    }

    if (startDate >= endDate)
    {
        Console.WriteLine("Error: Start date must be before end date.");
        Environment.Exit(1);
        return;
    }

    string outputFileName = outputFile ?? "output.png";

    // Count commits per day from all repositories
    var commitsByDate = new Dictionary<DateTime, int>();
    List<string> reposProcessed = new List<string>();

    if (scanSubdirectories)
    {
        // Recursively find all git repositories
        var repos = FindGitRepositories(inputDir);
        
        if (repos.Count == 0)
        {
            Console.WriteLine($"Error: No git repositories found in '{inputDir}' or its subdirectories.");
            Environment.Exit(1);
            return;
        }

        Console.WriteLine($"Found {repos.Count} git repository(ies) to process:");
        foreach (var repoPath in repos)
        {
            Console.WriteLine($"  - {repoPath}");
        }
        Console.WriteLine();

        // Process each repository
        foreach (var repoPath in repos)
        {
            try
            {
                ProcessRepository(repoPath, startDate, endDate, commitsByDate, userFilter);
                reposProcessed.Add(repoPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to process repository '{repoPath}': {ex.Message}");
            }
        }
    }
    else
    {
        // Single repository mode
        if (!Repository.IsValid(inputDir))
        {
            Console.WriteLine($"Error: '{inputDir}' is not a valid git repository.");
            Environment.Exit(1);
            return;
        }

        ProcessRepository(inputDir, startDate, endDate, commitsByDate, userFilter);
        reposProcessed.Add(inputDir);
    }

    Console.WriteLine($"\nProcessing commits from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}...");
    if (!string.IsNullOrWhiteSpace(userFilter))
    {
        Console.WriteLine($"Filtering by user: {userFilter}");
    }
    Console.WriteLine($"Processed {reposProcessed.Count} repository(ies)");
    Console.WriteLine($"Found {commitsByDate.Values.Sum()} total contributions across {commitsByDate.Count} days");

    Rgba32[] contributionColours = LoadColoursFromFile(coloursFile);

    // Generate the contribution graph
    var graphGenerator = new ContributionGraphGenerator(contributionColours);
    var image = graphGenerator.Generate(commitsByDate, startDate, endDate);

    // Save the image
    image.SaveAsPng(outputFileName);
    Console.WriteLine($"Contribution graph saved to {outputFileName}");
}, inputDirOption, outputFileOption, startDateOption, endDateOption, scanSubdirectoriesOption, userFilterOption, coloursFileOption);

static Rgba32[] LoadColoursFromFile(string? coloursFile)
{
    // Default green colours (GitHub style)
    Rgba32[] defaultColours = new[]
    {
        new Rgba32(22, 27, 34),      // No contributions
        new Rgba32(14, 68, 41),      // 1-2 contributions
        new Rgba32(0, 109, 50),      // 3-5 contributions
        new Rgba32(38, 166, 65),     // 6-10 contributions
        new Rgba32(57, 211, 83),     // 11+ contributions
    };

    if (string.IsNullOrWhiteSpace(coloursFile))
    {
        return defaultColours;
    }

    if (!File.Exists(coloursFile))
    {
        Console.WriteLine($"Warning: Colours file '{coloursFile}' not found. Using default colours.");
        return defaultColours;
    }

    try
    {
        string jsonContent = File.ReadAllText(coloursFile);
        var jsonDoc = JsonDocument.Parse(jsonContent);
        var root = jsonDoc.RootElement;

        // Expect an array of hex colour strings
        if (root.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine($"Warning: Colours file '{coloursFile}' must contain a JSON array. Using default colours.");
            return defaultColours;
        }

        var colours = new List<Rgba32>();
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                Console.WriteLine($"Warning: Invalid colour format in colours file. Using default colours.");
                return defaultColours;
            }

            string hexColour = element.GetString() ?? string.Empty;
            if (TryParseHexColour(hexColour, out Rgba32 colour))
            {
                colours.Add(colour);
            }
            else
            {
                Console.WriteLine($"Warning: Invalid hex colour '{hexColour}' in colours file. Using default colours.");
                return defaultColours;
            }
        }

        if (colours.Count != 5)
        {
            Console.WriteLine($"Warning: Colours file must contain exactly 5 colours (found {colours.Count}). Using default colours.");
            return defaultColours;
        }

        return colours.ToArray();
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"Warning: Invalid JSON in colours file '{coloursFile}': {ex.Message}. Using default colours.");
        return defaultColours;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: Error reading colours file '{coloursFile}': {ex.Message}. Using default colours.");
        return defaultColours;
    }
}

static bool TryParseHexColour(string hex, out Rgba32 colour)
{
    colour = default;

    if (string.IsNullOrWhiteSpace(hex))
        return false;

    // Remove # if present
    hex = hex.TrimStart('#');

    // Must be 6 characters (RRGGBB) or 8 characters (RRGGBBAA)
    if (hex.Length != 6 && hex.Length != 8)
        return false;

    try
    {
        uint value = Convert.ToUInt32(hex, 16);
        
        if (hex.Length == 6)
        {
            // RRGGBB format - assume alpha = 255 (fully opaque)
            byte r = (byte)((value >> 16) & 0xFF);
            byte g = (byte)((value >> 8) & 0xFF);
            byte b = (byte)(value & 0xFF);
            colour = new Rgba32(r, g, b, 255);
        }
        else
        {
            // RRGGBBAA format
            byte r = (byte)((value >> 24) & 0xFF);
            byte g = (byte)((value >> 16) & 0xFF);
            byte b = (byte)((value >> 8) & 0xFF);
            byte a = (byte)(value & 0xFF);
            colour = new Rgba32(r, g, b, a);
        }

        return true;
    }
    catch
    {
        return false;
    }
}

static List<string> FindGitRepositories(string rootPath)
{
    var repositories = new List<string>();
    var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (!Directory.Exists(rootPath))
    {
        return repositories;
    }

    // Check if the root directory itself is a git repo
    if (Repository.IsValid(rootPath))
    {
        repositories.Add(rootPath);
        visitedDirs.Add(rootPath);
    }

    // Recursively search for git repositories, avoiding .git directories
    try
    {
        ScanDirectory(rootPath, repositories, visitedDirs);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: Error scanning directories: {ex.Message}");
    }

    return repositories;
}

static void ScanDirectory(string directory, List<string> repositories, HashSet<string> visitedDirs)
{
    // Skip if already visited or if this is a .git directory
    if (visitedDirs.Contains(directory) || Path.GetFileName(directory).Equals(".git", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    visitedDirs.Add(directory);

    // Check if this directory is a git repository
    if (Repository.IsValid(directory))
    {
        repositories.Add(directory);
        // Don't scan inside a git repository's subdirectories
        return;
    }

    // Recursively scan subdirectories
    try
    {
        var subdirs = Directory.GetDirectories(directory);
        foreach (var subdir in subdirs)
        {
            ScanDirectory(subdir, repositories, visitedDirs);
        }
    }
    catch (UnauthorizedAccessException)
    {
        // Skip directories we don't have access to
    }
    catch (Exception)
    {
        // Ignore other errors for individual directories
    }
}

static void ProcessRepository(string repoPath, DateTime startDate, DateTime endDate, Dictionary<DateTime, int> commitsByDate, string? userFilter)
{
    using var repo = new Repository(repoPath);

    foreach (var commit in repo.Commits)
    {
        if (commit.Committer?.When == null)
            continue;
            
        var commitDate = commit.Committer.When.DateTime.Date;
        
        if (commitDate >= startDate && commitDate < endDate)
        {
            if (!string.IsNullOrWhiteSpace(userFilter))
            {
                bool matchesFilter = false;
                
                if (commit.Author != null)
                {
                    matchesFilter = (!string.IsNullOrEmpty(commit.Author.Name) && 
                                    commit.Author.Name.Contains(userFilter, StringComparison.OrdinalIgnoreCase)) ||
                                   (!string.IsNullOrEmpty(commit.Author.Email) && 
                                    commit.Author.Email.Contains(userFilter, StringComparison.OrdinalIgnoreCase));
                }
                
                if (!matchesFilter && commit.Committer != null)
                {
                    matchesFilter = (!string.IsNullOrEmpty(commit.Committer.Name) && 
                                    commit.Committer.Name.Contains(userFilter, StringComparison.OrdinalIgnoreCase)) ||
                                   (!string.IsNullOrEmpty(commit.Committer.Email) && 
                                    commit.Committer.Email.Contains(userFilter, StringComparison.OrdinalIgnoreCase));
                }
                
                if (!matchesFilter)
                {
                    continue;
                }
            }
            
            commitsByDate.TryGetValue(commitDate, out int count);
            commitsByDate[commitDate] = count + 1;
        }
    }
}

return await rootCommand.InvokeAsync(args);


