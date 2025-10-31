using LibGit2Sharp;
using SixLabors.ImageSharp;
using System.CommandLine;

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

var rootCommand = new RootCommand("Generates a GitHub-style contribution graph from a git repository")
{
    inputDirOption,
    outputFileOption,
    startDateOption,
    endDateOption
};

rootCommand.SetHandler((string inputDir, string? outputFile, string? startDateStr, string? endDateStr) =>
{
    if (!Repository.IsValid(inputDir))
    {
        Console.WriteLine($"Error: '{inputDir}' is not a valid git repository.");
        Environment.Exit(1);
        return;
    }

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

    using var repo = new Repository(inputDir);

    // Count commits per day
    var commitsByDate = new Dictionary<DateTime, int>();

    foreach (var commit in repo.Commits)
    {
        var commitDate = commit.Committer.When.DateTime.Date;
        
        if (commitDate >= startDate && commitDate < endDate)
        {
            commitsByDate.TryGetValue(commitDate, out int count);
            commitsByDate[commitDate] = count + 1;
        }
    }

    Console.WriteLine($"Processing commits from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}...");
    Console.WriteLine($"Found {commitsByDate.Values.Sum()} total contributions across {commitsByDate.Count} days");

    // Generate the contribution graph
    var graphGenerator = new ContributionGraphGenerator();
    var image = graphGenerator.Generate(commitsByDate, startDate, endDate);

    // Save the image
    image.SaveAsPng(outputFileName);
    Console.WriteLine($"Contribution graph saved to {outputFileName}");
}, inputDirOption, outputFileOption, startDateOption, endDateOption);

return await rootCommand.InvokeAsync(args);


