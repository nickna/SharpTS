using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for TypeScript import alias declarations (import X = Namespace.Member).
/// Runs against both interpreter and compiler where supported.
/// </summary>
public class ImportAliasTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BasicFunctionAlias(ExecutionMode mode)
    {
        var code = @"
            namespace Utils {
                export function greet(): string { return ""Hello""; }
            }
            import greet = Utils.greet;
            console.log(greet());
        ";
        Assert.Equal("Hello\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BasicVariableAlias(ExecutionMode mode)
    {
        var code = @"
            namespace Constants {
                export const PI: number = 3.14;
            }
            import PI = Constants.PI;
            console.log(PI);
        ";
        Assert.Equal("3.14\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ClassAlias(ExecutionMode mode)
    {
        var code = @"
            namespace Models {
                export class User {
                    name: string;
                    constructor(name: string) {
                        this.name = name;
                    }
                }
            }
            import User = Models.User;
            const u = new User(""Alice"");
            console.log(u.name);
        ";
        Assert.Equal("Alice\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedNamespacePath(ExecutionMode mode)
    {
        var code = @"
            namespace A {
                export namespace B {
                    export namespace C {
                        export const value: number = 42;
                    }
                }
            }
            import value = A.B.C.value;
            console.log(value);
        ";
        Assert.Equal("42\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedNamespacePathFunction(ExecutionMode mode)
    {
        var code = @"
            namespace Outer {
                export namespace Inner {
                    export function compute(): number { return 100; }
                }
            }
            import compute = Outer.Inner.compute;
            console.log(compute());
        ";
        Assert.Equal("100\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void InterfaceAlias_TypeOnly(ExecutionMode mode)
    {
        var code = @"
            namespace Types {
                export interface Person {
                    name: string;
                    age: number;
                }
            }
            import Person = Types.Person;
            const p: Person = { name: ""Bob"", age: 30 };
            console.log(p.name);
            console.log(p.age);
        ";
        Assert.Equal("Bob\n30\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EnumAlias(ExecutionMode mode)
    {
        var code = @"
            namespace Enums {
                export enum Color { Red, Green, Blue }
            }
            import Color = Enums.Color;
            const c: Color = Color.Green;
            console.log(c);
        ";
        Assert.Equal("1\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ImportAliasInsideNamespace(ExecutionMode mode)
    {
        var code = @"
            namespace Source {
                export function getValue(): number { return 999; }
            }
            namespace Consumer {
                import getValue = Source.getValue;
                export function use(): number { return getValue(); }
            }
            console.log(Consumer.use());
        ";
        Assert.Equal("999\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ExportedImportAlias(ExecutionMode mode)
    {
        var code = @"
            namespace Inner {
                export function helper(): string { return ""helped""; }
            }
            namespace Outer {
                export import helper = Inner.helper;
            }
            console.log(Outer.helper());
        ";
        Assert.Equal("helped\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MultipleAliases(ExecutionMode mode)
    {
        var code = @"
            namespace Math {
                export function add(a: number, b: number): number { return a + b; }
                export function sub(a: number, b: number): number { return a - b; }
                export const PI: number = 3.14159;
            }
            import add = Math.add;
            import sub = Math.sub;
            import PI = Math.PI;
            console.log(add(10, 5));
            console.log(sub(10, 5));
            console.log(PI);
        ";
        Assert.Equal("15\n5\n3.14159\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AliasNestedNamespace(ExecutionMode mode)
    {
        var code = @"
            namespace Root {
                export namespace Branch {
                    export function leaf(): string { return ""leaf value""; }
                }
            }
            import Branch = Root.Branch;
            console.log(Branch.leaf());
        ";
        Assert.Equal("leaf value\n", TestHarness.Run(code, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GenericClassAlias(ExecutionMode mode)
    {
        var code = @"
            namespace Collections {
                export class Box<T> {
                    value: T;
                    constructor(value: T) {
                        this.value = value;
                    }
                }
            }
            import Box = Collections.Box;
            const numBox = new Box<number>(42);
            const strBox = new Box<string>(""hello"");
            console.log(numBox.value);
            console.log(strBox.value);
        ";
        Assert.Equal("42\nhello\n", TestHarness.Run(code, mode));
    }
}
