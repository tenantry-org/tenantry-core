// Fails if .github/dependabot.yml's lists of ignored packages disagree with the project files. Usage:
//
//   dotnet run scripts/check-dependabot.cs [-- --shared-from-core]
//
// A package is banded when it is declared (a central PackageVersion, or a version on a sample's or tool's
// reference) with a version range ("[8.0.0, 9.0.0)"), an MSBuild property ("$(TenantryCoreVersion)"), or different
// versions in different places (one per target framework). Those versions are set by hand: a range's floor is the
// oldest version supported, and a per-framework version must stay in its framework's band, which Dependabot cannot
// do. Dependabot ignores banded packages and maintains every package declared with one exact version everywhere.
// Only packages some project references count.
//
// The banded list is the run of `- dependency-name:` entries with no `update-types` between the
// "# Banded packages: begin" and "# Banded packages: end" comments in the nuget entry. With --shared-from-core (Tenantry
// Pro), the versions eng/common/Packages.props sets are Tenantry Core's to maintain, so the ones not banded must be
// listed, also without update-types, between "# Set in Tenantry Core: begin" and "# Set in Tenantry Core: end".
using System.Text.RegularExpressions;
using System.Xml.Linq;

const string ConfigPath = ".github/dependabot.yml";
const string SharedVersionsPath = "eng/common/Packages.props";
var sharedFromCore = args.Contains("--shared-from-core");

var elements = Directory
    .EnumerateFiles(".", "*.*", SearchOption.AllDirectories)
    .Where(path => Path.GetExtension(path) is ".csproj" or ".props" or ".targets")
    .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".git"))
    .SelectMany(path => XDocument.Load(path)
        .Descendants()
        // Items inside a <Target> (the latest lane in eng/common/Build.targets) rewrite existing items; they declare nothing.
        .Where(element => element.Name.LocalName is "PackageReference" or "PackageVersion"
            && !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "Target"))
        .Select(element => (
            Kind: element.Name.LocalName,
            Id: (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update"),
            Version: (string?)element.Attribute("Version") ?? (string?)element.Attribute("VersionOverride")
                ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value,
            Path: Path.GetRelativePath(".", path))))
    .Where(element => element.Id is not null)
    .ToList();

var referenced = elements.Where(element => element.Kind == "PackageReference").Select(element => element.Id!)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var declarations = elements.Where(element => element.Version is not null && referenced.Contains(element.Id!)).ToList();

var banded = declarations
    .GroupBy(declaration => declaration.Id!, StringComparer.OrdinalIgnoreCase)
    .Where(group => group.Any(declaration => IsSetByHand(declaration.Version!))
        || group.Select(declaration => declaration.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
    .Select(group => group.Key)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

var setInCore = declarations
    .Where(declaration => declaration.Path.Replace('\\', '/') == SharedVersionsPath && !banded.Contains(declaration.Id!))
    .Select(declaration => declaration.Id!)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

var lines = File.ReadAllLines(ConfigPath);
var failed = !Check("Banded packages", banded, "Banded but not ignored by Dependabot",
    "Ignored by Dependabot but not banded (declared with one exact version, or not referenced)", required: true);
failed |= !Check("Set in Tenantry Core", sharedFromCore ? setInCore : [],
    $"Set in {SharedVersionsPath}, which Tenantry Core maintains, but not ignored by Dependabot",
    $"Listed as set in Tenantry Core but not a single exact version in {SharedVersionsPath}", required: sharedFromCore);

if (failed)
{
    Console.Error.WriteLine($"Update the lists in {ConfigPath}.");
    return 1;
}

var maintained = declarations.Select(d => d.Id!).Distinct(StringComparer.OrdinalIgnoreCase).Count() - banded.Count - (sharedFromCore ? setInCore.Count : 0);
Console.WriteLine($"{ConfigPath}: {banded.Count} banded packages ignored{(sharedFromCore ? $", {setInCore.Count} set in Tenantry Core ignored" : "")}; the other {maintained} are maintained.");
return 0;

// Compares one list in dependabot.yml (between "# <name>: begin" and "# <name>: end") with what it should hold.
bool Check(string name, HashSet<string> expected, string missingMessage, string staleMessage, bool required)
{
    var begin = Array.FindIndex(lines, line => line.Trim() == $"# {name}: begin");
    var end = Array.FindIndex(lines, line => line.Trim() == $"# {name}: end");
    if (begin < 0 && end < 0 && !required)
    {
        return true;
    }

    if (begin < 0 || end < begin)
    {
        Console.Error.WriteLine(required
            ? $"{ConfigPath}: missing the '# {name}: begin' / '# {name}: end' comments around its list."
            : $"{ConfigPath}: has a '{name}' list, which only Tenantry Pro (--shared-from-core) keeps.");
        return false;
    }

    var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var index = begin + 1; index < end; index++)
    {
        var match = Regex.Match(lines[index], @"^\s*-\s*dependency-name:\s*""?([^""\s]+)""?\s*$");
        if (!match.Success) continue;

        if (index + 1 < end && lines[index + 1].TrimStart().StartsWith("update-types", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"{ConfigPath}:{index + 1}: a package in '{name}' must be ignored entirely, without update-types.");
            return false;
        }

        listed.Add(match.Groups[1].Value);
    }

    if (!required)
    {
        Console.Error.WriteLine($"{ConfigPath}: has a '{name}' list, which only Tenantry Pro (--shared-from-core) keeps.");
        return false;
    }

    var missing = expected.Except(listed, StringComparer.OrdinalIgnoreCase).Order().ToList();
    var stale = listed.Except(expected, StringComparer.OrdinalIgnoreCase).Order().ToList();
    foreach (var id in missing)
    {
        var where = declarations.Where(declaration => string.Equals(declaration.Id, id, StringComparison.OrdinalIgnoreCase))
            .Select(declaration => $"{declaration.Version} in {declaration.Path}").Distinct();
        Console.Error.WriteLine($"{missingMessage}: {id} ({string.Join("; ", where)})");
    }

    foreach (var id in stale)
    {
        Console.Error.WriteLine($"{staleMessage}: {id}");
    }

    return missing.Count == 0 && stale.Count == 0;
}

static bool IsSetByHand(string version) =>
    version.Contains('[') || version.Contains('(') || version.Contains("$(", StringComparison.Ordinal) || version.Contains('*');
