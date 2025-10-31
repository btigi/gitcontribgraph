using LibGit2Sharp;
using SixLabors.ImageSharp;
using System.CommandLine;
using System.IO;

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

var rootCommand = new RootCommand("Generates a GitHub-style contribution graph from a git repository")
{
    inputDirOption,
    outputFileOption,
    startDateOption,
    endDateOption,
    scanSubdirectoriesOption
};

rootCommand.SetHandler((string inputDir, string? outputFile, string? startDateStr, string? endDateStr, bool scanSubdirectories) =>
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
                ProcessRepository(repoPath, startDate, endDate, commitsByDate);
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

        ProcessRepository(inputDir, startDate, endDate, commitsByDate);
        reposProcessed.Add(inputDir);
    }

    Console.WriteLine($"\nProcessing commits from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}...");
    Console.WriteLine($"Processed {reposProcessed.Count} repository(ies)");
    Console.WriteLine($"Found {commitsByDate.Values.Sum()} total contributions across {commitsByDate.Count} days");

    // Generate the contribution graph
    var graphGenerator = new ContributionGraphGenerator();
    var image = graphGenerator.Generate(commitsByDate, startDate, endDate);

    // Save the image
    image.SaveAsPng(outputFileName);
    Console.WriteLine($"Contribution graph saved to {outputFileName}");
}, inputDirOption, outputFileOption, startDateOption, endDateOption, scanSubdirectoriesOption);

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

static void ProcessRepository(string repoPath, DateTime startDate, DateTime endDate, Dictionary<DateTime, int> commitsByDate)
{
    using var repo = new Repository(repoPath);

    foreach (var commit in repo.Commits)
    {
        var commitDate = commit.Committer.When.DateTime.Date;
        
        if (commitDate >= startDate && commitDate < endDate)
        {
            commitsByDate.TryGetValue(commitDate, out int count);
            commitsByDate[commitDate] = count + 1;
        }
    }
}

return await rootCommand.InvokeAsync(args);


