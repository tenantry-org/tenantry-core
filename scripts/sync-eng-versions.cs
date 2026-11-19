// Gives eng/doc-snippets, which pins its own package versions, the exact version the central package files set.
// Usage:
//
//   dotnet run scripts/sync-eng-versions.cs [-- --check]
//
// eng/doc-snippets is built from a copy outside the repository, so it cannot take versions from
// Directory.Packages.props or eng/common/Packages.props and pins its own. Where a package has one exact version
// centrally and an exact version there too, the two must stay the same (scripts/check-dependabot.cs fails otherwise).
// Dependabot cannot keep them so: it cannot read the project, whose Tenantry references take their version from a
// property only scripts/check-doc-snippets.sh sets. So .github/workflows/dependabot-lock-files.yml runs this on
// Dependabot's NuGet branches, after the central version moved. A package with a range or a property centrally, or a
// different version per target framework, is banded and set by hand; it is left alone. Only the Version attribute
// changes, on the line that declares the package. With --check it changes nothing and fails if a version would change.
using System.Text.RegularExpressions;
using System.Xml.Linq;

string[] projects = ["eng/doc-snippets/DocSnippets.csproj"];
var check = args.Contains("--check");
var exact = new Regex(@"^\d+(\.\d+){1,3}(-[0-9A-Za-z.-]+)?$");

// The central versions: unconditional PackageVersion items (and in no conditional ItemGroup) with one exact version
var central = new[] { "Directory.Packages.props", "eng/common/Packages.props" }
    .SelectMany(path => XDocument.Load(path).Descendants())
    .Where(element => element.Name.LocalName == "PackageVersion")
    .Select(element => (
        Id: (string?)element.Attribute("Include"),
        Version: (string?)element.Attribute("Version"),
        Conditional: element.AncestorsAndSelf().Any(e => e.Attribute("Condition") is not null)))
    .Where(item => item.Id is not null && item.Version is not null)
    .GroupBy(item => item.Id!, StringComparer.OrdinalIgnoreCase)
    .Where(group => group.Count() == 1 && !group.Single().Conditional && exact.IsMatch(group.Single().Version!))
    .ToDictionary(group => group.Key, group => group.Single().Version!, StringComparer.OrdinalIgnoreCase);

var changes = 0;
foreach (var project in projects)
{
    var text = File.ReadAllText(project);
    var updated = text;
    foreach (var reference in XDocument.Parse(text).Descendants().Where(e => e.Name.LocalName == "PackageReference"))
    {
        var id = (string?)reference.Attribute("Include");
        var version = (string?)reference.Attribute("Version");
        if (id is null || version is null || !exact.IsMatch(version)
            || !central.TryGetValue(id, out var wanted) || wanted == version)
        {
            continue;
        }

        var line = new Regex($@"(<PackageReference\s+Include=""{Regex.Escape(id)}""\s+Version="")({Regex.Escape(version)})("")");
        if (line.Matches(updated).Count != 1)
        {
            Console.Error.WriteLine($"{project}: cannot find the one line that declares {id} {version}.");
            return 1;
        }

        updated = line.Replace(updated, $"${{1}}{wanted}${{3}}");
        Console.WriteLine($"{project}: {id} {version} -> {wanted}");
        changes++;
    }

    if (!check && updated != text)
    {
        File.WriteAllText(project, updated);
    }
}

if (changes == 0)
{
    Console.WriteLine("eng/doc-snippets' exact versions match the central package files.");
}
else if (check)
{
    Console.Error.WriteLine("Run dotnet run scripts/sync-eng-versions.cs to update them.");
    return 1;
}

return 0;
