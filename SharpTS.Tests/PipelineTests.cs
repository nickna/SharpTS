using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests;

/// <summary>
/// Tests that verify the interpreter and compiler produce identical output
/// for the same TypeScript source code.
/// </summary>
public class PipelineTests
{
    [Theory]
    [InlineData("""
        let x: number = 10;
        console.log(x);
        """)]
    [InlineData("""
        let a: number = 5;
        let b: number = 3;
        console.log(a + b);
        """)]
    [InlineData("""
        console.log(2 * 3 + 4);
        """)]
    public void NumberExpressions_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        let s: string = "hello";
        console.log(s);
        """)]
    [InlineData("""
        console.log("Hello, World!");
        """)]
    [InlineData("""
        let a: string = "foo";
        let b: string = "bar";
        console.log(a + b);
        """)]
    public void StringExpressions_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        let arr: number[] = [1, 2, 3];
        console.log(arr.length);
        """)]
    [InlineData("""
        let arr: number[] = [10, 20, 30];
        console.log(arr[1]);
        """)]
    [InlineData("""
        let arr: number[] = [1, 2];
        arr.push(3);
        console.log(arr.length);
        """)]
    public void ArrayOperations_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        class Point {
            x: number;
            y: number;
            constructor(x: number, y: number) {
                this.x = x;
                this.y = y;
            }
        }
        let p: Point = new Point(3, 4);
        console.log(p.x);
        console.log(p.y);
        """)]
    [InlineData("""
        class Counter {
            count: number;
            constructor() {
                this.count = 0;
            }
            increment(): void {
                this.count = this.count + 1;
            }
        }
        let c: Counter = new Counter();
        c.increment();
        c.increment();
        console.log(c.count);
        """)]
    public void ClassOperations_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        function add(a: number, b: number): number {
            return a + b;
        }
        console.log(add(3, 5));
        """)]
    [InlineData("""
        function factorial(n: number): number {
            if (n <= 1) {
                return 1;
            }
            return n * factorial(n - 1);
        }
        console.log(factorial(5));
        """)]
    public void FunctionCalls_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        let x: number = 5;
        if (x > 3) {
            console.log("greater");
        } else {
            console.log("lesser");
        }
        """)]
    [InlineData("""
        let sum: number = 0;
        for (let i: number = 1; i <= 5; i = i + 1) {
            sum = sum + i;
        }
        console.log(sum);
        """)]
    [InlineData("""
        let i: number = 0;
        while (i < 3) {
            console.log(i);
            i = i + 1;
        }
        """)]
    public void ControlFlow_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        let arr: number[] = [1, 2, 3];
        for (let x: number of arr) {
            console.log(x);
        }
        """)]
    public void ForOfLoop_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        function makeAdder(x: number): (y: number) => number {
            return (y: number): number => x + y;
        }
        let add5: (y: number) => number = makeAdder(5);
        console.log(add5(3));
        """)]
    public void Closures_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        try {
            throw "error";
        } catch (e) {
            console.log(e);
        }
        """)]
    [InlineData("""
        try {
            console.log("try");
        } finally {
            console.log("finally");
        }
        """)]
    [InlineData("""
        try {
            throw 42;
        } catch (e) {
            console.log(e);
        } finally {
            console.log("done");
        }
        """)]
    public void ErrorHandling_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        class Config {
            static version: number = 42;
        }
        console.log(Config.version);
        """)]
    [InlineData("""
        class Counter {
            static count: number = 0;
            static increment(): number {
                Counter.count = Counter.count + 1;
                return Counter.count;
            }
        }
        console.log(Counter.increment());
        console.log(Counter.increment());
        """)]
    public void StaticMembers_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        class Box {
            private _value: number;
            constructor() {
                this._value = 42;
            }
            get value(): number {
                return this._value;
            }
        }
        let b: Box = new Box();
        console.log(b.value);
        """)]
    [InlineData("""
        class Counter {
            private _count: number;
            constructor() {
                this._count = 0;
            }
            get count(): number {
                return this._count;
            }
            set count(v: number) {
                this._count = v;
            }
        }
        let c: Counter = new Counter();
        c.count = 10;
        console.log(c.count);
        """)]
    public void GettersSetters_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        const arr: number[] = [1, 2, 3];
        const [a, b] = arr;
        console.log(a);
        console.log(b);
        """)]
    [InlineData("""
        const obj = { name: "Alice", age: 30 };
        const { name, age } = obj;
        console.log(name);
        console.log(age);
        """)]
    [InlineData("""
        const [head, ...tail] = [1, 2, 3, 4];
        console.log(head);
        console.log(tail.length);
        """)]
    public void Destructuring_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        function identity<T>(x: T): T {
            return x;
        }
        console.log(identity(42));
        console.log(identity("hello"));
        """)]
    [InlineData("""
        function identity<T>(x: T): T {
            return x;
        }
        console.log(identity<number>(42));
        """)]
    [InlineData("""
        class Box<T> {
            value: T;
            constructor(v: T) {
                this.value = v;
            }
        }
        let b: Box<number> = new Box<number>(42);
        console.log(b.value);
        """)]
    [InlineData("""
        class Animal {
            name: string;
            constructor(n: string) {
                this.name = n;
            }
        }
        function getName<T extends Animal>(a: T): string {
            return a.name;
        }
        let a: Animal = new Animal("Rex");
        console.log(getName(a));
        """)]
    public void Generics_MatchBetweenInterpreterAndCompiler(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }
}
