// Fails if the README, the docs, the samples or the src projects (their code, XML documentation comments and package
// descriptions) name an API, namespace or package that no longer exists. Usage:
//
//   dotnet run scripts/check-removed-names.cs
//
// The names are listed in eng/common/removed-names.txt (Tenantry Core's, shared with Tenantry Pro) and, if it exists,
// eng/removed-names.txt (the repository's own): one per line, optionally followed by " => " and what replaces it,
// which the error gives; "#" starts a comment. A name matches as a whole identifier or dotted name, so ITenantScoped
// matches "ITenantScoped<TKey>" and "Tenantry.ITenantScoped", but not "ITenantScopedThing". A dotted name (a package or
// namespace) also matches the start of a longer one (Tenantry.Pro.EfCore.MySql.Extensions), except a test assembly's
// name (Tenantry.Pro.EfCore.MySql.IntegrationTests). The changelog, which records the removals, is not checked.
using System.Text.RegularExpressions;

string[] lists = ["eng/common/removed-names.txt", "eng/removed-names.txt"];
string[] roots = ["README.md", "docs", "samples", "src"];
string[] extensions = [".md", ".cs", ".csproj", ".props", ".targets", ".json", ".http"];

var removed = lists
    .Where(File.Exists)
    .SelectMany(File.ReadAllLines)
    .Select(line => line.Split('#', 2)[0].Trim())
    .Where(line => line.Length > 0)
    .Select(line => line.Split("=>", 2, StringSplitOptions.TrimEntries))
    .Select(parts => (
        Name: parts[0],
        Replacement: parts.Length > 1 ? parts[1] : null,
        Pattern: new Regex(
            $@"(?<!\w){Regex.Escape(parts[0])}(?!\w)" + (parts[0].Contains('.') ? @"(?!\.(?:\w+\.)*\w*Tests\b)" : ""),
            RegexOptions.CultureInvariant)))
    .ToList();

if (removed.Count == 0)
{
    Console.Error.WriteLine($"No names in {string.Join(" or ", lists)}.");
    return 1;
}

var files = roots
    .SelectMany(root => File.Exists(root) ? [root]
        : Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : [])
    .Where(path => extensions.Contains(Path.GetExtension(path)))
    .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "node_modules"))
    .Order(StringComparer.Ordinal);

var found = 0;
foreach (var path in files)
{
    var lines = File.ReadAllLines(path);
    for (var i = 0; i < lines.Length; i++)
    {
        foreach (var (name, replacement, pattern) in removed.Where(entry => entry.Pattern.IsMatch(lines[i])))
        {
            found++;
            Console.Error.WriteLine($"{path.Replace('\\', '/')}:{i + 1}: {name} no longer exists"
                + (replacement is null ? "" : $"; use {replacement}") + ".");
        }
    }
}

if (found > 0)
{
    Console.Error.WriteLine($"{found} mention(s) of removed names (see scripts/check-removed-names.cs).");
    return 1;
}

Console.WriteLine($"No removed names in {string.Join(", ", roots)} ({removed.Count} names checked).");
return 0;
