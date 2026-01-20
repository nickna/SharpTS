using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Parser tests for ES2022 private class elements (#field, #method).
/// </summary>
public class PrivateFieldParserTests
{
    #region Helpers

    private static List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseOrThrow();
    }

    private static Stmt.Class ParseClass(string source)
    {
        var statements = Parse(source);
        Assert.Single(statements);
        return Assert.IsType<Stmt.Class>(statements[0]);
    }

    #endregion

    #region Private Field Declarations

    [Fact]
    public void PrivateField_BasicDeclaration()
    {
        var source = """
            class Foo {
                #value: number;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Fields);
        Assert.True(classStmt.Fields[0].IsPrivate);
        Assert.Equal("#value", classStmt.Fields[0].Name.Lexeme);
    }

    [Fact]
    public void PrivateField_WithInitializer()
    {
        var source = """
            class Foo {
                #count: number = 0;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Fields);
        Assert.True(classStmt.Fields[0].IsPrivate);
        Assert.NotNull(classStmt.Fields[0].Initializer);
    }

    [Fact]
    public void PrivateField_Static()
    {
        var source = """
            class Foo {
                static #instances: number = 0;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Fields);
        Assert.True(classStmt.Fields[0].IsPrivate);
        Assert.True(classStmt.Fields[0].IsStatic);
    }

    [Fact]
    public void PrivateField_MultipleFields()
    {
        var source = """
            class Point {
                #x: number;
                #y: number;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Equal(2, classStmt.Fields.Count);
        Assert.All(classStmt.Fields, f => Assert.True(f.IsPrivate));
    }

    #endregion

    #region Private Method Declarations

    [Fact]
    public void PrivateMethod_BasicDeclaration()
    {
        var source = """
            class Foo {
                #doWork(): void { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Methods);
        Assert.True(classStmt.Methods[0].IsPrivate);
        Assert.Equal("#doWork", classStmt.Methods[0].Name.Lexeme);
    }

    [Fact]
    public void PrivateMethod_WithParameters()
    {
        var source = """
            class Calculator {
                #add(a: number, b: number): number {
                    return a + b;
                }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Methods);
        Assert.True(classStmt.Methods[0].IsPrivate);
        Assert.Equal(2, classStmt.Methods[0].Parameters.Count);
    }

    [Fact]
    public void PrivateMethod_Static()
    {
        var source = """
            class Utils {
                static #helper(): void { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Methods);
        Assert.True(classStmt.Methods[0].IsPrivate);
        Assert.True(classStmt.Methods[0].IsStatic);
    }

    #endregion

    #region Private Element Access Expressions

    [Fact]
    public void PrivateField_AccessInMethod()
    {
        var source = """
            class Foo {
                #value: number = 0;
                getValue(): number {
                    return this.#value;
                }
            }
            """;
        var classStmt = ParseClass(source);
        // Just verify it parses without error
        Assert.Single(classStmt.Fields);
        Assert.Single(classStmt.Methods);
    }

    [Fact]
    public void PrivateField_AssignmentInMethod()
    {
        var source = """
            class Foo {
                #value: number = 0;
                setValue(v: number): void {
                    this.#value = v;
                }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Fields);
        Assert.Single(classStmt.Methods);
    }

    [Fact]
    public void PrivateMethod_CallInMethod()
    {
        var source = """
            class Foo {
                #helper(): void { }
                doWork(): void {
                    this.#helper();
                }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Equal(2, classStmt.Methods.Count);
    }

    [Fact]
    public void StaticPrivateField_AccessInStaticMethod()
    {
        var source = """
            class Counter {
                static #count: number = 0;
                static getCount(): number {
                    return Counter.#count;
                }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Single(classStmt.Fields);
        Assert.Single(classStmt.Methods);
    }

    #endregion

    #region Mixed Public and Private

    [Fact]
    public void MixedFields_PublicAndPrivate()
    {
        var source = """
            class Box {
                value: number = 0;
                #secret: number = 100;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Equal(2, classStmt.Fields.Count);

        var publicField = classStmt.Fields.First(f => !f.IsPrivate);
        var privateField = classStmt.Fields.First(f => f.IsPrivate);

        Assert.Equal("value", publicField.Name.Lexeme);
        Assert.Equal("#secret", privateField.Name.Lexeme);
    }

    [Fact]
    public void MixedMethods_PublicAndPrivate()
    {
        var source = """
            class Foo {
                #helper(): void { }
                doWork(): void { }
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Equal(2, classStmt.Methods.Count);

        var privateMethod = classStmt.Methods.First(m => m.IsPrivate);
        var publicMethod = classStmt.Methods.First(m => !m.IsPrivate);

        Assert.Equal("#helper", privateMethod.Name.Lexeme);
        Assert.Equal("doWork", publicMethod.Name.Lexeme);
    }

    [Fact]
    public void SameName_PublicAndPrivateCoexist()
    {
        var source = """
            class Box {
                value: number = 0;
                #value: number = 100;
            }
            """;
        var classStmt = ParseClass(source);
        Assert.Equal(2, classStmt.Fields.Count);

        var publicField = classStmt.Fields.First(f => f.Name.Lexeme == "value");
        var privateField = classStmt.Fields.First(f => f.Name.Lexeme == "#value");

        Assert.False(publicField.IsPrivate);
        Assert.True(privateField.IsPrivate);
    }

    #endregion

    #region Lexer Token Tests

    [Fact]
    public void Lexer_PrivateIdentifierToken()
    {
        var source = "#fieldName";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        Assert.Equal(2, tokens.Count); // PRIVATE_IDENTIFIER + EOF
        Assert.Equal(TokenType.PRIVATE_IDENTIFIER, tokens[0].Type);
        Assert.Equal("#fieldName", tokens[0].Lexeme);
    }

    [Fact]
    public void Lexer_MultiplePrivateIdentifiers()
    {
        var source = "#a #b #c";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        Assert.Equal(4, tokens.Count); // 3 PRIVATE_IDENTIFIER + EOF
        Assert.All(tokens.Take(3), t => Assert.Equal(TokenType.PRIVATE_IDENTIFIER, t.Type));
    }

    #endregion
}
