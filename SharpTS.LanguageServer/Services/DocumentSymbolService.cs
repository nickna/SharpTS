using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using SharpTS.Parsing;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Builds the outline a client shows for a file: its declarations, nested as they are written.
/// </summary>
/// <remarks>
/// Ranges come from the parser's <see cref="SpanTable"/>, so a symbol covers exactly the text its
/// declaration occupies. A declaration the parser did not record a span for is skipped rather than
/// given an invented range — an outline entry that scrolls to the wrong place is worse than one
/// that is missing.
/// </remarks>
public sealed class DocumentSymbolService
{
    /// <summary>
    /// Parses <paramref name="text"/> and returns its outline. Returns an empty list when the file
    /// cannot be parsed, since a client asks for symbols on every keystroke and a half-typed
    /// declaration is the normal case, not an error.
    /// </summary>
    public IReadOnlyList<DocumentSymbol> GetSymbols(string uri, string text)
    {
        var document = new SourceDocument(uri, text);

        List<Stmt> statements;
        try
        {
            var parsed = new Parser(new Lexer(text).ScanTokens())
                .WithSourceDocument(document)
                .Parse();
            statements = parsed.Statements;
        }
        catch
        {
            return [];
        }

        return Build(statements, document);
    }

    private static List<DocumentSymbol> Build(IEnumerable<Stmt> statements, SourceDocument document)
    {
        var symbols = new List<DocumentSymbol>();

        foreach (var statement in statements)
        {
            // `export class C {}` is one statement wrapping another; the declaration inside is what
            // the outline should show, and it carries the span.
            var declaration = statement is Stmt.Export export && export.Declaration is not null
                ? export.Declaration
                : statement;

            if (Describe(declaration, document) is { } symbol)
                symbols.Add(symbol);
        }

        return symbols;
    }

    private static DocumentSymbol? Describe(Stmt statement, SourceDocument document)
    {
        return statement switch
        {
            Stmt.Class c => Make(c, c.Name, SymbolKind.Class, document, ClassMembers(c, document)),
            Stmt.Function f => Make(f, f.Name, SymbolKind.Function, document),
            Stmt.Interface i => Make(i, i.Name, SymbolKind.Interface, document),
            Stmt.Enum e => Make(e, e.Name, SymbolKind.Enum, document, EnumMembers(e, document)),
            Stmt.TypeAlias t => Make(t, t.Name, SymbolKind.Class, document),
            Stmt.Namespace n => Make(n, n.Name, SymbolKind.Namespace, document, Build(n.Members, document)),
            Stmt.Var v => Make(v, v.Name, SymbolKind.Variable, document),
            Stmt.Const c => Make(c, c.Name, SymbolKind.Constant, document),
            _ => null,
        };
    }

    /// <summary>
    /// Class members are parsed inside the class body rather than through the statement dispatcher,
    /// so they carry no span of their own; their name token locates them.
    /// </summary>
    private static List<DocumentSymbol> ClassMembers(Stmt.Class declaration, SourceDocument document)
    {
        var members = new List<DocumentSymbol>();

        foreach (var method in declaration.Methods)
            AddFromToken(members, method.Name, method.Name.Lexeme == "constructor" ? SymbolKind.Constructor : SymbolKind.Method, document);

        foreach (var field in declaration.Fields)
            AddFromToken(members, field.Name, SymbolKind.Field, document);

        foreach (var accessor in declaration.Accessors ?? [])
            AddFromToken(members, accessor.Name, SymbolKind.Property, document);

        return members;
    }

    private static List<DocumentSymbol> EnumMembers(Stmt.Enum declaration, SourceDocument document)
    {
        var members = new List<DocumentSymbol>();
        foreach (var member in declaration.Members)
            AddFromToken(members, member.Name, SymbolKind.EnumMember, document);
        return members;
    }

    private static void AddFromToken(List<DocumentSymbol> into, Token name, SymbolKind kind, SourceDocument document)
    {
        if (name.Start < 0) return;

        var range = ToRange(name.Span, document);
        into.Add(new DocumentSymbol
        {
            Name = name.Lexeme,
            Kind = kind,
            Range = range,
            SelectionRange = range,
        });
    }

    private static DocumentSymbol? Make(
        Stmt statement, Token name, SymbolKind kind, SourceDocument document, List<DocumentSymbol>? children = null)
    {
        if (!document.Spans.TryGetSpan(statement, out var span) || span.IsHidden) return null;

        return new DocumentSymbol
        {
            Name = name.Lexeme,
            Kind = kind,
            Range = ToRange(span, document),
            // Selecting a symbol should put the cursor on its name, not on the whole declaration.
            SelectionRange = name.Start >= 0 ? ToRange(name.Span, document) : ToRange(span, document),
            Children = children is { Count: > 0 } ? new Container<DocumentSymbol>(children) : null,
        };
    }

    /// <summary>Converts a span to the zero-based line/character range LSP uses.</summary>
    private static Range ToRange(SourceSpan span, SourceDocument document)
    {
        var (startLine, startColumn) = document.Lines.ToPosition(span.Start);
        var (endLine, endColumn) = document.Lines.ToPosition(span.End);
        return new Range(startLine - 1, startColumn - 1, endLine - 1, endColumn - 1);
    }
}
