using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for 'import type' parsing feature that enables type-only imports.
/// </summary>
public class ImportTypeTests
{
    [Fact]
    public void ImportType_StatementLevel_ParsesCorrectly()
    {
        var source = """
            import type { MyType } from './types';
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.Parse();

        Assert.Single(statements);
        var import = Assert.IsType<Stmt.Import>(statements[0]);
        Assert.True(import.IsTypeOnly);
        Assert.NotNull(import.NamedImports);
        Assert.Single(import.NamedImports);
        Assert.Equal("MyType", import.NamedImports[0].Imported.Lexeme);
    }

    [Fact]
    public void ImportType_InlineSpecifier_ParsesCorrectly()
    {
        var source = """
            import { type MyType, myFunction } from './module';
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.Parse();

        Assert.Single(statements);
        var import = Assert.IsType<Stmt.Import>(statements[0]);
        Assert.False(import.IsTypeOnly); // Statement level is not type-only
        Assert.NotNull(import.NamedImports);
        Assert.Equal(2, import.NamedImports.Count);

        // First specifier is type-only
        Assert.True(import.NamedImports[0].IsTypeOnly);
        Assert.Equal("MyType", import.NamedImports[0].Imported.Lexeme);

        // Second specifier is not type-only
        Assert.False(import.NamedImports[1].IsTypeOnly);
        Assert.Equal("myFunction", import.NamedImports[1].Imported.Lexeme);
    }

    [Fact]
    public void ImportType_MultipleInlineSpecifiers_ParsesCorrectly()
    {
        var source = """
            import { type TypeA, type TypeB, valueC } from './module';
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.Parse();

        Assert.Single(statements);
        var import = Assert.IsType<Stmt.Import>(statements[0]);
        Assert.Equal(3, import.NamedImports!.Count);

        Assert.True(import.NamedImports[0].IsTypeOnly);
        Assert.True(import.NamedImports[1].IsTypeOnly);
        Assert.False(import.NamedImports[2].IsTypeOnly);
    }

    [Fact]
    public void ImportType_WithAlias_ParsesCorrectly()
    {
        var source = """
            import { type Foo as Bar } from './module';
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.Parse();

        Assert.Single(statements);
        var import = Assert.IsType<Stmt.Import>(statements[0]);
        Assert.Single(import.NamedImports!);
        Assert.True(import.NamedImports[0].IsTypeOnly);
        Assert.Equal("Foo", import.NamedImports[0].Imported.Lexeme);
        Assert.Equal("Bar", import.NamedImports[0].LocalName?.Lexeme);
    }

    [Fact]
    public void ImportType_NamespaceImport_ParsesCorrectly()
    {
        var source = """
            import type * as Types from './types';
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.Parse();

        Assert.Single(statements);
        var import = Assert.IsType<Stmt.Import>(statements[0]);
        Assert.True(import.IsTypeOnly);
        Assert.NotNull(import.NamespaceImport);
        Assert.Equal("Types", import.NamespaceImport.Lexeme);
    }

    [Fact]
    public void ImportType_DefaultImport_ParsesCorrectly()
    {
        var source = """
            import type Default from './module';
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.Parse();

        Assert.Single(statements);
        var import = Assert.IsType<Stmt.Import>(statements[0]);
        Assert.True(import.IsTypeOnly);
        Assert.NotNull(import.DefaultImport);
        Assert.Equal("Default", import.DefaultImport.Lexeme);
    }

    [Fact]
    public void ImportType_RegularImport_NotTypeOnly()
    {
        var source = """
            import { foo } from './module';
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.Parse();

        Assert.Single(statements);
        var import = Assert.IsType<Stmt.Import>(statements[0]);
        Assert.False(import.IsTypeOnly);
        Assert.False(import.NamedImports![0].IsTypeOnly);
    }
}
