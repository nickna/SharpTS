using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class NamespaceTests
{
    private static string Run(string code)
    {
        var output = new StringWriter();
        Console.SetOut(output);

        var lexer = new Lexer(code);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.Parse();

        var typeChecker = new TypeChecker();
        typeChecker.Check(statements);

        var interpreter = new Interpreter();
        interpreter.Interpret(statements);

        return output.ToString().Trim();
    }

    [Fact]
    public void BasicNamespaceWithFunction()
    {
        var code = @"
            namespace Foo {
                export function bar(): number { return 42; }
            }
            console.log(Foo.bar());
        ";
        Assert.Equal("42", Run(code));
    }

    [Fact]
    public void NamespaceWithVariable()
    {
        var code = @"
            namespace Constants {
                export const PI: number = 3.14159;
            }
            console.log(Constants.PI);
        ";
        Assert.Equal("3.14159", Run(code));
    }

    [Fact]
    public void NestedNamespace()
    {
        var code = @"
            namespace Outer {
                export namespace Inner {
                    export function greet(): string { return ""Hello""; }
                }
            }
            console.log(Outer.Inner.greet());
        ";
        Assert.Equal("Hello", Run(code));
    }

    [Fact]
    public void DottedNamespaceSyntax()
    {
        var code = @"
            namespace A.B.C {
                export let value: number = 123;
            }
            console.log(A.B.C.value);
        ";
        Assert.Equal("123", Run(code));
    }

    [Fact]
    public void DeclarationMerging()
    {
        var code = @"
            namespace Merged {
                export function foo(): number { return 1; }
            }
            namespace Merged {
                export function bar(): number { return 2; }
            }
            console.log(Merged.foo() + Merged.bar());
        ";
        Assert.Equal("3", Run(code));
    }

    [Fact]
    public void NamespaceWithEnum()
    {
        var code = @"
            namespace Config {
                export enum LogLevel { Debug, Info, Warn, Error }
            }
            console.log(Config.LogLevel.Error);
        ";
        Assert.Equal("3", Run(code));
    }

    [Fact(Skip = "Requires parser support for 'new Namespace.ClassName()' syntax")]
    public void NamespaceWithClass()
    {
        var code = @"
            namespace Shapes {
                export class Circle {
                    radius: number;
                    constructor(r: number) {
                        this.radius = r;
                    }
                    area(): number { return 3.14159 * this.radius * this.radius; }
                }
            }
            let c = new Shapes.Circle(2);
            console.log(c.area());
        ";
        // PI * 2 * 2 = 12.56636
        Assert.StartsWith("12.566", Run(code));
    }

    [Fact]
    public void NamespaceMultipleFunctions()
    {
        var code = @"
            namespace Utils {
                export function add(a: number, b: number): number { return a + b; }
                export function multiply(a: number, b: number): number { return a * b; }
            }
            console.log(Utils.add(2, 3));
            console.log(Utils.multiply(4, 5));
        ";
        var result = Run(code).Replace("\r\n", "\n");
        Assert.Equal("5\n20", result);
    }

    [Fact]
    public void DeeplyNestedDottedNamespace()
    {
        var code = @"
            namespace Company.Product.Feature.SubFeature {
                export const version: string = ""1.0.0"";
            }
            console.log(Company.Product.Feature.SubFeature.version);
        ";
        Assert.Equal("1.0.0", Run(code));
    }

    [Fact(Skip = "Dotted namespace merging requires enhanced type checker support")]
    public void NamespaceMergingWithDottedSyntax()
    {
        var code = @"
            namespace A.B {
                export let x: number = 10;
            }
            namespace A.B {
                export let y: number = 20;
            }
            console.log(A.B.x + A.B.y);
        ";
        Assert.Equal("30", Run(code));
    }
}
