// Writes the API reference (docs/api) from docfx's metadata. Run through scripts/generate-api-docs.sh, which
// produces the metadata first. Usage:
//
//   dotnet run scripts/api-docs.cs -- <docfx metadata directory> <output directory>
//
// One page per public type, named after its full name ("tenantry-core-itenantscope.md"), plus README.md, the
// index by package and namespace. The pages are plain markdown that reads the same on GitHub and on the site,
// whose docs are MDX: everything outside code is escaped for both, and every type or member name is code.
#:package YamlDotNet@18.1.0
#:property PublishAot=false

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: dotnet run scripts/api-docs.cs -- <docfx metadata directory> <output directory>");
    return 1;
}

var (input, output) = (args[0], args[1]);
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var items = new Dictionary<string, Item>(StringComparer.Ordinal);
var references = new Dictionary<string, Reference>(StringComparer.Ordinal);
foreach (var path in Directory.EnumerateFiles(input, "*.yml").Order(StringComparer.Ordinal))
{
    if (Path.GetFileName(path) == "toc.yml") continue;

    var document = deserializer.Deserialize<Document>(File.ReadAllText(path));
    foreach (var item in document.Items ?? []) items[item.Uid] = item;
    foreach (var reference in document.References ?? []) references.TryAdd(reference.Uid, reference);
}

string[] typeKinds = ["Class", "Struct", "Interface", "Enum", "Delegate"];
var types = items.Values.Where(item => typeKinds.Contains(item.Type)).OrderBy(item => item.FullName, StringComparer.Ordinal).ToList();

// File names: the full name without generic arity, lowercased; the arity is kept only where two types would collide.
var slugs = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var group in types.GroupBy(type => Slug(type.Uid, withArity: false)))
{
    foreach (var type in group) slugs[type.Uid] = group.Count() > 1 ? Slug(type.Uid, withArity: true) : group.Key;
}

// Every public parameter and type parameter needs a description: the compiler requires a doc comment on each
// public member (CS1591), but not its <param> and <typeparam> tags.
var undocumented = items.Values
    .Where(item => typeKinds.Contains(item.Type) || (item.Parent is not null && slugs.ContainsKey(item.Parent)))
    .SelectMany(item => (item.Syntax?.Parameters ?? []).Concat(item.Syntax?.TypeParameters ?? [])
        .Where(parameter => string.IsNullOrWhiteSpace(parameter.Description))
        .Select(parameter => $"{item.Source?.Path?.Replace("../../", "")}:{item.Source?.StartLine + 1}: {item.FullName}: `{parameter.Id}` has no description"))
    .Order(StringComparer.Ordinal)
    .ToList();
if (undocumented.Count > 0)
{
    foreach (var line in undocumented) Console.Error.WriteLine(line);
    Console.Error.WriteLine($"api-docs: {undocumented.Count} parameters have no description: add <param> or <typeparam> tags.");
    return 1;
}

if (Directory.Exists(output))
{
    foreach (var stale in Directory.EnumerateFiles(output, "*.md")) File.Delete(stale);
}
Directory.CreateDirectory(output);

foreach (var type in types) File.WriteAllText(Path.Combine(output, slugs[type.Uid] + ".md"), TypePage(type));
File.WriteAllText(Path.Combine(output, "README.md"), IndexPage());

Console.WriteLine($"api-docs: {types.Count} types from {types.Select(Package).Distinct().Count()} packages → {output}");
return 0;

string IndexPage()
{
    var page = new StringBuilder();
    page.AppendLine("# API reference");
    page.AppendLine();
    page.AppendLine("Every public type in the packages, generated from their XML documentation comments. The guides explain how");
    page.AppendLine("the pieces fit together; this reference is for the details of each type and member.");

    foreach (var package in types.GroupBy(Package).OrderBy(group => group.Key, StringComparer.Ordinal))
    {
        page.AppendLine();
        page.AppendLine($"## {package.Key}");

        foreach (var ns in package.GroupBy(type => type.Namespace).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            page.AppendLine();
            page.AppendLine($"### `{ns.Key}`");
            page.AppendLine();
            page.AppendLine("| Type | Kind | Summary |");
            page.AppendLine("|------|------|---------|");
            foreach (var type in ns)
            {
                page.AppendLine($"| [`{type.Name}`]({slugs[type.Uid]}.md) | {type.Type.ToLowerInvariant()} | {Cell(FirstSentence(type.Summary))} |");
            }
        }
    }

    return page.ToString();
}

string TypePage(Item type)
{
    var page = new StringBuilder();
    page.AppendLine($"# {Heading(type.Name)} {type.Type.ToLowerInvariant()}");
    page.AppendLine();
    page.AppendLine($"Namespace: `{type.Namespace}` · Package: `{Package(type)}` · [API reference](README.md)");
    AppendDocumentation(page, type);

    var syntax = type.Syntax;
    if (syntax?.Content is { } signature) AppendCode(page, WithoutVarianceInConstraints(signature));

    if (syntax?.TypeParameters is { Count: > 0 } typeParameters)
    {
        page.AppendLine();
        page.AppendLine("## Type parameters");
        page.AppendLine();
        foreach (var parameter in typeParameters) page.AppendLine($"- `{parameter.Id}`{Described(parameter.Description)}");
    }

    var bases = (type.Inheritance ?? []).Where(uid => uid != "System.Object").ToList();
    if (bases.Count > 0) AppendLine(page, $"Inherits {string.Join(" → ", bases.Select(TypeLink))}.");
    if (type.Implements is { Count: > 0 } implements) AppendLine(page, $"Implements {string.Join(", ", implements.Select(TypeLink))}.");
    if (type.DerivedClasses is { Count: > 0 } derived) AppendLine(page, $"Derived types: {string.Join(", ", derived.Select(TypeLink))}.");

    var members = (type.Children ?? []).Select(uid => items.GetValueOrDefault(uid)).OfType<Item>().ToList();
    if (type.Type == "Enum")
    {
        page.AppendLine();
        page.AppendLine("## Values");
        page.AppendLine();
        page.AppendLine("| Value | Description |");
        page.AppendLine("|-------|-------------|");
        foreach (var field in members) page.AppendLine($"| `{field.Syntax?.Content ?? field.Name}` | {Cell(Markdown(field.Summary))} |");
        return page.ToString();
    }

    foreach (var (kind, title) in new[]
             {
                 ("Constructor", "Constructors"), ("Field", "Fields"), ("Property", "Properties"),
                 ("Method", "Methods"), ("Event", "Events"), ("Operator", "Operators"),
             })
    {
        var ofKind = members.Where(member => member.Type == kind).ToList();
        if (ofKind.Count == 0) continue;

        page.AppendLine();
        page.AppendLine($"## {title}");
        foreach (var member in ofKind) AppendMember(page, member);
    }

    return page.ToString();
}

void AppendMember(StringBuilder page, Item member)
{
    page.AppendLine();
    page.AppendLine($"### {Heading(member.Name)}");
    AppendDocumentation(page, member);

    var syntax = member.Syntax;
    if (syntax?.Content is { } signature) AppendCode(page, signature);

    if (syntax?.TypeParameters is { Count: > 0 } typeParameters)
    {
        page.AppendLine();
        page.AppendLine("Type parameters:");
        page.AppendLine();
        foreach (var parameter in typeParameters) page.AppendLine($"- `{parameter.Id}`{Described(parameter.Description)}");
    }

    if (syntax?.Parameters is { Count: > 0 } parameters)
    {
        page.AppendLine();
        page.AppendLine("Parameters:");
        page.AppendLine();
        foreach (var parameter in parameters)
        {
            page.AppendLine($"- `{parameter.Id}` {TypeLink(parameter.Type)}{Described(parameter.Description)}");
        }
    }

    if (syntax?.Return is { Type: { } returnType } returns && member.Type != "Constructor")
    {
        var label = member.Type == "Property" ? "Value" : "Returns";
        AppendLine(page, $"{label}: {TypeLink(returnType)}{Described(returns.Description)}");
    }

    if (member.Exceptions is { Count: > 0 } exceptions)
    {
        page.AppendLine();
        page.AppendLine("Exceptions:");
        page.AppendLine();
        foreach (var exception in exceptions) page.AppendLine($"- {TypeLink(exception.Type)}{Described(exception.Description)}");
    }

    AppendRemarks(page, member);
}

void AppendDocumentation(StringBuilder page, Item item)
{
    if (!string.IsNullOrWhiteSpace(item.Summary)) AppendLine(page, Markdown(item.Summary));
    // A type's remarks and examples follow its summary; a member's follow its signature (AppendMember).
    if (typeKinds.Contains(item.Type)) AppendRemarks(page, item);
}

void AppendRemarks(StringBuilder page, Item item)
{
    if (!string.IsNullOrWhiteSpace(item.Remarks)) AppendLine(page, Markdown(item.Remarks));
    foreach (var example in item.Example ?? []) AppendLine(page, Markdown(example));
}

static void AppendLine(StringBuilder page, string text)
{
    page.AppendLine();
    page.AppendLine(text.Trim());
}

// docfx repeats a type parameter's variance where a constraint names it (where TKey : IEquatable<out TKey>), which
// is not C#: the variance belongs on the declaration only.
static string WithoutVarianceInConstraints(string signature)
{
    var where = signature.IndexOf(" where ", StringComparison.Ordinal);
    return where < 0
        ? signature
        : signature[..where] + Regex.Replace(signature[where..], @"(?<=[<,]\s*)(?:in|out)\s+", "");
}

static void AppendCode(StringBuilder page, string code)
{
    page.AppendLine();
    page.AppendLine("```csharp");
    page.AppendLine(code.Trim());
    page.AppendLine("```");
}

string Described(string? description) =>
    string.IsNullOrWhiteSpace(description) ? "" : ": " + Markdown(description).ReplaceLineEndings(" ").Trim();

// A type as it appears in text: a link when it has a page here, otherwise its short name as code.
string TypeLink(string uid)
{
    var target = items.GetValueOrDefault(uid);
    var name = target?.Name ?? references.GetValueOrDefault(uid)?.Name ?? ShortName(uid);

    // A constructed generic ("ITenantDescriptor{TKey}") links to its definition's page.
    var definition = target is not null ? uid : DefinitionUid(uid);
    if (definition is not null && slugs.TryGetValue(definition, out var slug)) return $"[`{name}`]({slug}.md)";

    return CoreTypeUrl(uid) is { } url ? $"[`{name}`]({url})" : $"`{name}`";
}

// A Tenantry Core type referenced from another repository's API (Pro's): its page in Core's reference on the site.
// Absolute, so it works on GitHub as well; Core's page names follow the same rule as these (Slug).
string? CoreTypeUrl(string uid)
{
    var reference = references.GetValueOrDefault(uid);
    var typeUid = reference?.CommentId?.StartsWith("T:", StringComparison.Ordinal) == true ? uid
        : reference?.CommentId is not null ? reference.Parent
        : null;
    typeUid = typeUid is null ? null : Regex.Replace(typeUid, @"\{.*\}$", "");
    return typeUid is not null && Regex.IsMatch(typeUid, @"^Tenantry\.(Core|EfCore|AspNetCore)\.")
        ? $"https://tenantry.dev/docs/core/api/{Slug(typeUid, withArity: false)}"
        : null;
}

string? DefinitionUid(string uid)
{
    var match = Regex.Match(uid, @"^([^{]+)\{(.+)\}$");
    if (!match.Success) return null;

    var arity = match.Groups[2].Value.Split(',').Length;
    var candidate = $"{match.Groups[1].Value}`{arity}";
    return slugs.ContainsKey(candidate) ? candidate : null;
}

static string ShortName(string uid)
{
    var withoutParameters = uid.Split('(')[0];
    var name = withoutParameters[(withoutParameters.LastIndexOf('.') + 1)..];
    return Regex.Replace(name, "`+\\d+", "");
}

// A member's link: its declaring type's page.
string MemberLink(string uid)
{
    if (slugs.ContainsKey(uid)) return TypeLink(uid);

    var member = items.GetValueOrDefault(uid);
    if (member?.Parent is { } parent && slugs.TryGetValue(parent, out var slug))
    {
        return $"[`{items[parent].Name}.{MemberName(member)}`]({slug}.md)";
    }

    return TypeLink(uid);
}

static string MemberName(Item member) => member.Name.Split('(')[0];

// docfx's HTML-flavoured comment text → markdown that renders the same on GitHub and in MDX.
string Markdown(string? html)
{
    if (string.IsNullOrWhiteSpace(html)) return "";

    var text = html.Replace("\r\n", "\n");
    var blocks = new List<string>();
    string Keep(string block)
    {
        blocks.Add(block);
        return $"\u0001{blocks.Count - 1}\u0002";
    }

    text = Regex.Replace(text, @"<pre><code[^>]*>(.*?)</code></pre>", match =>
        Keep($"\n\n```csharp\n{WebUtility.HtmlDecode(match.Groups[1].Value).Trim('\n')}\n```\n\n"), RegexOptions.Singleline);
    text = Regex.Replace(text, @"<xref href=""([^""]+)""[^>]*>(.*?)</xref>", match =>
    {
        var uid = Uri.UnescapeDataString(match.Groups[1].Value).Split('?')[0];
        var label = match.Groups[2].Value;
        var link = MemberLink(uid);
        return Keep(label.Length > 0 && link.Contains("](") ? $"[{Escape(WebUtility.HtmlDecode(label))}]({link.Split("](")[1]}" : link);
    }, RegexOptions.Singleline);
    text = Regex.Replace(text, @"<code[^>]*>(.*?)</code>", match => Keep($"`{WebUtility.HtmlDecode(match.Groups[1].Value)}`"), RegexOptions.Singleline);
    text = Regex.Replace(text, @"<a href=""([^""]+)""[^>]*>(.*?)</a>", match => Keep($"[{Escape(WebUtility.HtmlDecode(match.Groups[2].Value))}]({match.Groups[1].Value})"), RegexOptions.Singleline);
    text = Regex.Replace(text, @"</?(strong|b)>", "**");
    text = Regex.Replace(text, @"</?(em|i)>", "*");
    text = Regex.Replace(text, @"\s*<li>\s*", "\n- ");
    text = Regex.Replace(text, @"\s*</li>\s*|</?ul>", "\n");
    text = Regex.Replace(text, @"\s*</?p>\s*", "\n\n");

    // Everything left is prose: decode docfx's entities, then escape what MDX would read as JSX or expressions.
    text = Escape(WebUtility.HtmlDecode(text));
    text = Regex.Replace(text, "\u0001(\\d+)\u0002", match => blocks[int.Parse(match.Groups[1].Value)]);

    // One line per paragraph or list item; the comments wrap at the source's line length.
    var paragraphs = Regex.Split(text.Trim(), @"\n{2,}");
    return string.Join("\n\n", paragraphs.Select(paragraph => paragraph.StartsWith("```", StringComparison.Ordinal)
        ? paragraph
        : Regex.Replace(paragraph, @"\n(?!- )", " "))).Trim();
}

static string Escape(string prose) =>
    prose.Replace("<", "&lt;").Replace(">", "&gt;").Replace("{", "\\{").Replace("}", "\\}");

static string Heading(string name) => $"`{name}`";

static string Cell(string markdown) => markdown.ReplaceLineEndings(" ").Replace("|", "\\|").Trim();

string FirstSentence(string? summary)
{
    var text = Markdown(summary).ReplaceLineEndings(" ");
    var end = Regex.Match(text, @"\.(\s|$)(?![^`]*`)");
    return end.Success ? text[..(end.Index + 1)] : text;
}

static string Package(Item type) => type.Assemblies?.FirstOrDefault() ?? type.Namespace;

static string Slug(string uid, bool withArity)
{
    var name = withArity ? uid.Replace('`', '-') : Regex.Replace(uid, "`\\d+", "");
    return name.Replace('.', '-').ToLowerInvariant();
}

sealed class Document
{
    public List<Item>? Items { get; set; }
    public List<Reference>? References { get; set; }
}

sealed class Item
{
    public string Uid { get; set; } = "";
    public string? Parent { get; set; }
    public List<string>? Children { get; set; }
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Type { get; set; } = "";
    public string Namespace { get; set; } = "";
    public List<string>? Assemblies { get; set; }
    public string? Summary { get; set; }
    public string? Remarks { get; set; }
    public List<string>? Example { get; set; }
    public Syntax? Syntax { get; set; }
    public List<string>? Inheritance { get; set; }
    public List<string>? Implements { get; set; }
    public List<string>? DerivedClasses { get; set; }
    public List<ExceptionInfo>? Exceptions { get; set; }
    public SourceInfo? Source { get; set; }
}

sealed class SourceInfo
{
    public string? Path { get; set; }
    public int StartLine { get; set; }
}

sealed class Syntax
{
    public string? Content { get; set; }
    public List<Parameter>? Parameters { get; set; }
    public List<Parameter>? TypeParameters { get; set; }
    public Parameter? Return { get; set; }
}

sealed class Parameter
{
    public string? Id { get; set; }
    public string Type { get; set; } = "";
    public string? Description { get; set; }
}

sealed class ExceptionInfo
{
    public string Type { get; set; } = "";
    public string? Description { get; set; }
}

sealed class Reference
{
    public string Uid { get; set; } = "";
    public string? CommentId { get; set; }
    public string? Parent { get; set; }
    public string? Name { get; set; }
}
