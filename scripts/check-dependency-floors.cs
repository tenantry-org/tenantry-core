// The minimum dependency lane: fails unless every version-range floor the packages publish is what some test
// actually runs against. Usage:
//
//   dotnet run scripts/check-dependency-floors.cs
//
// A range ("[8.0.31, 9.0.0)") tells consumers the oldest version that works: its floor. NuGet resolves a range
// to the lowest version the graph allows, but a test graph can lift it (a test's own provider package can
// need a newer EF Core), and then the floor is promised without ever being tested. For each range declared in
// src, per target framework, this reads the committed test lock files (which CI restores in locked mode) and
// requires at least one test project that includes that src project to resolve exactly the floor.
// Ranges set through an MSBuild property (our own packages, the .NET 11 preview) are not checked.
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string[] defaultFrameworks = ["net8.0", "net9.0", "net10.0"];

var floors = new List<(string Project, string Package, string Framework, string Floor)>();
foreach (var path in Directory.EnumerateFiles("src", "*.csproj", SearchOption.AllDirectories))
{
    var project = Path.GetFileNameWithoutExtension(path);
    foreach (var reference in XDocument.Load(path).Descendants().Where(element => element.Name.LocalName == "PackageReference"))
    {
        var id = (string?)reference.Attribute("Include");
        var version = ((string?)reference.Attribute("Version"))?.Trim();
        if (id is null || version is null || !version.StartsWith('[') || version.Contains("$(")) continue;

        var floor = Regex.Match(version, @"^\[\s*([^,\]\s]+)").Groups[1].Value;
        var condition = (string?)reference.Attribute("Condition") ?? (string?)reference.Parent?.Attribute("Condition");
        var framework = condition is null ? null : Regex.Match(condition, @"'\$\(TargetFramework\)'\s*==\s*'([^']+)'").Groups[1].Value;
        if (condition is not null && string.IsNullOrEmpty(framework))
        {
            Console.Error.WriteLine($"{path}: {id} has a condition this check does not understand: {condition}");
            return 1;
        }

        foreach (var target in framework is null ? defaultFrameworks : [framework])
        {
            if (defaultFrameworks.Contains(target)) floors.Add((project, id, target, floor));
        }
    }
}

// Per test lock file and framework: the projects in the graph, and the resolved version of each package.
var graphs = new List<(string Test, string Framework, HashSet<string> Projects, Dictionary<string, string> Resolved)>();
foreach (var path in Directory.EnumerateFiles("tests", "packages.lock.json", SearchOption.AllDirectories))
{
    if (path.Split(Path.DirectorySeparatorChar).Contains("obj")) continue;

    using var lockFile = JsonDocument.Parse(File.ReadAllText(path));
    foreach (var framework in lockFile.RootElement.GetProperty("dependencies").EnumerateObject())
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in framework.Value.EnumerateObject())
        {
            var type = dependency.Value.GetProperty("type").GetString();
            if (type == "Project") projects.Add(dependency.Name);
            else if (dependency.Value.TryGetProperty("resolved", out var version)) resolved[dependency.Name] = version.GetString()!;
        }

        graphs.Add((Path.GetFileName(Path.GetDirectoryName(path)!), framework.Name, projects, resolved));
    }
}

var failures = new List<string>();
foreach (var (project, package, framework, floor) in floors.Distinct())
{
    var tested = graphs
        .Where(graph => graph.Framework == framework && graph.Projects.Contains(project) && graph.Resolved.ContainsKey(package))
        .Select(graph => (graph.Test, Version: graph.Resolved[package]))
        .ToList();

    if (tested.Count == 0)
    {
        failures.Add($"{project} [{framework}] {package} {floor}: no test resolves it for {framework}.");
    }
    else if (!tested.Any(test => string.Equals(test.Version, floor, StringComparison.OrdinalIgnoreCase)))
    {
        var versions = string.Join(", ", tested.Select(test => $"{test.Version} in {test.Test}").Distinct());
        failures.Add($"{project} [{framework}] {package}: floor {floor} is never tested ({versions}).");
    }
}

foreach (var failure in failures) Console.Error.WriteLine(failure);
if (failures.Count > 0)
{
    Console.Error.WriteLine("Raise each floor to a version a test runs against, or add a test that runs against the floor.");
    return 1;
}

Console.WriteLine($"Every floor is tested: {floors.Distinct().Count()} ranges across {graphs.Count} test graphs.");
return 0;
