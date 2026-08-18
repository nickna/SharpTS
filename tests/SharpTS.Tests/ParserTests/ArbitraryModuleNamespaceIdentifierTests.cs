using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public class ArbitraryModuleNamespaceIdentifierTests
{
    private static List<Stmt> Parse(string source) =>
        new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();

    [Fact]
    public void StringNamedImportAndExportSpecifiersUseDecodedNamespaceNames()
    {
        var statements = Parse("""
            const empty = "empty";
            export { empty as "" };
            import { "" as localEmpty, "<X>" as valueX } from "./dep";
            """);

        var export = Assert.IsType<Stmt.Export>(statements[1]);
        Assert.Equal("", Assert.Single(export.NamedExports!).ExportedName!.Lexeme);

        var import = Assert.IsType<Stmt.Import>(statements[2]);
        Assert.Collection(import.NamedImports!,
            specifier =>
            {
                Assert.Equal("", specifier.Imported.Lexeme);
                Assert.Equal("localEmpty", specifier.LocalName!.Lexeme);
            },
            specifier =>
            {
                Assert.Equal("<X>", specifier.Imported.Lexeme);
                Assert.Equal("valueX", specifier.LocalName!.Lexeme);
            });
    }

    [Fact]
    public void StringNamedNamespaceReExportParses()
    {
        var export = Assert.IsType<Stmt.Export>(Assert.Single(Parse(
            "export * as \"valid name\" from \"./dep\";")));

        Assert.Equal("valid name", export.NamespaceExportName!.Lexeme);
        Assert.Equal(TokenType.STRING, export.NamespaceExportName.Type);
    }
}
