using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Services;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

/// <summary>
/// Covers the file outline served as <c>textDocument/documentSymbol</c>. Ranges come from the
/// parser's span table, so these assert positions rather than just names — an outline entry that
/// scrolls somewhere other than its declaration is the failure that matters.
/// </summary>
public class DocumentSymbolServiceTests
{
    private readonly DocumentSymbolService _symbols = new();

    private IReadOnlyList<DocumentSymbol> Symbols(string source) => _symbols.GetSymbols("test.ts", source);

    [Fact]
    public void TopLevelDeclarationsAppearInTheOutline()
    {
        var symbols = Symbols("""
            function greet(name: string): string { return name; }
            class Service {}
            interface Shape {}
            enum Color { Red }
            const answer = 42;
            """);

        Assert.Collection(symbols,
            s => Assert.Equal(("greet", SymbolKind.Function), (s.Name, s.Kind)),
            s => Assert.Equal(("Service", SymbolKind.Class), (s.Name, s.Kind)),
            s => Assert.Equal(("Shape", SymbolKind.Interface), (s.Name, s.Kind)),
            s => Assert.Equal(("Color", SymbolKind.Enum), (s.Name, s.Kind)),
            s => Assert.Equal(("answer", SymbolKind.Constant), (s.Name, s.Kind)));
    }

    /// <summary>
    /// A symbol's range covers its whole declaration, while its selection range covers just the
    /// name — that is what puts the cursor on the identifier when the outline is used to navigate.
    /// </summary>
    [Fact]
    public void RangesCoverTheDeclarationAndSelectionCoversTheName()
    {
        const string source = "function greet(): void {}\n";
        var symbol = Assert.Single(Symbols(source));

        Assert.Equal(new Position(0, 0), symbol.Range.Start);
        Assert.Equal(new Position(0, 25), symbol.Range.End);

        // "greet" starts at column 9 and is five characters long.
        Assert.Equal(new Position(0, 9), symbol.SelectionRange.Start);
        Assert.Equal(new Position(0, 14), symbol.SelectionRange.End);
    }

    [Fact]
    public void ExportedDeclarationsAreUnwrapped()
    {
        var symbols = Symbols("""
            export class Widget {}
            export function build(): void {}
            """);

        Assert.Collection(symbols,
            s => Assert.Equal(("Widget", SymbolKind.Class), (s.Name, s.Kind)),
            s => Assert.Equal(("build", SymbolKind.Function), (s.Name, s.Kind)));
    }

    [Fact]
    public void ClassMembersNestUnderTheirClass()
    {
        var symbols = Symbols("""
            class Service {
              count: number = 0;
              run(): number { return this.count; }
            }
            """);

        var service = Assert.Single(symbols);
        Assert.NotNull(service.Children);

        var names = service.Children!.Select(child => child.Name).ToArray();
        Assert.Contains("count", names);
        Assert.Contains("run", names);
    }

    [Fact]
    public void EnumMembersNestUnderTheirEnum()
    {
        var color = Assert.Single(Symbols("enum Color { Red, Green }\n"));

        Assert.NotNull(color.Children);
        Assert.Equal(["Red", "Green"], color.Children!.Select(child => child.Name));
    }

    [Fact]
    public void NamespaceMembersNestUnderTheirNamespace()
    {
        var outer = Assert.Single(Symbols("""
            namespace Outer {
              export function inner(): void {}
            }
            """));

        Assert.Equal(SymbolKind.Namespace, outer.Kind);
        Assert.Equal("inner", Assert.Single(outer.Children!).Name);
    }

    /// <summary>
    /// A client asks for symbols on every keystroke, so a half-typed file is the normal case and
    /// must not throw.
    /// </summary>
    [Fact]
    public void UnparseableInputYieldsNoSymbolsRatherThanThrowing()
    {
        Assert.Empty(Symbols("class {{{ ¯\\_(ツ)_/¯"));
    }

    [Fact]
    public void EmptyDocumentYieldsNoSymbols()
    {
        Assert.Empty(Symbols(""));
    }
}
