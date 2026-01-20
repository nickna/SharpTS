using Xunit;
using SharpTS.Parsing;

namespace SharpTS.Tests.AutoAccessorTests;

/// <summary>
/// Parser tests for TypeScript 4.9+ auto-accessor class fields (accessor keyword).
/// </summary>
public class AutoAccessorParserTests
{
    private static ParseResult Parse(string code)
    {
        var lexer = new Lexer(code);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens, DecoratorMode.None);
        return parser.Parse();
    }

    [Fact]
    public void Parser_AbstractAccessor_ShouldReportError()
    {
        var code = @"
            class Base {
                abstract accessor value: number;
            }
        ";
        var result = Parse(code);
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Message.Contains("abstract", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parser_PrivateIdentifierAccessor_ShouldReportError()
    {
        var code = @"
            class Foo {
                accessor #privateValue: number = 0;
            }
        ";
        var result = Parse(code);
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Message.Contains("private identifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parser_BasicAccessor_ShouldParseSuccessfully()
    {
        var code = @"
            class Point {
                accessor x: number = 0;
                accessor y: number = 0;
            }
        ";
        var result = Parse(code);
        Assert.True(result.IsSuccess);

        // Verify the class has auto-accessors
        var classStmt = result.Statements[0] as Stmt.Class;
        Assert.NotNull(classStmt);
        Assert.NotNull(classStmt.AutoAccessors);
        Assert.Equal(2, classStmt.AutoAccessors.Count);
        Assert.Equal("x", classStmt.AutoAccessors[0].Name.Lexeme);
        Assert.Equal("y", classStmt.AutoAccessors[1].Name.Lexeme);
    }

    [Fact]
    public void Parser_StaticAccessor_ShouldParseSuccessfully()
    {
        var code = @"
            class Counter {
                static accessor count: number = 0;
            }
        ";
        var result = Parse(code);
        Assert.True(result.IsSuccess);

        var classStmt = result.Statements[0] as Stmt.Class;
        Assert.NotNull(classStmt);
        Assert.NotNull(classStmt.AutoAccessors);
        Assert.Single(classStmt.AutoAccessors);
        Assert.True(classStmt.AutoAccessors[0].IsStatic);
    }

    [Fact]
    public void Parser_ReadonlyAccessor_ShouldParseSuccessfully()
    {
        var code = @"
            class Immutable {
                readonly accessor id: string = ""abc"";
            }
        ";
        var result = Parse(code);
        Assert.True(result.IsSuccess);

        var classStmt = result.Statements[0] as Stmt.Class;
        Assert.NotNull(classStmt);
        Assert.NotNull(classStmt.AutoAccessors);
        Assert.Single(classStmt.AutoAccessors);
        Assert.True(classStmt.AutoAccessors[0].IsReadonly);
    }

    [Fact]
    public void Parser_OverrideAccessor_ShouldParseSuccessfully()
    {
        var code = @"
            class Base {
                accessor value: number = 10;
            }
            class Derived extends Base {
                override accessor value: number = 20;
            }
        ";
        var result = Parse(code);
        Assert.True(result.IsSuccess);

        var derivedClass = result.Statements[1] as Stmt.Class;
        Assert.NotNull(derivedClass);
        Assert.NotNull(derivedClass.AutoAccessors);
        Assert.Single(derivedClass.AutoAccessors);
        Assert.True(derivedClass.AutoAccessors[0].IsOverride);
    }

    [Fact]
    public void Parser_AccessorWithoutInitializer_ShouldParseSuccessfully()
    {
        var code = @"
            class Container {
                accessor data: any;
            }
        ";
        var result = Parse(code);
        Assert.True(result.IsSuccess);

        var classStmt = result.Statements[0] as Stmt.Class;
        Assert.NotNull(classStmt);
        Assert.NotNull(classStmt.AutoAccessors);
        Assert.Single(classStmt.AutoAccessors);
        Assert.Null(classStmt.AutoAccessors[0].Initializer);
    }

    [Fact]
    public void Parser_AccessorWithoutTypeAnnotation_ShouldParseSuccessfully()
    {
        var code = @"
            class Container {
                accessor value = 42;
            }
        ";
        var result = Parse(code);
        Assert.True(result.IsSuccess);

        var classStmt = result.Statements[0] as Stmt.Class;
        Assert.NotNull(classStmt);
        Assert.NotNull(classStmt.AutoAccessors);
        Assert.Single(classStmt.AutoAccessors);
        Assert.Null(classStmt.AutoAccessors[0].TypeAnnotation);
        Assert.NotNull(classStmt.AutoAccessors[0].Initializer);
    }
}
