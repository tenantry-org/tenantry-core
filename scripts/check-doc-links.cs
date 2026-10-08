// Fails if a link in the docs or the repository's other Markdown files points at a file, page or heading that does
// not exist. Usage:
//
//   dotnet run scripts/check-doc-links.cs
//
// docs/ is published on tenantry.dev (the site's scripts/sync-docs.mjs), so its links are checked the way the site
// serves them: a relative link to another page (other.md#heading) must reach a page and heading in docs/; a relative
// link that leaves docs/ (../samples/X) must reach a file in the repository, which the site links to on GitHub; any
// other relative link inside docs/ (an image, a folder) would not be served, so it fails. Headings get the ids the site
// gives them: a trailing [#id], or else the heading's text slugged the way github-slugger does, with repeats numbered.
// README.md, CONTRIBUTING.md, RELEASING.md, RELEASE-CHECKLIST.md and the samples' READMEs are read on GitHub, so their
// relative links resolve from their own folder. eng/package-readme.md is shown on NuGet, which cannot follow relative
// links, so it may only use absolute ones.
//
// Absolute links are checked when they point into documentation this script can read: https://tenantry.dev/docs/<group>/
// for this repository's group, and https://github.com/tenantry-org/<repository>/blob|tree/<ref>/<path> for this
// repository. Links to the other repository's docs are checked if it is checked out beside this one
// (../tenantry-core or ../tenantry-pro) and skipped otherwise. Other URLs are not fetched.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

var group = Directory.Exists("src/Tenantry.Pro") ? "pro" : "core";
var repository = $"tenantry-{group}";
var otherGroup = group == "core" ? "pro" : "core";
var otherRoot = Path.Combine("..", $"tenantry-{otherGroup}");
var docRoots = new Dictionary<string, string> { [group] = "." };
if (Directory.Exists(Path.Combine(otherRoot, "docs"))) docRoots[otherGroup] = otherRoot;

string[] githubFiles = ["README.md", "CONTRIBUTING.md", "RELEASING.md", "RELEASE-CHECKLIST.md"];
var files = githubFiles.Where(File.Exists)
    .Concat(Directory.Exists("samples") ? Directory.EnumerateFiles("samples", "README.md", SearchOption.AllDirectories) : [])
    .Concat(File.Exists("eng/package-readme.md") ? ["eng/package-readme.md"] : [])
    .Concat(Directory.Exists("docs") ? Directory.EnumerateFiles("docs", "*.md", SearchOption.AllDirectories) : [])
    .Select(Normalize)
    .Where(path => !path.Split('/').Any(part => part is "bin" or "obj" or "node_modules"))
    .Distinct()
    .Order(StringComparer.Ordinal)
    .ToList();

var headings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
var errors = new List<string>();
var checkedLinks = 0;
var skipped = 0;

foreach (var path in files)
{
    foreach (var (line, target) in Links(File.ReadAllLines(path)))
    {
        checkedLinks++;
        var problem = Check(path, target);
        if (problem is null) continue;
        if (problem.Length == 0) { skipped++; continue; }
        errors.Add($"{path}:{line}: ({target}) {problem}");
    }
}

foreach (var error in errors) Console.Error.WriteLine(error);
Console.WriteLine($"Checked {checkedLinks} links in {files.Count} files"
    + (skipped > 0 ? $"; skipped {skipped} into tenantry-{otherGroup}'s docs, which is not checked out beside this repository" : "")
    + $"; {errors.Count} broken.");
return errors.Count == 0 ? 0 : 1;

// Null when the link is fine, "" when it cannot be checked here, otherwise what is wrong.
string? Check(string page, string target)
{
    var (link, anchor) = SplitAnchor(target);

    if (link.Length == 0) return HeadingProblem(page, anchor);

    var site = Regex.Match(link, @"^https://tenantry\.dev/docs/(core|pro)(?:/(.*?))?/?$");
    if (site.Success)
    {
        if (!docRoots.TryGetValue(site.Groups[1].Value, out var root)) return "";
        var slug = site.Groups[2].Value;
        var candidates = slug.Length == 0
            ? new[] { "docs/README.md" }
            : [$"docs/{slug}.md", $"docs/{slug}/README.md"];
        var file = candidates.Select(c => Normalize(Path.Combine(root, c))).FirstOrDefault(File.Exists);
        return file is null ? $"no page docs/{slug}{(slug.Length == 0 ? "README" : "")}.md in tenantry-{site.Groups[1].Value}"
            : HeadingProblem(file, anchor);
    }

    var github = Regex.Match(link, @"^https://github\.com/tenantry-org/(tenantry-core|tenantry-pro)/(?:blob|tree)/[^/]+/(.+?)/?$");
    if (github.Success)
    {
        var root = github.Groups[1].Value == repository ? "."
            : Directory.Exists(Path.Combine("..", github.Groups[1].Value)) ? Path.Combine("..", github.Groups[1].Value) : null;
        if (root is null) return "";
        var file = Normalize(Path.Combine(root, Uri.UnescapeDataString(github.Groups[2].Value)));
        return Exists(file) ? HeadingProblem(file, anchor) : $"no {github.Groups[2].Value} in {github.Groups[1].Value}";
    }

    if (Regex.IsMatch(link, @"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase) || link.StartsWith("//")) return null;
    if (link.StartsWith('/')) return "a site path only works on the site; use a relative link or a full URL";

    if (page == "eng/package-readme.md") return "NuGet cannot follow relative links; use a full URL";

    var resolved = Normalize(Path.Combine(Path.GetDirectoryName(page) ?? "", Uri.UnescapeDataString(link)));
    if (resolved.StartsWith("../")) return $"points outside the repository";
    if (!Exists(resolved)) return $"no {resolved}";

    var inDocs = page.StartsWith("docs/");
    if (inDocs && resolved.StartsWith("docs/") && !resolved.EndsWith(".md"))
        return "the site serves only the Markdown pages in docs/; link to other files with a path that leaves docs/ (../)";

    return HeadingProblem(resolved, anchor);
}

string? HeadingProblem(string file, string anchor)
{
    if (anchor.Length == 0 || !file.EndsWith(".md") || Directory.Exists(file)) return null;
    if (!headings.TryGetValue(file, out var ids)) headings[file] = ids = HeadingIds(File.ReadAllLines(file));
    return ids.Contains(anchor) ? null : $"no heading #{anchor} in {file}";
}

static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

static string Normalize(string path)
{
    var parts = new List<string>();
    foreach (var part in path.Replace('\\', '/').Split('/'))
    {
        if (part is "" or ".") continue;
        if (part == ".." && parts.Count > 0 && parts[^1] != "..") parts.RemoveAt(parts.Count - 1);
        else parts.Add(part);
    }
    return string.Join('/', parts);
}

static (string Link, string Anchor) SplitAnchor(string target)
{
    var hash = target.IndexOf('#');
    return hash < 0 ? (target, "") : (target[..hash], Uri.UnescapeDataString(target[(hash + 1)..]));
}

// Every link and image target outside code, with its 1-based line number.
static IEnumerable<(int Line, string Target)> Links(string[] lines)
{
    var inline = new Regex(@"!?\[(?:[^\[\]]|\[[^\]]*\])*\]\(\s*<?([^)\s>]+)>?(?:\s+(?:""[^""]*""|'[^']*'|\([^)]*\)))?\s*\)");
    var reference = new Regex(@"^\s{0,3}\[[^\]]+\]:\s*<?([^\s>]+)>?");
    string? fence = null;
    for (var i = 0; i < lines.Length; i++)
    {
        var trimmed = lines[i].TrimStart();
        var marker = Regex.Match(trimmed, @"^(`{3,}|~{3,})");
        if (marker.Success)
        {
            if (fence is null) fence = marker.Value[..3];
            else if (marker.Value.StartsWith(fence)) fence = null;
            continue;
        }
        if (fence is not null) continue;

        var text = Regex.Replace(lines[i], @"(`+)(?:(?!\1).)+?\1", "");
        var definition = reference.Match(text);
        if (definition.Success) yield return (i + 1, definition.Groups[1].Value);
        foreach (Match match in inline.Matches(text)) yield return (i + 1, match.Groups[1].Value);
    }
}

// The ids the site gives a page's headings (fumadocs' remark-heading): a trailing [#id], or else the heading's text,
// with links, HTML, code marks and emphasis removed, slugged like github-slugger. Also ids set in HTML (<a id="x">).
static HashSet<string> HeadingIds(string[] lines)
{
    var ids = new HashSet<string>(StringComparer.Ordinal);
    var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
    var inFence = false;
    foreach (var line in lines)
    {
        if (line.TrimStart().StartsWith("```")) inFence = !inFence;
        if (inFence) continue;

        foreach (Match html in Regex.Matches(line, @"<[^>]*\b(?:id|name)\s*=\s*""([^""]+)""[^>]*>")) ids.Add(html.Groups[1].Value);

        var heading = Regex.Match(line, @"^#{1,6}\s+(.*?)\s*#*\s*$");
        if (!heading.Success) continue;

        var custom = Regex.Match(heading.Groups[1].Value, @"\s*\[#([^\]]+)\]\s*$");
        if (custom.Success)
        {
            ids.Add(custom.Groups[1].Value);
            continue;
        }

        var text = heading.Groups[1].Value;
        text = Regex.Replace(text, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
        // A code span keeps its text, "<TKey>" included; an HTML tag outside one is dropped.
        text = Regex.Replace(text, @"`([^`]*)`|<[^>]+>", match => match.Groups[1].Value);
        text = Regex.Replace(text, @"(\*\*|\*)(.+?)\1", "$2");

        var slug = Slug(text);
        var result = slug;
        if (occurrences.TryGetValue(slug, out var count))
        {
            do
            {
                count++;
                result = $"{slug}-{count}";
            }
            while (occurrences.ContainsKey(result));
            occurrences[slug] = count;
        }
        occurrences[result] = 0;
        ids.Add(result);
    }
    return ids;
}

// github-slugger: lower-cased; letters, marks, numbers, "-" and "_" kept; spaces become "-"; everything else dropped.
static string Slug(string value)
{
    var slug = new StringBuilder();
    foreach (var rune in value.ToLowerInvariant().EnumerateRunes())
    {
        if (rune.Value == ' ') slug.Append('-');
        else if (rune.Value is '-' or '_') slug.Append((char)rune.Value);
        else if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                 or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                 or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                 or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber)
            slug.Append(rune.ToString());
    }
    return slug.ToString();
}
