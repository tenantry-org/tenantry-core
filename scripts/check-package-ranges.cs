// Fails if any packed dependency lacks its intended version range. Usage, after dotnet pack:
//
//   dotnet run scripts/check-package-ranges.cs -- <directory containing the .nupkg files>
//
// The rules:
//   - A dependency on another package from the same directory (a sibling from this release) must be exact, [x.y.z],
//     when the sibling grants the dependant its internals (InternalsVisibleTo, read from the packed assembly), since
//     the dependant then works only with the same release (Tenantry Pro's integration and EF Core packages). Any other
//     sibling must take this release up to the next that may break it, [x.y.z, x.(y+1).0) in 0.x, so a consumer can
//     update one package of the set within that band. eng/common/Build.targets packs project references that way: a
//     reference marked PinExact="true" as exact, the others as a band.
//   - Microsoft.Extensions.* take a minimum only (">= x.y.z", no upper bound). Microsoft ships every
//     Microsoft.Extensions major for every supported framework and keeps it compatible, and current Azure SDKs need
//     Microsoft.Extensions 10.x even on net8.0, so a cap would stop consumers restoring. The minimum must be the
//     target framework's own major (net8.0 needs 8.x or later, not 10.x), except where an API arrived in a later
//     major that still supports the framework (HybridCache, in Microsoft.Extensions.Caching.Abstractions 9.0).
//   - Every other dependency must be bounded below its next breaking version: [a.b.c, (a+1).0.0), or for a 0.x
//     package, where a minor release may break (SemVer), [0.b.c, 0.(b+1).0) (Tenantry Core in beta). Microsoft.AspNetCore.*
//     and Microsoft.EntityFrameworkCore* must also match the target framework's major version (net10.0 depends on
//     10.x), because each framework's build is compiled against that major.
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length > 1 || args.Any(arg => arg.StartsWith("--", StringComparison.Ordinal)))
{
    Console.Error.WriteLine("Usage: dotnet run scripts/check-package-ranges.cs -- <package directory>");
    return 2;
}

var directory = args.FirstOrDefault() ?? "artifacts";
var packages = Directory.GetFiles(directory, "*.nupkg")
    .Where(path => !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
    .Select(ReadPackage)
    .ToList();

if (packages.Count == 0)
{
    Console.Error.WriteLine($"No packages found in '{directory}'.");
    return 1;
}

// One release's packages: a sibling's internals are read from the release that depends on it.
var mixed = packages.GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToList();
if (mixed.Count > 0)
{
    foreach (var group in mixed)
    {
        Console.Error.WriteLine($"'{directory}' holds more than one version of {group.Key} ({string.Join(", ", group.Select(p => p.Version))}); pack into an empty directory.");
    }

    return 1;
}

var siblings = packages.ToDictionary(package => package.Id, StringComparer.OrdinalIgnoreCase);
var failures = new List<string>();

foreach (var package in packages)
{
    foreach (var dependency in package.Dependencies)
    {
        var problem = siblings.TryGetValue(dependency.Id, out var sibling)
            ? CheckSibling(dependency, package, sibling)
            : dependency.Id.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase)
                ? CheckMinimum(dependency)
                : CheckBand(dependency);

        if (problem is not null)
        {
            failures.Add($"{package.Id} {package.Version} [{dependency.Framework}] depends on {dependency.Id} {dependency.Version}: {problem}");
        }
    }

    Console.WriteLine($"{package.Id} {package.Version}: {package.Dependencies.Count} dependencies checked");
}

// docs/compatibility.md names the target frameworks ("The packages target net8.0, net9.0 and net10.0", the list in
// bold or not, as each repository writes it); the list must match what the packages ship.
var compatibility = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory))!, "docs", "compatibility.md");
compatibility = File.Exists(compatibility) ? compatibility : Path.Combine("docs", "compatibility.md");
var documented = Regex.Match(File.ReadAllText(compatibility), @"The packages target (?:\*\*)?(?<list>net\d+\.\d+(?:(?:, | and )net\d+\.\d+)*)").Groups["list"].Value;
var documentedFrameworks = Regex.Matches(documented, @"net\d+\.\d+").Select(m => m.Value).ToHashSet();
var shippedFrameworks = packages.SelectMany(p => p.Dependencies).Select(d => d.Framework).Where(f => f.StartsWith("net", StringComparison.Ordinal)).ToHashSet();
if (documentedFrameworks.Count == 0)
{
    failures.Add($"{compatibility} has no sentence naming the target frameworks in the form \"The packages target net8.0, net9.0 and net10.0\", the list in bold or not.");
}
else if (!documentedFrameworks.SetEquals(shippedFrameworks))
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

static string? CheckSibling(Dependency dependency, Package package, Package sibling)
{
    if (sibling.InternalsVisibleTo.Contains(package.Id))
    {
        return dependency.Version == $"[{package.Version}]"
            ? null
            : $"expected exactly [{package.Version}], because {sibling.Id} grants it its internals (set PinExact=\"true\" on the ProjectReference)";
    }

    var expected = $"[{package.Version}, {NextBreaking(package.Version)})";

    // A nuspec writes the range without the space.
    return dependency.Version.Replace(" ", "") == expected.Replace(" ", "")
        ? null
        : $"expected {expected}, this release up to the next that may break it, because {sibling.Id} shares no internals with it (only a ProjectReference to a package that does takes PinExact=\"true\")";
}

static string? CheckBand(Dependency dependency)
{
    var match = Regex.Match(dependency.Version, @"^\[(?<min>[^,\[\]()\s]+), ?(?<max>[^,\[\]()\s]+)\)$");
    if (!match.Success)
    {
        return "expected a bounded range [min, max)";
    }

    var major = Major(match.Groups["min"].Value);
    var nextBreaking = NextBreaking(match.Groups["min"].Value);
    if (match.Groups["max"].Value != nextBreaking)
    {
        return $"expected the upper bound {nextBreaking} (the next breaking version)";
    }

    var frameworkMajor = Regex.Match(dependency.Framework, @"^net(?<major>\d+)\.\d+$");
    var followsFramework = dependency.Id.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase)
        || dependency.Id.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase);

    return followsFramework && frameworkMajor.Success && int.Parse(frameworkMajor.Groups["major"].Value) != major
        ? $"expected version {frameworkMajor.Groups["major"].Value}.x to match the target framework"
        : null;
}

static string? CheckMinimum(Dependency dependency)
{
    // A nuspec writes "[x.y.z, )" as the bare minimum "x.y.z".
    if (!Regex.IsMatch(dependency.Version, @"^[0-9][^,\[\]()\s]*$"))
    {
        return "expected a minimum version only (no upper bound)";
    }

    var frameworkMajor = Regex.Match(dependency.Framework, @"^net(?<major>\d+)\.\d+$");
    if (!dependency.Id.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase) || !frameworkMajor.Success)
    {
        return null;
    }

    var expected = int.Parse(frameworkMajor.Groups["major"].Value);
    expected = Math.Max(expected, FirstMajorWithTheApi(dependency.Id));
    return Major(dependency.Version) != expected
        ? $"expected a minimum of {expected}.x, the target framework's own major or the first with the API"
        : null;
}

// For a package whose API the packages use arrived in a later major than some framework's, which still supports it:
// that major (0 for the rest).
static int FirstMajorWithTheApi(string id) =>
    id.Equals("Microsoft.Extensions.Caching.Abstractions", StringComparison.OrdinalIgnoreCase) ? 9 : 0;   // HybridCache

static int Major(string version) => int.Parse(version.Split('.', '-')[0]);

// The first version that may break a dependant: the next major, or in 0.x the next minor.
static string NextBreaking(string version)
{
    var parts = version.Split('.', '-');
    return parts[0] == "0" ? $"0.{int.Parse(parts[1]) + 1}.0" : $"{int.Parse(parts[0]) + 1}.0.0";
}

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

    var id = Value("id");
    return new Package(id, Value("version"), dependencies, ReadInternalsVisibleTo(archive, id));
}

// The assembly names an assembly in the package grants its internals to (its InternalsVisibleTo attributes).
static HashSet<string> ReadInternalsVisibleTo(ZipArchive archive, string id)
{
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var entry = archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal)
        && e.Name.Equals($"{id}.dll", StringComparison.OrdinalIgnoreCase));
    if (entry is null)
    {
        return names;
    }

    using var assembly = new MemoryStream();
    using (var stream = entry.Open())
    {
        stream.CopyTo(assembly);
    }

    assembly.Position = 0;
    using var reader = new PEReader(assembly);
    var metadata = reader.GetMetadataReader();
    foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
    {
        var attribute = metadata.GetCustomAttribute(handle);
        if (attribute.Constructor.Kind != HandleKind.MemberReference
            || metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is not { Kind: HandleKind.TypeReference } parent
            || metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name) != "InternalsVisibleToAttribute")
        {
            continue;
        }

        var value = metadata.GetBlobReader(attribute.Value);
        value.ReadUInt16(); // The custom attribute prolog
        names.Add(value.ReadSerializedString()!.Split(',')[0].Trim());
    }

    return names;
}

internal sealed record Package(string Id, string Version, List<Dependency> Dependencies, HashSet<string> InternalsVisibleTo);

internal sealed record Dependency(string Framework, string Id, string Version);
