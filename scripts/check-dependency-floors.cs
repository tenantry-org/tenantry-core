// The minimum dependency lane: fails unless every version-range floor the packages publish is what some test
// actually runs against. Usage:
//
//   dotnet run scripts/check-dependency-floors.cs
//
// A range ("[8.0.31, 9.0.0)") tells consumers the oldest version that works: its floor. NuGet resolves a range
// to the lowest version the graph allows, but a test graph can lift it (a test's own provider package can
// need a newer EF Core), and then the floor is promised without ever being tested. For each range a src project
// depends on, per target framework (its central version in Directory.Packages.props, as MSBuild evaluates it for
// that framework), this reads the committed test lock files (which CI restores in locked mode) and requires at
// least one test project that includes that src project to resolve exactly the floor. Our own packages and the
// .NET 11 preview are not checked.
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

string[] defaultFrameworks = ["net8.0", "net9.0", "net10.0"];

var floors = new List<(string Project, string Package, string Framework, string Floor)>();
foreach (var path in Directory.EnumerateFiles("src", "*.csproj", SearchOption.AllDirectories))
{
    var project = Path.GetFileNameWithoutExtension(path);
    foreach (var framework in defaultFrameworks)
    {
        var items = Evaluate(path, framework);
        var central = items.GetProperty("PackageVersion").EnumerateArray()
            .ToDictionary(item => item.GetProperty("Identity").GetString()!, item => Metadata(item, "Version"), StringComparer.OrdinalIgnoreCase);

        foreach (var reference in items.GetProperty("PackageReference").EnumerateArray())
        {
            if (Metadata(reference, "IsImplicitlyDefined") == "true") continue;

            var id = reference.GetProperty("Identity").GetString()!;
            var version = (Metadata(reference, "VersionOverride") ?? Metadata(reference, "Version") ?? central.GetValueOrDefault(id))?.Trim();
            if (version is null || !version.StartsWith('[') || id.StartsWith("Tenantry.", StringComparison.OrdinalIgnoreCase)) continue;

            floors.Add((project, id, framework, Regex.Match(version, @"^\[\s*([^,\]\s]+)").Groups[1].Value));
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

// The project's package references and central versions, as MSBuild evaluates them for one target framework.
static JsonElement Evaluate(string project, string framework)
{
    using var process = Process.Start(new ProcessStartInfo("dotnet",
        ["msbuild", project, "-getItem:PackageReference", "-getItem:PackageVersion", $"-p:TargetFramework={framework}", "-nologo"])
    {
        RedirectStandardOutput = true,
    })!;
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"Evaluating {project} for {framework} failed:\n{output}");

    return JsonDocument.Parse(output).RootElement.GetProperty("Items").Clone();
}

static string? Metadata(JsonElement item, string name) =>
    item.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text ? text : null;
