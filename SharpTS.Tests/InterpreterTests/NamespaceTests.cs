using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class NamespaceTests
{
    [Fact]
    public void BasicNamespaceWithFunction()
    {
        var code = @"
            namespace Foo {
                export function bar(): number { return 42; }
            }
            console.log(Foo.bar());
        ";
        Assert.Equal("42\n", TestHarness.RunInterpreted(code));
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
        Assert.Equal("3.14159\n", TestHarness.RunInterpreted(code));
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
        Assert.Equal("Hello\n", TestHarness.RunInterpreted(code));
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
        Assert.Equal("123\n", TestHarness.RunInterpreted(code));
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
        Assert.Equal("3\n", TestHarness.RunInterpreted(code));
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
        Assert.Equal("3\n", TestHarness.RunInterpreted(code));
    }

    [Fact]
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
        Assert.StartsWith("12.566", TestHarness.RunInterpreted(code));
    }

    [Fact]
    public void DeepNestedNamespaceClass()
    {
        var code = @"
            namespace Company.Products.Widgets {
                export class Button {
                    label: string;
                    constructor(l: string) { this.label = l; }
                }
            }
            let btn = new Company.Products.Widgets.Button(""Click me"");
            console.log(btn.label);
        ";
        Assert.Equal("Click me\n", TestHarness.RunInterpreted(code));
    }

    [Fact]
    public void NamespaceWithGenericClass()
    {
        var code = @"
            namespace Collections {
                export class Box<T> {
                    value: T;
                    constructor(v: T) { this.value = v; }
                }
            }
            let numBox = new Collections.Box<number>(42);
            console.log(numBox.value);
            let strBox = new Collections.Box<string>(""hello"");
            console.log(strBox.value);
        ";
        Assert.Equal("42\nhello\n", TestHarness.RunInterpreted(code));
    }

    [Fact]
    public void NamespaceClassInheritance()
    {
        var code = @"
            namespace Animals {
                export class Animal {
                    name: string;
                    constructor(n: string) { this.name = n; }
                }
                export class Dog extends Animal {
                    constructor(n: string) { super(n); }
                    bark(): string { return this.name + "" says woof!""; }
                }
            }
            let dog = new Animals.Dog(""Rex"");
            console.log(dog.bark());
        ";
        Assert.Equal("Rex says woof!\n", TestHarness.RunInterpreted(code));
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
        Assert.Equal("5\n20\n", TestHarness.RunInterpreted(code));
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
        Assert.Equal("1.0.0\n", TestHarness.RunInterpreted(code));
    }

    [Fact]
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
        Assert.Equal("30\n", TestHarness.RunInterpreted(code));
    }
}
