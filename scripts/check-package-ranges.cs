// Fails if any packed dependency lacks its intended version range. Usage, after dotnet pack:
//
//   dotnet run scripts/check-package-ranges.cs -- <directory containing the .nupkg files>
//
// The rules:
//   - A dependency on another package from the same directory (a sibling from this release) must take this release
//     up to the next minor: [x.y.z, x.(y+1).0). The packages share no internals, and a minor release in 0.x may
//     break (in 1.x a major would), so a consumer can update one of them within the minor.
//   - Microsoft.Extensions.*, Microsoft.Data.SqlClient, MySqlConnector, Azure.Identity and Microsoft.Identity.Client
//     take a minimum only (">= x.y.z", no upper bound). Microsoft ships every Microsoft.Extensions major for every
//     supported framework and keeps it compatible, and current Azure SDKs need Microsoft.Extensions 10.x even on
//     net8.0, so a cap would stop consumers restoring. The drivers are uncapped by the EF Core providers
//     themselves, and the identity packages are only security floors. A Microsoft.Extensions.* minimum must be the
//     target framework's own major (net8.0 needs 8.x or later, not 10.x).
//   - Every other dependency must be bounded to one major version: [a.b.c, (a+1).0.0). Microsoft.AspNetCore.*,
//     Microsoft.EntityFrameworkCore* and Npgsql must also match the target framework's major version (net10.0
//     depends on 10.x), because each framework's build is compiled against that major.
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
            : IsMinimumOnly(dependency.Id)
                ? CheckMinimum(dependency)
                : CheckBand(dependency);

        if (problem is not null)
        {
            failures.Add($"{package.Id} {package.Version} [{dependency.Framework}] depends on {dependency.Id} {dependency.Version}: {problem}");
        }
    }

    Console.WriteLine($"{package.Id} {package.Version}: {package.Dependencies.Count} dependencies checked");
}

// docs/compatibility.md names the target frameworks ("The packages target **net8.0, net9.0 and net10.0**"); it
// must match what the packages ship.
var compatibility = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory))!, "docs", "compatibility.md");
compatibility = File.Exists(compatibility) ? compatibility : Path.Combine("docs", "compatibility.md");
var documented = Regex.Match(File.ReadAllText(compatibility), @"The packages target \*\*(?<list>[^*]+)\*\*").Groups["list"].Value;
var documentedFrameworks = Regex.Matches(documented, @"net\d+\.\d+").Select(m => m.Value).ToHashSet();
var shippedFrameworks = packages.SelectMany(p => p.Dependencies).Select(d => d.Framework).Where(f => f.StartsWith("net", StringComparison.Ordinal)).ToHashSet();
if (!documentedFrameworks.SetEquals(shippedFrameworks))
{
    failures.Add($"{compatibility} lists {string.Join(", ", documentedFrameworks.Order())} but the packages ship {string.Join(", ", shippedFrameworks.Order())}.");
}

var prefix = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? "::error::" : "";
foreach (var failure in failures)
{
    Console.Error.WriteLine(prefix + failure);
}

Console.WriteLine(failures.Count == 0
    ? $"All dependency ranges in {packages.Count} packages are as intended."
    : $"{failures.Count} problems: dependencies without their intended range, or docs/compatibility.md out of date.");

return failures.Count == 0 ? 0 : 1;

static string? CheckSibling(Dependency dependency, string packageVersion)
{
    var release = Regex.Match(packageVersion, @"^(?<major>\d+)\.(?<minor>\d+)\.");
    var expected = $"[{packageVersion}, {release.Groups["major"].Value}.{int.Parse(release.Groups["minor"].Value) + 1}.0)";

    // A nuspec writes the range without the space.
    return dependency.Version.Replace(" ", "") == expected.Replace(" ", "")
        ? null
        : $"expected {expected}: this release up to the next minor";
}

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
    var followsFramework = dependency.Id.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase)
        || dependency.Id.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
        || dependency.Id.Equals("Npgsql", StringComparison.OrdinalIgnoreCase);

    return followsFramework && frameworkMajor.Success && int.Parse(frameworkMajor.Groups["major"].Value) != major
        ? $"expected version {frameworkMajor.Groups["major"].Value}.x to match the target framework"
        : null;
}

static bool IsMinimumOnly(string id) =>
    id.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase)
    || id is "Microsoft.Data.SqlClient" or "MySqlConnector" or "Azure.Identity" or "Microsoft.Identity.Client";

static string? CheckMinimum(Dependency dependency)
{
    // A nuspec writes "[x.y.z, )" as the bare minimum "x.y.z".
    if (!Regex.IsMatch(dependency.Version, @"^[0-9][^,\[\]()\s]*$"))
    {
        return "expected a minimum version only (no upper bound)";
    }

    var frameworkMajor = Regex.Match(dependency.Framework, @"^net(?<major>\d+)\.\d+$");
    return dependency.Id.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase)
        && frameworkMajor.Success && int.Parse(frameworkMajor.Groups["major"].Value) != Major(dependency.Version)
        ? $"expected a minimum of {frameworkMajor.Groups["major"].Value}.x, the target framework's own major"
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
