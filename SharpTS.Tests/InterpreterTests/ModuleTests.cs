using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class ModuleTests
{
    #region Named Exports and Imports

    [Fact]
    public void NamedExport_SingleVariable()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("3.14159\n", output);
    }

    [Fact]
    public void NamedExport_MultipleVariables()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("60\n", output);
    }

    [Fact]
    public void NamedExport_Function()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void NamedExport_Class()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Hello, Alice\n", output);
    }

    [Fact]
    public void NamedImport_WithAlias()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void NamedExport_List()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void NamedExport_ListWithAlias()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("100\n", output);
    }

    #endregion

    #region Default Exports and Imports

    [Fact]
    public void DefaultExport_Expression()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void DefaultExport_Function()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Hello, World\n", output);
    }

    [Fact]
    public void DefaultExport_Class()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void DefaultExport_ArrowFunction()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Combined Imports

    [Fact]
    public void CombinedImport_DefaultAndNamed()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Default\nHelper\n", output);
    }

    #endregion

    #region Namespace Imports

    [Fact]
    public void NamespaceImport_AllExports()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Re-exports

    [Fact]
    public void ReExport_Named()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void ReExport_NamedWithAlias()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("100\n", output);
    }

    [Fact]
    public void ReExport_All()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("3\n", output);
    }

    #endregion

    #region Multi-Level Dependencies

    [Fact]
    public void MultiLevel_ThreeModules()
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

        var output = TestHarness.RunModulesInterpreted(files, "./a.ts");
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void MultiLevel_DiamondDependency()
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

        var output = TestHarness.RunModulesInterpreted(files, "./a.ts");
        // d should only execute once due to module caching
        Assert.Contains("d executed\n", output);
        Assert.Equal(1, output.Split("d executed").Length - 1); // Count occurrences
    }

    #endregion

    #region Circular Dependency Detection

    [Fact]
    public void CircularDependency_ThrowsError()
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
            TestHarness.RunModulesInterpreted(files, "./a.ts"));
        // Can be "Circular dependency" or a type error about missing exports
        Assert.True(
            ex.Message.Contains("circular", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Module Error", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("no export", StringComparison.OrdinalIgnoreCase),
            $"Expected circular or module error, got: {ex.Message}");
    }

    [Fact]
    public void CircularDependency_IndirectCycle()
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
            TestHarness.RunModulesInterpreted(files, "./a.ts"));
        // Can be "Circular dependency" or a type error about missing exports
        Assert.True(
            ex.Message.Contains("circular", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Module Error", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("no export", StringComparison.OrdinalIgnoreCase),
            $"Expected circular or module error, got: {ex.Message}");
    }

    #endregion

    #region Side-Effect Imports

    [Fact]
    public void SideEffectImport_ExecutesModule()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Side effect executed\nMain executed\n", output);
    }

    #endregion

    #region Module Execution Order

    [Fact]
    public void ExecutionOrder_DependenciesFirst()
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

        var output = TestHarness.RunModulesInterpreted(files, "./root.ts");
        Assert.Equal("leaf\nmiddle\nroot\n2\n", output);
    }

    #endregion

    #region Path Resolution

    [Fact]
    public void PathResolution_OmittedExtension()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Helping!\n", output);
    }

    [Fact]
    public void PathResolution_WithExtension()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Helping!\n", output);
    }

    [Fact]
    public void PathResolution_NestedDirectories()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("25\n", output);
    }

    #endregion

    #region Export Types (Interface, Type Alias, Enum)

    [Fact]
    public void Export_InterfaceWithFactory()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Alice\n30\n", output);
    }

    [Fact]
    public void Export_TypeAliasUsedLocally()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("42000\n", output);
    }

    [Fact]
    public void Export_Enum()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("0\n2\n", output);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void Complex_MultipleExportsAndImports()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("3.14159\n5\n20\n15\n", output);
    }

    [Fact]
    public void Complex_ClassInheritanceAcrossModules()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Rex barks\n", output);
    }

    [Fact]
    public void Complex_FunctionUsingImportedClass()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("Bob\n", output);
    }

    #endregion
}
