using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for function type compatibility in TypeChecker.
/// Function types have contravariant parameters and covariant return types.
/// </summary>
public class FunctionCompatibilityTests
{
    #region Basic Function Type Assignment

    [Fact]
    public void SameFunctionSignature_Compatible()
    {
        var source = """
            type Adder = (a: number, b: number) => number;
            let add: Adder = (x: number, y: number): number => x + y;
            console.log(add(2, 3));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n", result);
    }

    [Fact]
    public void FunctionWithNamedParams_CompatibleWithDifferentNames()
    {
        var source = """
            type Adder = (a: number, b: number) => number;
            let add: Adder = (x: number, y: number): number => x + y;
            console.log(add(5, 7));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("12\n", result);
    }

    #endregion

    #region Parameter Contravariance (Callbacks)

    [Fact]
    public void CallbackWithFewerParams_Compatible()
    {
        // A callback can ignore parameters it doesn't need
        var source = """
            let arr = [1, 2, 3];
            arr.forEach((x) => console.log(x));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", result);
    }

    [Fact]
    public void CallbackIgnoresIndexAndArray_Compatible()
    {
        var source = """
            let arr = [10, 20, 30];
            let result = arr.map((x) => x * 2);
            console.log(result.join(","));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("20,40,60\n", result);
    }

    [Fact]
    public void CallbackWithMoreParams_Incompatible()
    {
        // A callback cannot require more parameters than provided
        var source = """
            type Callback = (x: number) => void;
            let cb: Callback = (a: number, b: number, c: number) => console.log(a + b + c);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void FilterCallback_SingleParam_Works()
    {
        var source = """
            let nums = [1, 2, 3, 4, 5, 6];
            let evens = nums.filter((n) => n % 2 === 0);
            console.log(evens.join(","));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("2,4,6\n", result);
    }

    #endregion

    #region Return Type Covariance

    [Fact]
    public void CompatibleReturnType_Works()
    {
        var source = """
            class Animal {
                name: string = "Animal";
            }
            class Dog extends Animal {
                breed: string = "Unknown";
            }

            type AnimalFactory = () => Animal;
            let createDog: AnimalFactory = (): Dog => new Dog();
            let animal = createDog();
            console.log(animal.name);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Animal\n", result);
    }

    [Fact]
    public void VoidReturnType_AcceptsAnyReturn()
    {
        var source = """
            type VoidFn = () => void;
            let fn: VoidFn = () => 42;  // Returns number but assigned to void fn
            fn();
            console.log("void accepts any");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("void accepts any\n", result);
    }

    [Fact]
    public void IncompatibleReturnType_Fails()
    {
        var source = """
            type NumberFn = () => number;
            let fn: NumberFn = (): string => "hello";
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Rest Parameters

    [Fact]
    public void RestParameter_AcceptsMultipleArgs()
    {
        var source = """
            function sum(...nums: number[]): number {
                let total = 0;
                for (let n of nums) {
                    total += n;
                }
                return total;
            }

            console.log(sum(1, 2, 3, 4, 5));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", result);
    }

    [Fact]
    public void RestParameter_AcceptsNoArgs()
    {
        var source = """
            function sum(...nums: number[]): number {
                let total = 0;
                for (let n of nums) {
                    total += n;
                }
                return total;
            }

            console.log(sum());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", result);
    }

    [Fact]
    public void RestParameter_MixedWithRegularParams()
    {
        var source = """
            function greet(greeting: string, ...names: string[]): void {
                for (let name of names) {
                    console.log(greeting + ", " + name);
                }
            }

            greet("Hello", "Alice", "Bob", "Charlie");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, Alice\nHello, Bob\nHello, Charlie\n", result);
    }

    [Fact]
    public void RestParameter_TypeMismatch_Fails()
    {
        var source = """
            function sum(...nums: number[]): number {
                return 0;
            }

            sum(1, 2, "three");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Optional Parameters

    [Fact]
    public void OptionalParameter_CanBeOmitted()
    {
        var source = """
            function greet(name: string, greeting?: string): void {
                console.log((greeting ?? "Hello") + ", " + name);
            }

            greet("Alice");
            greet("Bob", "Hi");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, Alice\nHi, Bob\n", result);
    }

    [Fact]
    public void OptionalParameter_WithDefaultValue()
    {
        var source = """
            function greet(name: string, greeting: string = "Hello"): void {
                console.log(greeting + ", " + name);
            }

            greet("Alice");
            greet("Bob", "Hi");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, Alice\nHi, Bob\n", result);
    }

    [Fact]
    public void RequiredAfterOptional_Fails()
    {
        var source = """
            function test(a?: number, b: number): void {
                console.log(a);
            }
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        // Should error about required param after optional
    }

    #endregion

    #region Function Type Expressions

    [Fact]
    public void ArrowFunctionType_Compatible()
    {
        var source = """
            let add: (a: number, b: number) => number = (x, y) => x + y;
            console.log(add(3, 4));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n", result);
    }

    [Fact]
    public void FunctionTypeAlias_Works()
    {
        var source = """
            type BinaryOp = (a: number, b: number) => number;

            let add: BinaryOp = (x, y) => x + y;
            let mul: BinaryOp = (x, y) => x * y;

            console.log(add(2, 3));
            console.log(mul(2, 3));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n6\n", result);
    }

    [Fact]
    public void FunctionReturningFunction_Works()
    {
        var source = """
            function makeAdder(x: number): (y: number) => number {
                return (y) => x + y;
            }

            let add5 = makeAdder(5);
            console.log(add5(3));
            console.log(add5(10));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("8\n15\n", result);
    }

    #endregion

    #region Generic Functions

    [Fact]
    public void GenericFunction_InfersType()
    {
        var source = """
            function identity<T>(x: T): T {
                return x;
            }

            console.log(identity(42));
            console.log(identity("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\nhello\n", result);
    }

    [Fact]
    public void GenericFunction_ExplicitTypeArg()
    {
        var source = """
            function identity<T>(x: T): T {
                return x;
            }

            let result: number = identity<number>(42);
            console.log(result);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void GenericFunction_MultipleTypeParams()
    {
        var source = """
            function pair<T, U>(first: T, second: U): [T, U] {
                return [first, second];
            }

            let p = pair("hello", 42);
            console.log(p[0]);
            console.log(p[1]);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n42\n", result);
    }

    #endregion

    #region Higher-Order Functions

    [Fact]
    public void MapFunction_TypeInference()
    {
        var source = """
            function map<T, U>(arr: T[], fn: (item: T) => U): U[] {
                let result: U[] = [];
                for (let item of arr) {
                    result.push(fn(item));
                }
                return result;
            }

            let nums = [1, 2, 3];
            let doubled = map(nums, (x) => x * 2);
            console.log(doubled.join(","));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("2,4,6\n", result);
    }

    [Fact]
    public void FilterFunction_PredicateType()
    {
        var source = """
            function filter<T>(arr: T[], predicate: (item: T) => boolean): T[] {
                let result: T[] = [];
                for (let item of arr) {
                    if (predicate(item)) {
                        result.push(item);
                    }
                }
                return result;
            }

            let nums = [1, 2, 3, 4, 5, 6];
            let evens = filter(nums, (x) => x % 2 === 0);
            console.log(evens.join(","));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("2,4,6\n", result);
    }

    [Fact]
    public void ReduceFunction_AccumulatorType()
    {
        var source = """
            function reduce<T, U>(arr: T[], fn: (acc: U, item: T) => U, initial: U): U {
                let acc = initial;
                for (let item of arr) {
                    acc = fn(acc, item);
                }
                return acc;
            }

            let nums = [1, 2, 3, 4, 5];
            let sum = reduce(nums, (acc, x) => acc + x, 0);
            console.log(sum);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", result);
    }

    #endregion

    #region Method Signatures

    [Fact]
    public void MethodSignature_MatchesInterface()
    {
        var source = """
            interface Calculator {
                add(a: number, b: number): number;
            }

            let calc: Calculator = {
                add: (x: number, y: number): number => x + y
            };

            console.log(calc.add(2, 3));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n", result);
    }

    [Fact]
    public void MethodSignature_Mismatch_Fails()
    {
        var source = """
            interface Calculator {
                add(a: number, b: number): number;
            }

            let calc: Calculator = {
                add: (x: string, y: string): string => x + y
            };
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region This Parameter

    [Fact(Skip = "Explicit this: parameter syntax not supported by parser")]
    public void ExplicitThisParameter_Works()
    {
        var source = """
            interface Counter {
                count: number;
                increment(this: Counter): void;
            }

            let counter: Counter = {
                count: 0,
                increment: function(this: Counter) {
                    this.count++;
                }
            };

            counter.increment();
            counter.increment();
            console.log(counter.count);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", result);
    }

    #endregion
}
