using System.Text.Json;
using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SharpTS.LanguageServer.Services;

/// <summary>Completion, hover, and navigation backed by the generated GUI control contract.</summary>
public sealed class GuiContractService
{
    private sealed record Prop(string Name, string Type, string Documentation, string[] EnumValues);
    private sealed record Control(string Kind, string Documentation, Prop[] Props);
    private sealed record Contract(string DeclarationPath, Control[] Controls);

    public CompletionList? Completion(string? documentPath, string text, int line, int character)
    {
        Contract? contract = Load(documentPath);
        string? before = LinePrefix(text, line, character);
        if (contract is null || before is null) return null;

        Match literal = Regex.Match(before, @"<(?<tag>[A-Z]\w*)\b[^>]*\b(?<prop>\w+)\s*=\s*[\""'](?<value>[^\""']*)$");
        if (literal.Success)
        {
            Control? control = contract.Controls.FirstOrDefault(item => item.Kind == literal.Groups["tag"].Value);
            Prop? prop = control?.Props.FirstOrDefault(item => item.Name == literal.Groups["prop"].Value);
            if (prop is null || prop.EnumValues.Length == 0) return null;
            string prefix = literal.Groups["value"].Value;
            return new CompletionList(prop.EnumValues.Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(value => new CompletionItem { Label = value, InsertText = value, Kind = CompletionItemKind.EnumMember, Detail = prop.Type }));
        }

        Match opening = Regex.Match(before, @"<(?<prefix>[A-Z]\w*)?$");
        if (opening.Success)
        {
            string prefix = opening.Groups["prefix"].Value;
            return new CompletionList(contract.Controls.Where(control => control.Kind != "Fragment" && control.Kind.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(control => new CompletionItem
                {
                    Label = control.Kind,
                    InsertText = control.Kind,
                    Kind = CompletionItemKind.Class,
                    Detail = "@sharpts/gui control",
                    Documentation = new StringOrMarkupContent(control.Documentation),
                }));
        }

        Match inside = Regex.Match(before, @"<(?<tag>[A-Z]\w*)\b(?<body>[^>]*)$");
        if (!inside.Success) return null;
        Control? selected = contract.Controls.FirstOrDefault(control => control.Kind == inside.Groups["tag"].Value);
        if (selected is null) return null;
        string body = inside.Groups["body"].Value;
        string prefixProp = Regex.Match(body, @"(?<prefix>\w*)$").Groups["prefix"].Value;
        var written = Regex.Matches(body, @"\b(?<name>\w+)\s*=").Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        return new CompletionList(selected.Props.Where(prop => !written.Contains(prop.Name) && prop.Name.StartsWith(prefixProp, StringComparison.OrdinalIgnoreCase))
            .Select(prop => new CompletionItem
            {
                Label = prop.Name,
                InsertText = prop.Name + "={$0}",
                InsertTextFormat = InsertTextFormat.Snippet,
                Kind = prop.Name.StartsWith("on", StringComparison.Ordinal) ? CompletionItemKind.Event : CompletionItemKind.Property,
                Detail = prop.Type,
                Documentation = new StringOrMarkupContent(prop.Documentation),
            }));
    }

    public Hover? Hover(string? documentPath, string text, int line, int character)
    {
        Contract? contract = Load(documentPath);
        string? lineText = GetLine(text, line);
        if (contract is null || lineText is null) return null;
        (string Word, int Start, int End) = WordAt(lineText, character);
        if (Word.Length == 0) return null;

        Control? control = contract.Controls.FirstOrDefault(item => item.Kind == Word);
        if (control is not null) return Markdown($"`{control.Kind}`\n\n{control.Documentation}");
        Match tag = Regex.Match(lineText[..Math.Min(Start, lineText.Length)], @"<(?<tag>[A-Z]\w*)\b[^>]*$");
        control = tag.Success ? contract.Controls.FirstOrDefault(item => item.Kind == tag.Groups["tag"].Value) : null;
        Prop? prop = control?.Props.FirstOrDefault(item => item.Name == Word);
        return prop is null ? null : Markdown($"`{prop.Name}: {prop.Type}`\n\n{prop.Documentation}");
    }

    public Location? Definition(string? documentPath, string text, int line, int character)
    {
        Contract? contract = Load(documentPath);
        string? lineText = GetLine(text, line);
        if (contract is null || lineText is null || !File.Exists(contract.DeclarationPath)) return null;
        (string word, _, _) = WordAt(lineText, character);
        Control? control = contract.Controls.FirstOrDefault(item => item.Kind == word);
        if (control is null) return null;
        string[] declarations = File.ReadAllLines(contract.DeclarationPath);
        int declarationLine = Array.FindIndex(declarations, value => value.Contains($"export const {word} =", StringComparison.Ordinal));
        if (declarationLine < 0) return null;
        int start = declarations[declarationLine].IndexOf(word, StringComparison.Ordinal);
        return new Location
        {
            Uri = DocumentUri.FromFileSystemPath(contract.DeclarationPath),
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                declarationLine, start, declarationLine, start + word.Length),
        };
    }

    private static Contract? Load(string? documentPath)
    {
        string? metadata = LocateMetadata(documentPath);
        if (metadata is null) return null;
        try
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(metadata));
            var controls = json.RootElement.GetProperty("controls").EnumerateArray().Select(control => new Control(
                control.GetProperty("kind").GetString()!,
                control.GetProperty("documentation").GetString() ?? string.Empty,
                control.GetProperty("props").EnumerateArray().Select(prop => new Prop(
                    prop.GetProperty("name").GetString()!,
                    prop.GetProperty("type").GetString()!,
                    prop.GetProperty("documentation").GetString() ?? string.Empty,
                    prop.TryGetProperty("enumValues", out JsonElement values) && values.ValueKind == JsonValueKind.Array
                        ? values.EnumerateArray().Select(value => value.GetString()!).ToArray()
                        : [])).ToArray())).ToArray();
            return new Contract(Path.Combine(Path.GetDirectoryName(metadata)!, "control-surface.generated.ts"), controls);
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    private static string? LocateMetadata(string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath)) return null;
        string? directory = Path.GetDirectoryName(Path.GetFullPath(documentPath));
        while (directory is not null)
        {
            string[] projects = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly)
                : [];
            string? guiProject = projects.FirstOrDefault(path => File.ReadAllText(path).Contains("SharpTS.Gui.Sdk", StringComparison.OrdinalIgnoreCase));
            if (guiProject is not null)
            {
                string obj = Path.Combine(directory, "obj");
                if (Directory.Exists(obj))
                {
                    string? built = Directory.EnumerateFiles(obj, "control-docs.generated.json", SearchOption.AllDirectories)
                        .FirstOrDefault(path => path.Contains("@sharpts", StringComparison.OrdinalIgnoreCase));
                    if (built is not null) return built;
                }
                Match version = Regex.Match(File.ReadAllText(guiProject), @"SharpTS\.Gui\.Sdk/(?<version>[^\""<]+)");
                if (version.Success)
                {
                    string packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
                    string cached = Path.Combine(packages, "sharpts.gui.sdk", version.Groups["version"].Value, "gui", "control-docs.generated.json");
                    if (File.Exists(cached)) return cached;
                }
                string? repository = directory;
                while (repository is not null)
                {
                    string source = Path.Combine(repository, "SharpTS.Gui.Sdk", "GuiPackage", "control-docs.generated.json");
                    if (File.Exists(source)) return source;
                    repository = Path.GetDirectoryName(repository);
                }
                return null;
            }
            directory = Path.GetDirectoryName(directory);
        }
        return null;
    }

    private static Hover Markdown(string value) => new()
    {
        Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = value }),
    };

    private static string? LinePrefix(string text, int line, int character)
    {
        string? value = GetLine(text, line);
        return value is null ? null : value[..Math.Min(character, value.Length)];
    }

    private static string? GetLine(string text, int line)
    {
        string[] lines = text.Split('\n');
        return line < 0 || line >= lines.Length ? null : lines[line].TrimEnd('\r');
    }

    private static (string Word, int Start, int End) WordAt(string text, int character)
    {
        int start = Math.Min(character, text.Length), end = start;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '$')) start--;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '$')) end++;
        return (text[start..end], start, end);
    }
}
