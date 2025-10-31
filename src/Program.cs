using LibGit2Sharp;
using SixLabors.ImageSharp;

if (args.Length == 0)
{
    Console.WriteLine("Usage: GitContribGraph <path-to-git-repo>");
    return 1;
}

string repoPath = args[0];

if (!Repository.IsValid(repoPath))
{
    Console.WriteLine($"Error: '{repoPath}' is not a valid git repository.");
    return 1;
}

using var repo = new Repository(repoPath);

var endDate = DateTime.Today;
var startDate = endDate.AddMonths(-12);

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

var graphGenerator = new ContributionGraphGenerator();
var image = graphGenerator.Generate(commitsByDate, startDate, endDate);

image.SaveAsPng("output.png");
Console.WriteLine("Contribution graph saved to output.png");

return 0;

