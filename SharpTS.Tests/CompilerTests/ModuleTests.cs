using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for module compilation to .NET IL.
/// </summary>
public class ModuleTests
{
    #region Side-Effect Imports (Basic Module Initialization)

    /// <summary>
    /// Tests that modules execute in the correct order - this is the most basic
    /// module functionality that should work.
    /// </summary>
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("Side effect executed\nMain executed\n", output);
    }

    [Fact]
    public void ExecutionOrder_MultipleModules()
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("a\nb\nmain\n", output);
    }

    #endregion

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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("7\n", output);
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("6\n", output);
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("Hello, World\n", output);
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("Default\nHelper\n", output);
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

        var output = TestHarness.RunModulesCompiled(files, "./a.ts");
        Assert.Equal("20\n", output);
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

        var output = TestHarness.RunModulesCompiled(files, "./root.ts");
        Assert.Equal("leaf\nmiddle\nroot\n2\n", output);
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("42\n", output);
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("3\n", output);
    }

    #endregion

    #region Export Enum

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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("0\n2\n", output);
    }

    #endregion

    #region Duplicate Class Names Across Modules

    /// <summary>
    /// Tests that two different modules can each define a class with the same name,
    /// and they should be treated as separate types.
    /// </summary>
    [Fact]
    public void DuplicateClassNames_AcrossModules_ShouldBeDistinct()
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("1\n2\n", output);
    }

    /// <summary>
    /// Tests that two different modules can each define a function with the same name,
    /// and they should be treated as separate functions.
    /// </summary>
    [Fact]
    public void DuplicateFunctionNames_AcrossModules_ShouldBeDistinct()
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("1\n2\n", output);
    }

    /// <summary>
    /// Tests that two different modules can each define an enum with the same name,
    /// and they should be treated as separate enums.
    /// </summary>
    [Fact]
    public void DuplicateEnumNames_AcrossModules_ShouldBeDistinct()
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("10\n20\n", output);
    }

    #endregion

    #region Cross-Module Type Usage

    /// <summary>
    /// Tests that a class exported from one module can be imported, instantiated,
    /// and have its methods called from another module.
    /// </summary>
    [Fact]
    public void CrossModule_ClassInstantiationAndMethodCall()
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("Alice\n30\nHello, Alice\n360\n", output);
    }

    /// <summary>
    /// Tests that a function exported from one module can be imported and called
    /// from another module with arguments and return values.
    /// </summary>
    [Fact]
    public void CrossModule_FunctionCall()
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("7\n30\n120\n", output);
    }

    /// <summary>
    /// Tests that an enum exported from one module can be imported and its
    /// members accessed from another module.
    /// </summary>
    [Fact]
    public void CrossModule_EnumAccess()
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("0\n1\n2\nlow\nhigh\n", output);
    }

    /// <summary>
    /// Tests that multiple modules can import from the same shared module,
    /// and each gets the correct types without interference.
    /// </summary>
    [Fact]
    public void CrossModule_SharedDependency()
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("2\n3\n", output);
    }

    /// <summary>
    /// Tests a complex scenario with classes, functions, and enums all being
    /// imported and used across modules.
    /// </summary>
    [Fact]
    public void CrossModule_MixedTypes()
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

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("0\n1\n2\nReview PR\n0\n", output);
    }

    #endregion
}
