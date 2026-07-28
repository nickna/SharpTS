using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Documentation;
using SharpTS.Runtime.DotNet;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Backs hover and completion for SharpTS decorators. Text-based (works off the line at the
/// cursor, no AST position index needed yet), reusing the same CLR resolver as the interop
/// analyzer plus <see cref="XmlDocLoader"/> for .NET XML documentation.
/// </summary>
public sealed class DecoratorService
{
    private readonly Func<string, Type?> _resolve;
    private readonly Func<IEnumerable<string>>? _typeNamesProvider;
    private readonly XmlDocLoader _xmlDoc = new();
    private List<string>? _typeNameCache;

    public DecoratorService(Func<string, Type?>? resolve = null, Func<IEnumerable<string>>? typeNames = null)
    {
        _resolve = resolve ?? DotNetTypeRegistry.Resolve;
        _typeNamesProvider = typeNames;
    }

    // Known public type names from the project's referenced assemblies (cached — the loaded
    // set is fixed for the session). Empty when no resolver/loader supplies them.
    private List<string> KnownTypeNames()
        => _typeNameCache ??= (_typeNamesProvider?.Invoke() ?? Enumerable.Empty<string>())
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

    private static readonly Regex DecoratorRx =
        new(@"@(\w+)(?:\s*\(\s*""([^""]*)""\s*\))?", RegexOptions.Compiled);
    private static readonly Regex DecoratorPrefixRx =
        new(@"@(\w*)$", RegexOptions.Compiled);
    private static readonly Regex DotNetTypeStringRx =
        new(@"@DotNetType\(\s*""([^""]*)$", RegexOptions.Compiled);
    private static readonly Regex InsideDecoratorCallRx =
        new(@"@(\w+)\s*\([^)]*$", RegexOptions.Compiled);

    private sealed record Builtin(string Name, string Snippet, string Description, string Signature, string Param);

    private static readonly Builtin[] Builtins =
    {
        new("DotNetType", "DotNetType(\"$0\")", "Maps this TypeScript class to an external .NET type. Argument: the fully-qualified CLR type name (e.g. \"System.Text.StringBuilder\").", "DotNetType(typeName: string)", "typeName: string"),
        new("DotNetOverload", "DotNetOverload(\"$0\")", "Pins which .NET overload a method resolves to. Argument: a parameter type (e.g. \"int\").", "DotNetOverload(signature: string)", "signature: string"),
        new("DotNetMethod", "DotNetMethod(\"$0\")", "Binds this member to a specific .NET method name.", "DotNetMethod(methodName: string)", "methodName: string"),
        new("DotNetProperty", "DotNetProperty(\"$0\")", "Binds this member to a specific .NET property name.", "DotNetProperty(propertyName: string)", "propertyName: string"),
        new("DotNetField", "DotNetField(\"$0\")", "Binds this member to a specific .NET field name.", "DotNetField(fieldName: string)", "fieldName: string"),
        new("DotNetEvent", "DotNetEvent(\"$0\")", "Binds this member to a specific .NET event name.", "DotNetEvent(eventName: string)", "eventName: string"),
    };

    /// <summary>Hover for the decorator under the (0-based) cursor, or null.</summary>
    public Hover? Hover(string text, int line, int character)
    {
        var lineText = GetLine(text, line);
        if (lineText is null) return null;

        foreach (Match m in DecoratorRx.Matches(lineText))
        {
            if (character < m.Index || character > m.Index + m.Length) continue;

            string name = m.Groups[1].Value;
            string? arg = m.Groups[2].Success ? m.Groups[2].Value : null;

            // @DotNetType("X") → show the resolved CLR type + its XML doc.
            if (name == "DotNetType" && arg is not null)
            {
                Type? type;
                try { type = DotNetTypeRegistry.ResolveFriendly(arg, _resolve); }
                catch (ArgumentException) { type = null; }
                if (type is not null)
                    return Markdown(TypeMarkdown(type));
            }

            var builtin = Array.Find(Builtins, b => b.Name == name);
            if (builtin is not null)
                return Markdown($"**@{name}**\n\n{builtin.Description}");

            return null;
        }
        return null;
    }

    /// <summary>Completion: CLR type names inside <c>@DotNetType("…")</c>, or the builtin
    /// decorators after an <c>@</c>; null otherwise.</summary>
    public CompletionList? Completion(string text, int line, int character)
    {
        var lineText = GetLine(text, line);
        if (lineText is null) return null;
        if (character > lineText.Length) character = lineText.Length;
        var before = lineText[..character];

        // Inside @DotNetType("…") — offer CLR type names from referenced assemblies.
        var typeMatch = DotNetTypeStringRx.Match(before);
        if (typeMatch.Success)
        {
            string partial = typeMatch.Groups[1].Value;
            var items = KnownTypeNames()
                .Where(n => partial.Length == 0 || n.Contains(partial, StringComparison.OrdinalIgnoreCase))
                .Take(200)
                .Select(n => new CompletionItem { Label = n, Kind = CompletionItemKind.Class, InsertText = n });
            return new CompletionList(items, isIncomplete: true);
        }

        // After @ — offer the builtin decorators.
        var m = DecoratorPrefixRx.Match(before);
        if (!m.Success) return null;

        string prefix = m.Groups[1].Value;
        var builtinItems = Builtins
            .Where(b => prefix.Length == 0 || b.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(b => new CompletionItem
            {
                Label = b.Name,
                Kind = CompletionItemKind.Snippet,
                Detail = "SharpTS decorator",
                Documentation = new StringOrMarkupContent(b.Description),
                InsertText = b.Snippet,
                InsertTextFormat = InsertTextFormat.Snippet
            });

        return new CompletionList(builtinItems);
    }

    /// <summary>Signature help when the cursor is inside a builtin decorator call, or null.</summary>
    public SignatureHelp? SignatureHelp(string text, int line, int character)
    {
        var lineText = GetLine(text, line);
        if (lineText is null) return null;
        if (character > lineText.Length) character = lineText.Length;

        var m = InsideDecoratorCallRx.Match(lineText[..character]);
        if (!m.Success) return null;

        var b = Array.Find(Builtins, x => x.Name == m.Groups[1].Value);
        if (b is null) return null;

        return new SignatureHelp
        {
            ActiveSignature = 0,
            ActiveParameter = 0,
            Signatures = new[]
            {
                new SignatureInformation
                {
                    Label = b.Signature,
                    Documentation = new StringOrMarkupContent(b.Description),
                    Parameters = new[]
                    {
                        new ParameterInformation { Label = new ParameterInformationLabel(b.Param) }
                    }
                }
            }
        };
    }

    private string TypeMarkdown(Type type)
    {
        string md = $"```csharp\n{type.FullName}\n```";
        var summary = _xmlDoc.GetTypeSummary(type);
        if (!string.IsNullOrWhiteSpace(summary))
            md += $"\n\n{summary}";
        return md;
    }

    private static Hover Markdown(string value) => new()
    {
        Contents = new MarkedStringsOrMarkupContent(
            new MarkupContent { Kind = MarkupKind.Markdown, Value = value })
    };

    private static string? GetLine(string text, int line)
    {
        if (line < 0) return null;
        var lines = text.Split('\n');
        if (line >= lines.Length) return null;
        return lines[line].TrimEnd('\r');
    }
}
