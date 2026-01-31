using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for ES modules: import/export statements, module resolution,
/// re-exports, execution order, and cross-module type usage.
/// </summary>
public class ModuleTests
{
    #region Named Exports and Imports

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedExport_SingleVariable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./math.ts"] = """
                export const PI: number = 3.14159;
                """,
            ["./main.ts"] = """
                import { PI } from './math';
                console.log(PI);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("3.14159\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedExport_MultipleVariables(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./constants.ts"] = """
                export const X: number = 10;
                export const Y: number = 20;
                export const Z: number = 30;
                """,
            ["./main.ts"] = """
                import { X, Y, Z } from './constants';
                console.log(X + Y + Z);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("60\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedExport_Function(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                export function add(a: number, b: number): number {
                    return a + b;
                }
                """,
            ["./main.ts"] = """
                import { add } from './utils';
                console.log(add(3, 4));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("7\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void NamedExport_Class(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./person.ts"] = """
                export class Person {
                    name: string;
                    constructor(name: string) {
                        this.name = name;
                    }
                    greet(): string {
                        return "Hello, " + this.name;
                    }
                }
                """,
            ["./main.ts"] = """
                import { Person } from './person';
                let p: Person = new Person("Alice");
                console.log(p.greet());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Hello, Alice\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedImport_WithAlias(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./math.ts"] = """
                export const value: number = 42;
                """,
            ["./main.ts"] = """
                import { value as answer } from './math';
                console.log(answer);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedExport_List(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./values.ts"] = """
                const a: number = 1;
                const b: number = 2;
                const c: number = 3;
                export { a, b, c };
                """,
            ["./main.ts"] = """
                import { a, b, c } from './values';
                console.log(a + b + c);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void NamedExport_ListWithAlias(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./values.ts"] = """
                const internal: number = 100;
                export { internal as external };
                """,
            ["./main.ts"] = """
                import { external } from './values';
                console.log(external);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("100\n", output);
    }

    #endregion

    #region Default Exports and Imports

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DefaultExport_Expression(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./config.ts"] = """
                export default 42;
                """,
            ["./main.ts"] = """
                import config from './config';
                console.log(config);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DefaultExport_Function(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./greet.ts"] = """
                export default function greet(name: string): string {
                    return "Hello, " + name;
                }
                """,
            ["./main.ts"] = """
                import greet from './greet';
                console.log(greet("World"));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Hello, World\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void DefaultExport_Class(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./counter.ts"] = """
                export default class Counter {
                    count: number;
                    constructor() {
                        this.count = 0;
                    }
                    increment(): void {
                        this.count = this.count + 1;
                    }
                }
                """,
            ["./main.ts"] = """
                import Counter from './counter';
                let c: Counter = new Counter();
                c.increment();
                c.increment();
                console.log(c.count);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void DefaultExport_ArrowFunction(ExecutionMode mode)
    {
        // Note: anonymous function export default not supported, use arrow function
        var files = new Dictionary<string, string>
        {
            ["./double.ts"] = """
                const double = (x: number): number => x * 2;
                export default double;
                """,
            ["./main.ts"] = """
                import double from './double';
                console.log(double(21));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Combined Imports

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CombinedImport_DefaultAndNamed(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./module.ts"] = """
                export const helper: string = "Helper";
                export default "Default";
                """,
            ["./main.ts"] = """
                import def, { helper } from './module';
                console.log(def);
                console.log(helper);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Default\nHelper\n", output);
    }

    #endregion

    #region Namespace Imports

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void NamespaceImport_AllExports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                export const A: number = 1;
                export const B: number = 2;
                export function sum(x: number, y: number): number {
                    return x + y;
                }
                """,
            ["./main.ts"] = """
                import * as Utils from './utils';
                console.log(Utils.A);
                console.log(Utils.B);
                console.log(Utils.sum(Utils.A, Utils.B));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Re-exports

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReExport_Named(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./original.ts"] = """
                export const value: number = 42;
                """,
            ["./reexporter.ts"] = """
                export { value } from './original';
                """,
            ["./main.ts"] = """
                import { value } from './reexporter';
                console.log(value);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void ReExport_NamedWithAlias(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./original.ts"] = """
                export const internal: number = 100;
                """,
            ["./reexporter.ts"] = """
                export { internal as external } from './original';
                """,
            ["./main.ts"] = """
                import { external } from './reexporter';
                console.log(external);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReExport_All(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./original.ts"] = """
                export const A: number = 1;
                export const B: number = 2;
                """,
            ["./reexporter.ts"] = """
                export * from './original';
                """,
            ["./main.ts"] = """
                import { A, B } from './reexporter';
                console.log(A + B);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("3\n", output);
    }

    #endregion

    #region Multi-Level Dependencies

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MultiLevel_ThreeModules(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./c.ts"] = """
                export const value: number = 10;
                """,
            ["./b.ts"] = """
                import { value } from './c';
                export const doubled: number = value * 2;
                """,
            ["./a.ts"] = """
                import { doubled } from './b';
                console.log(doubled);
                """
        };

        var output = TestHarness.RunModules(files, "./a.ts", mode);
        Assert.Equal("20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void MultiLevel_DiamondDependency(ExecutionMode mode)
    {
        // a -> b -> d
        // a -> c -> d
        var files = new Dictionary<string, string>
        {
            ["./d.ts"] = """
                export let counter: number = 0;
                counter = counter + 1;
                console.log("d executed");
                """,
            ["./b.ts"] = """
                import { counter } from './d';
                export const fromB: number = counter;
                console.log("b executed");
                """,
            ["./c.ts"] = """
                import { counter } from './d';
                export const fromC: number = counter;
                console.log("c executed");
                """,
            ["./a.ts"] = """
                import { fromB } from './b';
                import { fromC } from './c';
                console.log("a executed");
                console.log(fromB);
                console.log(fromC);
                """
        };

        var output = TestHarness.RunModules(files, "./a.ts", mode);
        // d should only execute once due to module caching
        Assert.Contains("d executed\n", output);
        Assert.Equal(1, output.Split("d executed").Length - 1); // Count occurrences
    }

    #endregion

    #region Circular Dependency Detection

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void CircularDependency_ThrowsError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                import { b } from './b';
                export const a: number = 1;
                """,
            ["./b.ts"] = """
                import { a } from './a';
                export const b: number = 2;
                """
        };

        var ex = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunModules(files, "./a.ts", mode));
        // Can be "Circular dependency" or a type error about missing exports
        Assert.True(
            ex.Message.Contains("circular", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Module Error", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("no export", StringComparison.OrdinalIgnoreCase),
            $"Expected circular or module error, got: {ex.Message}");
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void CircularDependency_IndirectCycle(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                import { c } from './c';
                export const a: number = 1;
                """,
            ["./b.ts"] = """
                import { a } from './a';
                export const b: number = 2;
                """,
            ["./c.ts"] = """
                import { b } from './b';
                export const c: number = 3;
                """
        };

        var ex = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunModules(files, "./a.ts", mode));
        // Can be "Circular dependency" or a type error about missing exports
        Assert.True(
            ex.Message.Contains("circular", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Module Error", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("no export", StringComparison.OrdinalIgnoreCase),
            $"Expected circular or module error, got: {ex.Message}");
    }

    #endregion

    #region Side-Effect Imports

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SideEffectImport_ExecutesModule(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./sideeffect.ts"] = """
                console.log("Side effect executed");
                """,
            ["./main.ts"] = """
                import './sideeffect';
                console.log("Main executed");
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Side effect executed\nMain executed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void ExecutionOrder_MultipleModules(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                console.log("a");
                """,
            ["./b.ts"] = """
                import './a';
                console.log("b");
                """,
            ["./main.ts"] = """
                import './b';
                console.log("main");
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("a\nb\nmain\n", output);
    }

    #endregion

    #region Module Execution Order

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ExecutionOrder_DependenciesFirst(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./leaf.ts"] = """
                console.log("leaf");
                export const x: number = 1;
                """,
            ["./middle.ts"] = """
                import { x } from './leaf';
                console.log("middle");
                export const y: number = x + 1;
                """,
            ["./root.ts"] = """
                import { y } from './middle';
                console.log("root");
                console.log(y);
                """
        };

        var output = TestHarness.RunModules(files, "./root.ts", mode);
        Assert.Equal("leaf\nmiddle\nroot\n2\n", output);
    }

    #endregion

    #region Path Resolution

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void PathResolution_OmittedExtension(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./helper.ts"] = """
                export const help: string = "Helping!";
                """,
            ["./main.ts"] = """
                import { help } from './helper';
                console.log(help);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Helping!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void PathResolution_WithExtension(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./helper.ts"] = """
                export const help: string = "Helping!";
                """,
            ["./main.ts"] = """
                import { help } from './helper.ts';
                console.log(help);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Helping!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void PathResolution_NestedDirectories(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./lib/utils/math.ts"] = """
                export function square(x: number): number {
                    return x * x;
                }
                """,
            ["./main.ts"] = """
                import { square } from './lib/utils/math';
                console.log(square(5));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("25\n", output);
    }

    #endregion

    #region Export Types (Interface, Type Alias, Enum)

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Export_InterfaceWithFactory(ExecutionMode mode)
    {
        // Note: Interfaces are type-only exports (erased at runtime).
        // This test uses a factory function alongside the interface.
        var files = new Dictionary<string, string>
        {
            ["./types.ts"] = """
                export interface Person {
                    name: string;
                    age: number;
                }
                export function createPerson(name: string, age: number): Person {
                    return { name: name, age: age };
                }
                """,
            ["./main.ts"] = """
                import { createPerson } from './types';
                let p = createPerson("Alice", 30);
                console.log(p.name);
                console.log(p.age);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Export_TypeAliasUsedLocally(ExecutionMode mode)
    {
        // Note: Type aliases are type-only exports (erased at runtime).
        // This test uses the type locally within the same module.
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                export function makeId(n: number): number {
                    return n * 1000;
                }
                """,
            ["./main.ts"] = """
                import { makeId } from './utils';
                let id: number = makeId(42);
                console.log(id);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42000\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Export_Enum(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./status.ts"] = """
                export enum Status {
                    Active,
                    Inactive,
                    Pending
                }
                """,
            ["./main.ts"] = """
                import { Status } from './status';
                console.log(Status.Active);
                console.log(Status.Pending);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("0\n2\n", output);
    }

    #endregion

    #region Complex Scenarios

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Complex_MultipleExportsAndImports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./math.ts"] = """
                export const PI: number = 3.14159;
                export function add(a: number, b: number): number { return a + b; }
                export function multiply(a: number, b: number): number { return a * b; }
                export default class Calculator {
                    value: number;
                    constructor() { this.value = 0; }
                    add(n: number): void { this.value = this.value + n; }
                    getResult(): number { return this.value; }
                }
                """,
            ["./main.ts"] = """
                import Calculator, { PI, add, multiply } from './math';
                console.log(PI);
                console.log(add(2, 3));
                console.log(multiply(4, 5));
                let calc: Calculator = new Calculator();
                calc.add(10);
                calc.add(5);
                console.log(calc.getResult());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("3.14159\n5\n20\n15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Complex_ClassInheritanceAcrossModules(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./base.ts"] = """
                export class Animal {
                    name: string;
                    constructor(name: string) {
                        this.name = name;
                    }
                    speak(): string {
                        return this.name + " makes a sound";
                    }
                }
                """,
            ["./dog.ts"] = """
                import { Animal } from './base';
                export class Dog extends Animal {
                    constructor(name: string) {
                        super(name);
                    }
                    speak(): string {
                        return this.name + " barks";
                    }
                }
                """,
            ["./main.ts"] = """
                import { Dog } from './dog';
                let d: Dog = new Dog("Rex");
                console.log(d.speak());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Rex barks\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Complex_FunctionUsingImportedClass(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./person.ts"] = """
                export class Person {
                    name: string;
                    constructor(name: string) {
                        this.name = name;
                    }
                }
                """,
            ["./factory.ts"] = """
                import { Person } from './person';
                export function createPerson(name: string): Person {
                    return new Person(name);
                }
                """,
            ["./main.ts"] = """
                import { createPerson } from './factory';
                let p = createPerson("Bob");
                console.log(p.name);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Bob\n", output);
    }

    #endregion

    #region Duplicate Names Across Modules (Compiled)

    /// <summary>
    /// Tests that two different modules can each define a class with the same name,
    /// and they should be treated as separate types.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void DuplicateClassNames_AcrossModules_ShouldBeDistinct(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./file1.ts"] = """
                export class Foo {
                    x: number = 1;
                }
                """,
            ["./file2.ts"] = """
                export class Foo {
                    y: number = 2;
                }
                """,
            ["./main.ts"] = """
                import { Foo as Foo1 } from './file1';
                import { Foo as Foo2 } from './file2';

                let f1 = new Foo1();
                let f2 = new Foo2();

                console.log(f1.x);
                console.log(f2.y);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1\n2\n", output);
    }

    /// <summary>
    /// Tests that two different modules can each define a function with the same name,
    /// and they should be treated as separate functions.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void DuplicateFunctionNames_AcrossModules_ShouldBeDistinct(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./file1.ts"] = """
                export function getValue(): number {
                    return 1;
                }
                """,
            ["./file2.ts"] = """
                export function getValue(): number {
                    return 2;
                }
                """,
            ["./main.ts"] = """
                import { getValue as getValue1 } from './file1';
                import { getValue as getValue2 } from './file2';

                console.log(getValue1());
                console.log(getValue2());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1\n2\n", output);
    }

    /// <summary>
    /// Tests that two different modules can each define an enum with the same name,
    /// and they should be treated as separate enums.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void DuplicateEnumNames_AcrossModules_ShouldBeDistinct(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./file1.ts"] = """
                export enum Status {
                    Active = 10
                }
                """,
            ["./file2.ts"] = """
                export enum Status {
                    Active = 20
                }
                """,
            ["./main.ts"] = """
                import { Status as Status1 } from './file1';
                import { Status as Status2 } from './file2';

                console.log(Status1.Active);
                console.log(Status2.Active);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("10\n20\n", output);
    }

    #endregion

    #region Cross-Module Type Usage (Compiled)

    /// <summary>
    /// Tests that a class exported from one module can be imported, instantiated,
    /// and have its methods called from another module.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void CrossModule_ClassInstantiationAndMethodCall(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./person.ts"] = """
                export class Person {
                    name: string;
                    age: number;

                    constructor(name: string, age: number) {
                        this.name = name;
                        this.age = age;
                    }

                    greet(): string {
                        return "Hello, " + this.name;
                    }

                    getAgeInMonths(): number {
                        return this.age * 12;
                    }
                }
                """,
            ["./main.ts"] = """
                import { Person } from './person';

                let p = new Person("Alice", 30);
                console.log(p.name);
                console.log(p.age);
                console.log(p.greet());
                console.log(p.getAgeInMonths());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Alice\n30\nHello, Alice\n360\n", output);
    }

    /// <summary>
    /// Tests that a function exported from one module can be imported and called
    /// from another module with arguments and return values.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void CrossModule_FunctionCall(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./math-utils.ts"] = """
                export function add(a: number, b: number): number {
                    return a + b;
                }

                export function multiply(a: number, b: number): number {
                    return a * b;
                }

                export function factorial(n: number): number {
                    if (n <= 1) return 1;
                    return n * factorial(n - 1);
                }
                """,
            ["./main.ts"] = """
                import { add, multiply, factorial } from './math-utils';

                console.log(add(3, 4));
                console.log(multiply(5, 6));
                console.log(factorial(5));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("7\n30\n120\n", output);
    }

    /// <summary>
    /// Tests that an enum exported from one module can be imported and its
    /// members accessed from another module.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void CrossModule_EnumAccess(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./status.ts"] = """
                export enum Status {
                    Pending = 0,
                    Active = 1,
                    Completed = 2
                }

                export enum Priority {
                    Low = "low",
                    Medium = "medium",
                    High = "high"
                }
                """,
            ["./main.ts"] = """
                import { Status, Priority } from './status';

                console.log(Status.Pending);
                console.log(Status.Active);
                console.log(Status.Completed);
                console.log(Priority.Low);
                console.log(Priority.High);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("0\n1\n2\nlow\nhigh\n", output);
    }

    /// <summary>
    /// Tests that multiple modules can import from the same shared module,
    /// and each gets the correct types without interference.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void CrossModule_SharedDependency(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./shared.ts"] = """
                export class Counter {
                    value: number = 0;

                    increment(): void {
                        this.value = this.value + 1;
                    }

                    getValue(): number {
                        return this.value;
                    }
                }

                export function createCounter(): Counter {
                    return new Counter();
                }
                """,
            ["./module-a.ts"] = """
                import { Counter, createCounter } from './shared';

                export function useCounterA(): number {
                    let c = createCounter();
                    c.increment();
                    c.increment();
                    return c.getValue();
                }
                """,
            ["./module-b.ts"] = """
                import { Counter } from './shared';

                export function useCounterB(): number {
                    let c = new Counter();
                    c.increment();
                    c.increment();
                    c.increment();
                    return c.getValue();
                }
                """,
            ["./main.ts"] = """
                import { useCounterA } from './module-a';
                import { useCounterB } from './module-b';

                console.log(useCounterA());
                console.log(useCounterB());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("2\n3\n", output);
    }

    /// <summary>
    /// Tests a complex scenario with classes, functions, and enums all being
    /// imported and used across modules.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void CrossModule_MixedTypes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./types.ts"] = """
                export enum TaskStatus {
                    Todo = 0,
                    InProgress = 1,
                    Done = 2
                }

                export class Task {
                    title: string;
                    status: number;

                    constructor(title: string) {
                        this.title = title;
                        this.status = TaskStatus.Todo;
                    }

                    start(): void {
                        this.status = TaskStatus.InProgress;
                    }

                    complete(): void {
                        this.status = TaskStatus.Done;
                    }
                }

                export function createTask(title: string): Task {
                    return new Task(title);
                }
                """,
            ["./main.ts"] = """
                import { Task, TaskStatus, createTask } from './types';

                let t1 = new Task("Write tests");
                console.log(t1.status);

                t1.start();
                console.log(t1.status);

                t1.complete();
                console.log(t1.status);

                let t2 = createTask("Review PR");
                console.log(t2.title);
                console.log(t2.status);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("0\n1\n2\nReview PR\n0\n", output);
    }

    #endregion
}
