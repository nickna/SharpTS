using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests that verify sync and async execution paths produce equivalent results.
/// Part of Phase 0: Divergence Audit for the async/sync refactoring plan.
///
/// These tests run code synchronously (no await in the code) to verify
/// that both paths produce identical behavior for non-async code.
/// </summary>
public class SyncAsyncEquivalenceTests
{
    // ===================== Basic Expressions =====================

    [Fact]
    public void Binary_Expression_SyncPath()
    {
        var source = """
            const a = 5 + 3;
            const b = 10 - 4;
            const c = 6 * 7;
            const d = 20 / 4;
            console.log(a, b, c, d);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("8 6 42 5\n", output);
    }

    [Fact]
    public void Binary_Expression_AsyncPath()
    {
        var source = """
            async function test() {
                const a = 5 + 3;
                const b = 10 - 4;
                const c = 6 * 7;
                const d = 20 / 4;
                console.log(a, b, c, d);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("8 6 42 5\n", output);
    }

    [Fact]
    public void Logical_Expression_SyncPath()
    {
        var source = """
            const a = true && false;
            const b = false || true;
            const c = null ?? "default";
            console.log(a, b, c);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false true default\n", output);
    }

    [Fact]
    public void Logical_Expression_AsyncPath()
    {
        var source = """
            async function test() {
                const a = true && false;
                const b = false || true;
                const c = null ?? "default";
                console.log(a, b, c);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false true default\n", output);
    }

    [Fact]
    public void Ternary_Expression_SyncPath()
    {
        var source = """
            const x = true ? "yes" : "no";
            const y = false ? "yes" : "no";
            console.log(x, y);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("yes no\n", output);
    }

    [Fact]
    public void Ternary_Expression_AsyncPath()
    {
        var source = """
            async function test() {
                const x = true ? "yes" : "no";
                const y = false ? "yes" : "no";
                console.log(x, y);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("yes no\n", output);
    }

    // ===================== Object and Array Literals =====================

    [Fact]
    public void Object_Literal_SyncPath()
    {
        var source = """
            const obj = { name: "test", value: 42 };
            console.log(obj.name, obj.value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("test 42\n", output);
    }

    [Fact]
    public void Object_Literal_AsyncPath()
    {
        var source = """
            async function test() {
                const obj = { name: "test", value: 42 };
                console.log(obj.name, obj.value);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("test 42\n", output);
    }

    [Fact]
    public void Array_Literal_SyncPath()
    {
        var source = """
            const arr = [1, 2, 3];
            console.log(arr[0], arr[1], arr[2], arr.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1 2 3 3\n", output);
    }

    [Fact]
    public void Array_Literal_AsyncPath()
    {
        var source = """
            async function test() {
                const arr = [1, 2, 3];
                console.log(arr[0], arr[1], arr[2], arr.length);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1 2 3 3\n", output);
    }

    [Fact]
    public void Spread_In_Object_SyncPath()
    {
        var source = """
            const base = { x: 1, y: 2 };
            const extended = { ...base, z: 3 };
            console.log(extended.x, extended.y, extended.z);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1 2 3\n", output);
    }

    [Fact]
    public void Spread_In_Object_AsyncPath()
    {
        var source = """
            async function test() {
                const base = { x: 1, y: 2 };
                const extended = { ...base, z: 3 };
                console.log(extended.x, extended.y, extended.z);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1 2 3\n", output);
    }

    [Fact]
    public void Spread_In_Array_SyncPath()
    {
        var source = """
            const arr1 = [1, 2];
            const arr2 = [...arr1, 3, 4];
            console.log(arr2.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,2,3,4\n", output);
    }

    [Fact]
    public void Spread_In_Array_AsyncPath()
    {
        var source = """
            async function test() {
                const arr1 = [1, 2];
                const arr2 = [...arr1, 3, 4];
                console.log(arr2.join(","));
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,2,3,4\n", output);
    }

    // ===================== Function Calls =====================

    [Fact]
    public void Function_Call_SyncPath()
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            console.log(add(3, 4));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void Function_Call_AsyncPath()
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            async function test() {
                console.log(add(3, 4));
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void Method_Call_SyncPath()
    {
        var source = """
            class Calculator {
                multiply(a: number, b: number): number {
                    return a * b;
                }
            }
            const calc = new Calculator();
            console.log(calc.multiply(5, 6));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("30\n", output);
    }

    [Fact]
    public void Method_Call_AsyncPath()
    {
        var source = """
            class Calculator {
                multiply(a: number, b: number): number {
                    return a * b;
                }
            }
            async function test() {
                const calc = new Calculator();
                console.log(calc.multiply(5, 6));
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("30\n", output);
    }

    // ===================== Class Instantiation (EvaluateNew) =====================

    [Fact]
    public void New_Expression_SyncPath()
    {
        var source = """
            class Person {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
            }
            const p = new Person("Alice");
            console.log(p.name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n", output);
    }

    [Fact]
    public void New_Expression_AsyncPath()
    {
        var source = """
            class Person {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
            }
            async function test() {
                const p = new Person("Alice");
                console.log(p.name);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Alice\n", output);
    }

    [Fact]
    public void New_BuiltIn_Date_SyncPath()
    {
        var source = """
            const d = new Date(2023, 0, 15);
            console.log(d.getFullYear());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2023\n", output);
    }

    [Fact]
    public void New_BuiltIn_Date_AsyncPath()
    {
        var source = """
            async function test() {
                const d = new Date(2023, 0, 15);
                console.log(d.getFullYear());
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2023\n", output);
    }

    // ===================== Switch Statement =====================

    [Fact]
    public void Switch_Statement_SyncPath()
    {
        var source = """
            function test(x: number): string {
                switch (x) {
                    case 1: return "one";
                    case 2: return "two";
                    default: return "other";
                }
            }
            console.log(test(1), test(2), test(3));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("one two other\n", output);
    }

    [Fact]
    public void Switch_Statement_AsyncPath()
    {
        var source = """
            async function test(x: number): Promise<string> {
                switch (x) {
                    case 1: return "one";
                    case 2: return "two";
                    default: return "other";
                }
            }
            async function main() {
                console.log(await test(1), await test(2), await test(3));
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("one two other\n", output);
    }

    [Fact]
    public void Switch_FallThrough_SyncPath()
    {
        var source = """
            function test(x: number): string {
                let result = "";
                switch (x) {
                    case 1:
                        result += "a";
                    case 2:
                        result += "b";
                        break;
                    case 3:
                        result += "c";
                        break;
                }
                return result;
            }
            console.log(test(1), test(2), test(3));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ab b c\n", output);
    }

    [Fact]
    public void Switch_FallThrough_AsyncPath()
    {
        var source = """
            async function test(x: number): Promise<string> {
                let result = "";
                switch (x) {
                    case 1:
                        result += "a";
                    case 2:
                        result += "b";
                        break;
                    case 3:
                        result += "c";
                        break;
                }
                return result;
            }
            async function main() {
                console.log(await test(1), await test(2), await test(3));
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ab b c\n", output);
    }

    // ===================== Try/Catch/Finally =====================

    [Fact]
    public void TryCatch_SyncPath()
    {
        var source = """
            function test(): string {
                try {
                    throw "error!";
                } catch (e) {
                    return "caught: " + e;
                }
            }
            console.log(test());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught: error!\n", output);
    }

    [Fact]
    public void TryCatch_AsyncPath()
    {
        var source = """
            async function test(): Promise<string> {
                try {
                    throw "error!";
                } catch (e) {
                    return "caught: " + e;
                }
            }
            async function main() {
                console.log(await test());
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught: error!\n", output);
    }

    [Fact]
    public void TryCatchFinally_SyncPath()
    {
        var source = """
            let log = "";
            function test(): string {
                try {
                    log += "try,";
                    throw "err";
                } catch (e) {
                    log += "catch,";
                    return "result";
                } finally {
                    log += "finally";
                }
            }
            const result = test();
            console.log(result, log);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("result try,catch,finally\n", output);
    }

    [Fact]
    public void TryCatchFinally_AsyncPath()
    {
        var source = """
            let log = "";
            async function test(): Promise<string> {
                try {
                    log += "try,";
                    throw "err";
                } catch (e) {
                    log += "catch,";
                    return "result";
                } finally {
                    log += "finally";
                }
            }
            async function main() {
                const result = await test();
                console.log(result, log);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("result try,catch,finally\n", output);
    }

    // ===================== For...of Loop =====================

    [Fact]
    public void ForOf_Array_SyncPath()
    {
        var source = """
            const arr = [1, 2, 3];
            let sum = 0;
            for (const x of arr) {
                sum += x;
            }
            console.log(sum);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void ForOf_Array_AsyncPath()
    {
        var source = """
            async function test() {
                const arr = [1, 2, 3];
                let sum = 0;
                for (const x of arr) {
                    sum += x;
                }
                console.log(sum);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void ForOf_String_SyncPath()
    {
        var source = """
            const str = "abc";
            let chars = "";
            for (const c of str) {
                chars += c + ",";
            }
            console.log(chars);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("a,b,c,\n", output);
    }

    [Fact]
    public void ForOf_String_AsyncPath()
    {
        var source = """
            async function test() {
                const str = "abc";
                let chars = "";
                for (const c of str) {
                    chars += c + ",";
                }
                console.log(chars);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("a,b,c,\n", output);
    }

    // ===================== Property Access (EvaluateGet) =====================

    [Fact]
    public void PropertyAccess_Instance_SyncPath()
    {
        var source = """
            class Box {
                value: number = 10;
            }
            const box = new Box();
            console.log(box.value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void PropertyAccess_Instance_AsyncPath()
    {
        var source = """
            class Box {
                value: number = 10;
            }
            async function test() {
                const box = new Box();
                console.log(box.value);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void PropertyAccess_Static_SyncPath()
    {
        var source = """
            class Config {
                static version: string = "1.0";
            }
            console.log(Config.version);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1.0\n", output);
    }

    [Fact]
    public void PropertyAccess_Static_AsyncPath()
    {
        var source = """
            class Config {
                static version: string = "1.0";
            }
            async function test() {
                console.log(Config.version);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1.0\n", output);
    }

    [Fact]
    public void PropertyAccess_BuiltIn_Number_SyncPath()
    {
        var source = """
            console.log(Number.MAX_VALUE > 0);
            console.log(Number.isNaN(NaN));
            console.log(Number.isFinite(42));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void PropertyAccess_BuiltIn_Number_AsyncPath()
    {
        var source = """
            async function test() {
                console.log(Number.MAX_VALUE > 0);
                console.log(Number.isNaN(NaN));
                console.log(Number.isFinite(42));
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void PropertyAccess_BuiltIn_Math_SyncPath()
    {
        var source = """
            console.log(Math.PI > 3);
            console.log(Math.floor(3.7));
            console.log(Math.ceil(3.2));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n3\n4\n", output);
    }

    [Fact]
    public void PropertyAccess_BuiltIn_Math_AsyncPath()
    {
        var source = """
            async function test() {
                console.log(Math.PI > 3);
                console.log(Math.floor(3.7));
                console.log(Math.ceil(3.2));
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n3\n4\n", output);
    }

    // ===================== Compound Assignment =====================

    [Fact]
    public void CompoundAssign_SyncPath()
    {
        var source = """
            let x = 5;
            x += 3;
            x -= 2;
            x *= 4;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("24\n", output);
    }

    [Fact]
    public void CompoundAssign_AsyncPath()
    {
        var source = """
            async function test() {
                let x = 5;
                x += 3;
                x -= 2;
                x *= 4;
                console.log(x);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("24\n", output);
    }

    // ===================== Logical Assignment =====================

    [Fact]
    public void LogicalAssign_SyncPath()
    {
        var source = """
            let a: any = null;
            let b: any = "value";
            let c: any = false;

            a ??= "default";
            b ??= "default";
            c ||= "default";

            console.log(a, b, c);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("default value default\n", output);
    }

    [Fact]
    public void LogicalAssign_AsyncPath()
    {
        var source = """
            async function test() {
                let a: any = null;
                let b: any = "value";
                let c: any = false;

                a ??= "default";
                b ??= "default";
                c ||= "default";

                console.log(a, b, c);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("default value default\n", output);
    }

    // ===================== Template Literals =====================

    [Fact]
    public void TemplateLiteral_SyncPath()
    {
        var source = """
            const name = "World";
            const greeting = `Hello, ${name}!`;
            console.log(greeting);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, World!\n", output);
    }

    [Fact]
    public void TemplateLiteral_AsyncPath()
    {
        var source = """
            async function test() {
                const name = "World";
                const greeting = `Hello, ${name}!`;
                console.log(greeting);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, World!\n", output);
    }

    // ===================== Index Operations =====================

    [Fact]
    public void IndexGet_Array_SyncPath()
    {
        var source = """
            const arr = [10, 20, 30];
            console.log(arr[0], arr[1], arr[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10 20 30\n", output);
    }

    [Fact]
    public void IndexGet_Array_AsyncPath()
    {
        var source = """
            async function test() {
                const arr = [10, 20, 30];
                console.log(arr[0], arr[1], arr[2]);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10 20 30\n", output);
    }

    [Fact]
    public void IndexSet_Array_SyncPath()
    {
        var source = """
            const arr: number[] = [1, 2, 3];
            arr[1] = 99;
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,99,3\n", output);
    }

    [Fact]
    public void IndexSet_Array_AsyncPath()
    {
        var source = """
            async function test() {
                const arr: number[] = [1, 2, 3];
                arr[1] = 99;
                console.log(arr.join(","));
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,99,3\n", output);
    }

    [Fact]
    public void IndexGet_Object_SyncPath()
    {
        var source = """
            const obj = { a: 1, b: 2 };
            console.log(obj["a"], obj["b"]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1 2\n", output);
    }

    [Fact]
    public void IndexGet_Object_AsyncPath()
    {
        var source = """
            async function test() {
                const obj = { a: 1, b: 2 };
                console.log(obj["a"], obj["b"]);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1 2\n", output);
    }

    // ===================== Class Inheritance =====================

    [Fact]
    public void ClassInheritance_SyncPath()
    {
        var source = """
            class Animal {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
                speak(): string {
                    return this.name + " makes a sound";
                }
            }
            class Dog extends Animal {
                speak(): string {
                    return this.name + " barks";
                }
            }
            const dog = new Dog("Rex");
            console.log(dog.speak());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Rex barks\n", output);
    }

    [Fact]
    public void ClassInheritance_AsyncPath()
    {
        var source = """
            class Animal {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
                speak(): string {
                    return this.name + " makes a sound";
                }
            }
            class Dog extends Animal {
                speak(): string {
                    return this.name + " barks";
                }
            }
            async function test() {
                const dog = new Dog("Rex");
                console.log(dog.speak());
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Rex barks\n", output);
    }

    [Fact]
    public void SuperCall_SyncPath()
    {
        var source = """
            class Base {
                value: number = 10;
                getValue(): number {
                    return this.value;
                }
            }
            class Derived extends Base {
                getValue(): number {
                    return super.getValue() * 2;
                }
            }
            const d = new Derived();
            console.log(d.getValue());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void SuperCall_AsyncPath()
    {
        var source = """
            class Base {
                value: number = 10;
                getValue(): number {
                    return this.value;
                }
            }
            class Derived extends Base {
                getValue(): number {
                    return super.getValue() * 2;
                }
            }
            async function test() {
                const d = new Derived();
                console.log(d.getValue());
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }
}
