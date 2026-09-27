// Fails if any packed dependency lacks its intended version range. Usage, after dotnet pack:
//
//   dotnet run scripts/check-package-ranges.cs -- <directory containing the .nupkg files>
//
// The rules:
//   - A dependency on another package from the same directory (a sibling from this release) must be exact: [x.y.z].
//   - Every other dependency must be bounded to one major version: [a.b.c, (a+1).0.0).
//   - Microsoft.Extensions.*, Microsoft.AspNetCore.*, Microsoft.EntityFrameworkCore* and Npgsql must also match the
//     target framework's major version (net10.0 depends on 10.x).
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var directory = args.Length > 0 ? args[0] : "artifacts";
var packages = Directory.GetFiles(directory, "*.nupkg")
    .Where(path => !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
    .Select(ReadPackage)
    .ToList();

if (packages.Count == 0)
{
    Console.Error.WriteLine($"No packages found in '{directory}'.");
    return 1;
}

var siblings = packages.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
var failures = new List<string>();

foreach (var package in packages)
{
    foreach (var dependency in package.Dependencies)
    {
        var problem = siblings.Contains(dependency.Id)
            ? CheckSibling(dependency, package.Version)
            : CheckBand(dependency);

        if (problem is not null)
        {
            failures.Add($"{package.Id} {package.Version} [{dependency.Framework}] depends on {dependency.Id} {dependency.Version}: {problem}");
        }
    }

    Console.WriteLine($"{package.Id} {package.Version}: {package.Dependencies.Count} dependencies checked");
}

var prefix = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? "::error::" : "";
foreach (var failure in failures)
{
    Console.Error.WriteLine(prefix + failure);
}

Console.WriteLine(failures.Count == 0
    ? $"All dependency ranges in {packages.Count} packages are as intended."
    : $"{failures.Count} dependencies lack their intended range.");

return failures.Count == 0 ? 0 : 1;

static string? CheckSibling(Dependency dependency, string packageVersion) =>
    dependency.Version == $"[{packageVersion}]"
        ? null
        : $"expected exactly [{packageVersion}], because packages from the same release are used together";

static string? CheckBand(Dependency dependency)
{
    var match = Regex.Match(dependency.Version, @"^\[(?<min>[^,\[\]()\s]+), ?(?<max>[^,\[\]()\s]+)\)$");
    if (!match.Success)
    {
        return "expected a bounded range [min, max)";
    }

    var major = Major(match.Groups["min"].Value);
    if (match.Groups["max"].Value != $"{major + 1}.0.0")
    {
        return $"expected the upper bound {major + 1}.0.0 (the next major version)";
    }

    var frameworkMajor = Regex.Match(dependency.Framework, @"^net(?<major>\d+)\.\d+$");
    var followsFramework = dependency.Id.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase)
        || dependency.Id.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase)
        || dependency.Id.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
        || dependency.Id.Equals("Npgsql", StringComparison.OrdinalIgnoreCase);

    return followsFramework && frameworkMajor.Success && int.Parse(frameworkMajor.Groups["major"].Value) != major
        ? $"expected version {frameworkMajor.Groups["major"].Value}.x to match the target framework"
        : null;
}

static int Major(string version) => int.Parse(version.Split('.', '-')[0]);

static Package ReadPackage(string path)
{
    using var archive = ZipFile.OpenRead(path);
    var entry = archive.Entries.Single(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
    using var stream = entry.Open();
    var metadata = XDocument.Load(stream).Root!.Elements().Single(e => e.Name.LocalName == "metadata");

    string Value(string name) => metadata.Elements().Single(e => e.Name.LocalName == name).Value;

    var dependencies = metadata.Elements()
        .Where(e => e.Name.LocalName == "dependencies")
        .Elements()
        .Where(e => e.Name.LocalName == "group")
        .SelectMany(group => group.Elements()
            .Where(e => e.Name.LocalName == "dependency")
            .Select(d => new Dependency(
                (string?)group.Attribute("targetFramework") ?? "any",
                (string)d.Attribute("id")!,
                (string?)d.Attribute("version") ?? "")))
        .ToList();

    return new Package(Value("id"), Value("version"), dependencies);
}

internal sealed record Package(string Id, string Version, List<Dependency> Dependencies);

internal sealed record Dependency(string Framework, string Id, string Version);
