// Fails if .github/dependabot.yml's list of banded packages disagrees with the project files. Usage:
//
//   dotnet run scripts/check-dependabot.cs
//
// A package is banded when any project declares it with a version range ("[8.0.0, 9.0.0)"), an MSBuild
// property ("$(TenantryCoreVersion)"), or different versions in different places (one per target framework).
// Those versions are set by hand: a range's floor is the oldest version supported, and a per-framework
// version must stay in its framework's band, which Dependabot cannot do. Dependabot ignores banded packages
// and maintains every package declared with one exact version everywhere.
//
// The banded list is the run of `- dependency-name:` entries with no `update-types` between the
// "# Banded packages: begin" and "# Banded packages: end" comments in the nuget entry.
using System.Text.RegularExpressions;
using System.Xml.Linq;

const string ConfigPath = ".github/dependabot.yml";
const string Begin = "# Banded packages: begin";
const string End = "# Banded packages: end";

var declarations = Directory
    .EnumerateFiles(".", "*.*", SearchOption.AllDirectories)
    .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(path) is "Directory.Build.props" or "Directory.Build.targets")
    .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".git"))
    .SelectMany(path => XDocument.Load(path)
        .Descendants()
        // References inside a <Target> (Directory.Build.targets' lane) rewrite existing items; they declare nothing.
        .Where(element => element.Name.LocalName == "PackageReference"
            && !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "Target"))
        .Select(element => (
            Id: (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update"),
            Version: (string?)element.Attribute("Version") ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value,
            Path: path)))
    .Where(declaration => declaration.Id is not null && declaration.Version is not null)
    .ToList();

var banded = declarations
    .GroupBy(declaration => declaration.Id!, StringComparer.OrdinalIgnoreCase)
    .Where(group => group.Any(declaration => IsSetByHand(declaration.Version!))
        || group.Select(declaration => declaration.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
    .Select(group => group.Key)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

var lines = File.ReadAllLines(ConfigPath);
var begin = Array.FindIndex(lines, line => line.Trim() == Begin);
var end = Array.FindIndex(lines, line => line.Trim() == End);
if (begin < 0 || end < begin)
{
    Console.Error.WriteLine($"{ConfigPath}: missing the '{Begin}' / '{End}' comments around the banded packages.");
    return 1;
}

var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
for (var index = begin + 1; index < end; index++)
{
    var match = Regex.Match(lines[index], @"^\s*-\s*dependency-name:\s*""?([^""\s]+)""?\s*$");
    if (!match.Success) continue;

    if (index + 1 < end && lines[index + 1].TrimStart().StartsWith("update-types", StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"{ConfigPath}:{index + 1}: a banded package is ignored entirely, without update-types.");
        return 1;
    }

    listed.Add(match.Groups[1].Value);
}

var missing = banded.Except(listed, StringComparer.OrdinalIgnoreCase).Order().ToList();
var stale = listed.Except(banded, StringComparer.OrdinalIgnoreCase).Order().ToList();

foreach (var id in missing)
{
    var where = declarations.Where(declaration => string.Equals(declaration.Id, id, StringComparison.OrdinalIgnoreCase))
        .Select(declaration => $"{declaration.Version} in {declaration.Path}").Distinct();
    Console.Error.WriteLine($"Banded but not ignored by Dependabot: {id} ({string.Join("; ", where)})");
}

foreach (var id in stale)
{
    Console.Error.WriteLine($"Ignored by Dependabot but not banded (declared with one exact version, or not at all): {id}");
}

if (missing.Count > 0 || stale.Count > 0)
{
    Console.Error.WriteLine($"Update the banded packages in {ConfigPath}.");
    return 1;
}

Console.WriteLine($"{ConfigPath}: {listed.Count} banded packages ignored; the other {declarations.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() - listed.Count} are maintained.");
return 0;

static bool IsSetByHand(string version) =>
    version.Contains('[') || version.Contains('(') || version.Contains("$(", StringComparison.Ordinal) || version.Contains('*');
