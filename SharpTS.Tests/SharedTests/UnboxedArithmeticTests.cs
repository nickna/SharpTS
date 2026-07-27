using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for arithmetic operations with typed locals, boxing boundaries,
/// compound assignments, comparisons, functions, classes, and arrow functions.
/// Runs against both interpreter and compiler.
/// </summary>
public class UnboxedArithmeticTests
{
    #region Numeric Chain Tests

    [Theory, ModeData]
    public void NumericChain_NoIntermediateBoxing(ExecutionMode mode)
    {
        var source = """
            let x: number = 1 + 2 + 3 + 4 + 5;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void TypedLocal_StaysUnboxed(ExecutionMode mode)
    {
        var source = """
            let a: number = 10;
            let b: number = a * 2;
            let c: number = b + a;
            console.log(c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("30\n", output);
    }

    [Theory, ModeData]
    public void MultipleArithmeticOps_ComplexExpression(ExecutionMode mode)
    {
        var source = """
            let x: number = (10 - 3) * 4 + 2 / 2;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("29\n", output);
    }

    [Theory, ModeData]
    public void LongChain_AllOperators(ExecutionMode mode)
    {
        var source = """
            let x: number = 100 - 20 + 5 * 2 - 30 / 3;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("80\n", output);
    }

    #endregion

    #region Typed Local Tests

    [Theory, ModeData]
    public void ExplicitNumberType_UsesUnboxedLocal(ExecutionMode mode)
    {
        var source = """
            let x: number = 42;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void InferredType_FallsBackToObject(ExecutionMode mode)
    {
        var source = """
            let x = 42;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    // CompiledOnly: Uninitialized `let x: number` defaults to 0 in compiled mode
    // (.NET value type default) but produces `undefined` in the interpreter (correct per JS spec).
    // This is a CLR platform constraint — .NET locals of value types are zero-initialized.
    [Theory, CompiledOnlyData]
    public void UninitializedNumberLocal_DefaultsToZero(ExecutionMode mode)
    {
        var source = """
            let x: number;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void TypedLocal_ReassignmentWorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            x = 20;
            x = x + 5;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("25\n", output);
    }

    #endregion

    #region Boxing Boundary Tests

    [Theory, ModeData]
    public void FunctionCallWithNumericArg_BoxesCorrectly(ExecutionMode mode)
    {
        var source = """
            function f(x: number): void {
                console.log(x);
            }
            f(1 + 2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void ReturnNumericValue_BoxesCorrectly(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                return 1 + 2;
            }
            console.log(f());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void PropertySetWithNumeric_BoxesCorrectly(ExecutionMode mode)
    {
        var source = """
            class C {
                x: number = 0;
            }
            let c = new C();
            c.x = 1 + 2;
            console.log(c.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void ArrayElementWithNumeric_BoxesCorrectly(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr[0] = 4 + 5;
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("9\n", output);
    }

    [Theory, ModeData]
    public void ConsoleLogWithNumericExpr_BoxesCorrectly(ExecutionMode mode)
    {
        var source = """
            console.log(10 * 5 - 3);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("47\n", output);
    }

    #endregion

    #region Mixed Type Tests

    [Theory, ModeData]
    public void MixedAnyAndNumber_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: any = 5;
            let y: number = (x as number) * 2;
            console.log(y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void ObjectLocalWithNumber_FallsBackToBoxing(ExecutionMode mode)
    {
        var source = """
            let x: any = 10;
            let y: number = x + 1;
            console.log(y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("11\n", output);
    }

    [Theory, ModeData]
    public void NumberAndStringConcatenation_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            let s: string = "Value: " + x;
            console.log(s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Value: 5\n", output);
    }

    #endregion

    #region Control Flow Tests

    [Theory, ModeData]
    public void TernaryWithNumericBranches_ProducesCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = true ? 1 + 2 : 3 + 4;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void TernaryWithNumericBranches_FalseBranch(ExecutionMode mode)
    {
        var source = """
            let x: number = false ? 1 + 2 : 3 + 4;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory, ModeData]
    public void IfElseWithNumericAssignment_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 0;
            if (true) {
                x = 1 + 2;
            } else {
                x = 3 + 4;
            }
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void LoopWithNumericAccumulator_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let sum: number = 0;
            let i: number = 0;
            while (i < 5) {
                sum = sum + i;
                i = i + 1;
            }
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void ForOfWithNumericSum_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            let sum: number = 0;
            for (let n of arr) {
                sum = sum + n;
            }
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    #endregion

    #region Compound Assignment Tests

    [Theory, ModeData]
    public void CompoundAddWithTypedLocal_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            x += 3;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory, ModeData]
    public void CompoundSubtractWithTypedLocal_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            x -= 3;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory, ModeData]
    public void CompoundMultiplyWithTypedLocal_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            x *= 4;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory, ModeData]
    public void CompoundDivideWithTypedLocal_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 20;
            x /= 4;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory, ModeData]
    public void IncrementTypedLocal_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            x++;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void DecrementTypedLocal_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            x--;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    [Theory, ModeData]
    public void PrefixIncrementTypedLocal_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            console.log(++x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void PrefixDecrementTypedLocal_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            console.log(--x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    #endregion

    #region Comparison Tests

    [Theory, ModeData]
    public void NumericComparison_ProducesBoolean(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            let y: number = 3;
            console.log(x > y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ComparisonInCondition_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            if (x > 3) {
                console.log("yes");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\n", output);
    }

    [Theory, ModeData]
    public void ChainedComparisons_WorkCorrectly(ExecutionMode mode)
    {
        var source = """
            let a: number = 1;
            let b: number = 2;
            let c: number = 3;
            console.log(a < b && b < c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ComparisonWithArithmetic_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            let y: number = 5;
            console.log(x - 3 > y - 2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Function with Numeric Locals Tests

    [Theory, ModeData]
    public void FunctionWithTypedLocals_ComputesCorrectly(ExecutionMode mode)
    {
        var source = """
            function compute(a: number, b: number): number {
                let result: number = a * b;
                result = result + 10;
                return result;
            }
            console.log(compute(3, 4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("22\n", output);
    }

    [Theory, ModeData]
    public void RecursiveFunctionWithNumbers_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            function factorial(n: number): number {
                if (n <= 1) {
                    return 1;
                }
                return n * factorial(n - 1);
            }
            console.log(factorial(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("120\n", output);
    }

    [Theory, ModeData]
    public void NestedFunctionCalls_WorkCorrectly(ExecutionMode mode)
    {
        var source = """
            function double(x: number): number {
                return x * 2;
            }
            function triple(x: number): number {
                return x * 3;
            }
            console.log(double(triple(5)));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("30\n", output);
    }

    #endregion

    #region Class Method Tests

    [Theory, ModeData]
    public void ClassMethodWithNumericComputation_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            class Calculator {
                value: number = 0;

                add(x: number): void {
                    this.value = this.value + x;
                }

                getDouble(): number {
                    return this.value * 2;
                }
            }

            let calc = new Calculator();
            calc.add(5);
            calc.add(10);
            console.log(calc.getDouble());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("30\n", output);
    }

    [Theory, ModeData]
    public void ClassWithNumericField_ArithmeticWorksCorrectly(ExecutionMode mode)
    {
        var source = """
            class Point {
                x: number;
                y: number;

                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }

                distanceFromOrigin(): number {
                    return Math.sqrt(this.x * this.x + this.y * this.y);
                }
            }

            let p = new Point(3, 4);
            console.log(p.distanceFromOrigin());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory, ModeData]
    public void ZeroOperations_WorkCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 0;
            let y: number = x + 0;
            let z: number = y * 0;
            console.log(z);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void NegativeNumbers_WorkCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = -5;
            let y: number = -3;
            let z: number = x * y;
            console.log(z);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void FloatingPointOperations_WorkCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 3.5;
            let y: number = 2.5;
            console.log(x + y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void DivisionBySmallNumber_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            let y: number = 3;
            console.log(x / y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("3.333", output);
    }

    #endregion

    #region Typed Return Value Tests

    [Theory, ModeData]
    public void TypedReturn_FibonacciRecursion_WorksCorrectly(ExecutionMode mode)
    {
        // Tests recursive typed returns with multiple return points
        var source = """
            function fib(n: number): number {
                if (n <= 1) {
                    return n;
                }
                return fib(n - 1) + fib(n - 2);
            }
            console.log(fib(10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("55\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_MutualRecursion_WorksCorrectly(ExecutionMode mode)
    {
        // Tests mutually recursive functions with typed returns
        var source = """
            function isEven(n: number): boolean {
                if (n === 0) return true;
                return isOdd(n - 1);
            }
            function isOdd(n: number): boolean {
                if (n === 0) return false;
                return isEven(n - 1);
            }
            console.log(isEven(10));
            console.log(isOdd(7));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_DeepNesting_WorksCorrectly(ExecutionMode mode)
    {
        // Tests deeply nested function calls with typed returns
        var source = """
            function a(x: number): number { return x + 1; }
            function b(x: number): number { return a(x) * 2; }
            function c(x: number): number { return b(x) - 3; }
            function d(x: number): number { return c(x) + 10; }
            function e(x: number): number { return d(x) / 2; }
            console.log(e(5));
            """;

        var output = TestHarness.Run(source, mode);
        // e(5) = d(5)/2 = (c(5)+10)/2 = ((b(5)-3)+10)/2 = (((a(5)*2)-3)+10)/2
        // = (((6*2)-3)+10)/2 = ((12-3)+10)/2 = (9+10)/2 = 19/2 = 9.5
        Assert.Equal("9.5\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_ChainedInExpression_WorksCorrectly(ExecutionMode mode)
    {
        // Tests using typed return values in complex expressions
        var source = """
            function square(x: number): number { return x * x; }
            function cube(x: number): number { return x * x * x; }
            let result: number = square(3) + cube(2) - square(2);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        // 9 + 8 - 4 = 13
        Assert.Equal("13\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_InTernaryCondition_WorksCorrectly(ExecutionMode mode)
    {
        // Tests typed return values used in ternary expressions
        var source = """
            function getValue(): number { return 10; }
            function getThreshold(): number { return 5; }
            let result: string = getValue() > getThreshold() ? "above" : "below";
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("above\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_BooleanFunction_WorksCorrectly(ExecutionMode mode)
    {
        // Tests functions with boolean return type
        var source = """
            function isPositive(n: number): boolean {
                return n > 0;
            }
            function isNegative(n: number): boolean {
                return n < 0;
            }
            console.log(isPositive(5));
            console.log(isNegative(-3));
            console.log(isPositive(-1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_StringFunction_WorksCorrectly(ExecutionMode mode)
    {
        // Tests functions with string return type
        var source = """
            function greet(name: string): string {
                return "Hello, " + name + "!";
            }
            console.log(greet("World"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World!\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_UsedAsArgument_WorksCorrectly(ExecutionMode mode)
    {
        // Tests passing typed return values directly as arguments
        var source = """
            function double(x: number): number { return x * 2; }
            function add(a: number, b: number): number { return a + b; }
            console.log(add(double(3), double(4)));
            """;

        var output = TestHarness.Run(source, mode);
        // add(6, 8) = 14
        Assert.Equal("14\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_InWhileCondition_WorksCorrectly(ExecutionMode mode)
    {
        // Tests typed boolean return in loop condition
        var source = """
            let count: number = 0;
            function shouldContinue(): boolean {
                count = count + 1;
                return count < 5;
            }
            while (shouldContinue()) {
                console.log(count);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n4\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_InIfCondition_WorksCorrectly(ExecutionMode mode)
    {
        // Tests typed boolean return in if condition
        var source = """
            function check(x: number): boolean {
                return x > 10;
            }
            if (check(15)) {
                console.log("greater");
            } else {
                console.log("less");
            }
            if (check(5)) {
                console.log("greater");
            } else {
                console.log("less");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("greater\nless\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_MultipleReturnPaths_WorksCorrectly(ExecutionMode mode)
    {
        // Tests function with multiple return statements
        var source = """
            function classify(n: number): string {
                if (n < 0) {
                    return "negative";
                }
                if (n === 0) {
                    return "zero";
                }
                if (n < 10) {
                    return "small";
                }
                return "large";
            }
            console.log(classify(-5));
            console.log(classify(0));
            console.log(classify(7));
            console.log(classify(100));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("negative\nzero\nsmall\nlarge\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_InArrayMap_WorksCorrectly(ExecutionMode mode)
    {
        // Tests typed return with array operations
        var source = """
            function double(x: number): number {
                return x * 2;
            }
            let arr: number[] = [1, 2, 3, 4, 5];
            let doubled = arr.map(x => double(x));
            console.log(doubled.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2,4,6,8,10\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_ClassMethodReturningNumber_WorksCorrectly(ExecutionMode mode)
    {
        // Tests class methods with typed returns
        var source = """
            class Counter {
                value: number = 0;

                increment(): number {
                    this.value = this.value + 1;
                    return this.value;
                }

                getDoubled(): number {
                    return this.value * 2;
                }
            }
            let c = new Counter();
            console.log(c.increment());
            console.log(c.increment());
            console.log(c.getDoubled());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n4\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_ClassMethodChaining_WorksCorrectly(ExecutionMode mode)
    {
        // Tests chaining calls on class methods with typed returns
        var source = """
            class Math2 {
                static square(x: number): number {
                    return x * x;
                }
                static cube(x: number): number {
                    return x * x * x;
                }
            }
            console.log(Math2.square(Math2.cube(2)));
            """;

        var output = TestHarness.Run(source, mode);
        // cube(2) = 8, square(8) = 64
        Assert.Equal("64\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_WithDefaultParameter_WorksCorrectly(ExecutionMode mode)
    {
        // Tests typed return with default parameters
        var source = """
            function multiply(x: number, factor: number = 2): number {
                return x * factor;
            }
            console.log(multiply(5));
            console.log(multiply(5, 3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n15\n", output);
    }

    [Theory, ModeData]
    public void TypedReturn_TailRecursion_WorksCorrectly(ExecutionMode mode)
    {
        // Tests tail-recursive function with typed return
        var source = """
            function sumTo(n: number, acc: number = 0): number {
                if (n <= 0) {
                    return acc;
                }
                return sumTo(n - 1, acc + n);
            }
            console.log(sumTo(10));
            """;

        var output = TestHarness.Run(source, mode);
        // 10 + 9 + 8 + 7 + 6 + 5 + 4 + 3 + 2 + 1 = 55
        Assert.Equal("55\n", output);
    }

    #endregion

    #region Arrow Function Typed Return Tests

    [Theory, ModeData]
    public void ArrowTypedReturn_ExpressionBody_Number(ExecutionMode mode)
    {
        // Tests arrow function with expression body returning number
        var source = """
            const double = (x: number): number => x * 2;
            console.log(double(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_ExpressionBody_Boolean(ExecutionMode mode)
    {
        // Tests arrow function with expression body returning boolean
        var source = """
            const isPositive = (x: number): boolean => x > 0;
            console.log(isPositive(5));
            console.log(isPositive(-3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_ExpressionBody_String(ExecutionMode mode)
    {
        // Tests arrow function with expression body returning string
        var source = """
            const greet = (name: string): string => "Hello, " + name;
            console.log(greet("World"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_BlockBody_Number(ExecutionMode mode)
    {
        // Tests arrow function with block body returning number
        var source = """
            const square = (x: number): number => {
                let result: number = x * x;
                return result;
            };
            console.log(square(7));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("49\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_BlockBody_MultipleReturns(ExecutionMode mode)
    {
        // Tests arrow function with multiple return paths
        var source = """
            const abs = (x: number): number => {
                if (x < 0) {
                    return -x;
                }
                return x;
            };
            console.log(abs(-5));
            console.log(abs(3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n3\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_NestedArrows(ExecutionMode mode)
    {
        // Tests nested arrow functions with typed returns
        var source = """
            const outer = (x: number): number => {
                const inner = (y: number): number => y * 2;
                return inner(x) + 1;
            };
            console.log(outer(5));
            """;

        var output = TestHarness.Run(source, mode);
        // inner(5) = 10, outer = 10 + 1 = 11
        Assert.Equal("11\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_ArrayMap(ExecutionMode mode)
    {
        // Tests arrow function typed return with array map
        var source = """
            const numbers: number[] = [1, 2, 3, 4, 5];
            const doubled = numbers.map((x: number): number => x * 2);
            console.log(doubled.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2,4,6,8,10\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_ArrayFilter(ExecutionMode mode)
    {
        // Tests arrow function typed return with array filter
        var source = """
            const numbers: number[] = [1, 2, 3, 4, 5, 6];
            const evens = numbers.filter((x: number): boolean => x % 2 === 0);
            console.log(evens.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2,4,6\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_ArrayReduce(ExecutionMode mode)
    {
        // Tests arrow function typed return with array reduce
        var source = """
            const numbers: number[] = [1, 2, 3, 4, 5];
            const sum = numbers.reduce((acc: number, x: number): number => acc + x, 0);
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_ChainedCalls(ExecutionMode mode)
    {
        // Tests chaining calls on arrow functions with typed returns
        var source = """
            const double = (x: number): number => x * 2;
            const square = (x: number): number => x * x;
            const addOne = (x: number): number => x + 1;
            console.log(addOne(square(double(3))));
            """;

        var output = TestHarness.Run(source, mode);
        // double(3) = 6, square(6) = 36, addOne(36) = 37
        Assert.Equal("37\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_WithClosure(ExecutionMode mode)
    {
        // Tests arrow function with closure and typed return
        var source = """
            function makeMultiplier(factor: number): (x: number) => number {
                return (x: number): number => x * factor;
            }
            const triple = makeMultiplier(3);
            console.log(triple(4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("12\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_VoidExpression_NoError(ExecutionMode mode)
    {
        // Tests arrow function with void expression body (like console.log)
        // Should not error and should handle void correctly
        var source = """
            const log = (msg: string): void => console.log(msg);
            log("Hello");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_InTernary(ExecutionMode mode)
    {
        // Tests arrow function typed return used in ternary expression
        var source = """
            const getValue = (flag: boolean): number => flag ? 10 : 20;
            console.log(getValue(true));
            console.log(getValue(false));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_RecursiveArrow(ExecutionMode mode)
    {
        // Tests recursive arrow function with typed return (using let for self-reference)
        var source = """
            let factorial: (n: number) => number;
            factorial = (n: number): number => n <= 1 ? 1 : n * factorial(n - 1);
            console.log(factorial(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("120\n", output);
    }

    [Theory, ModeData]
    public void ArrowTypedReturn_AsMethodCallback(ExecutionMode mode)
    {
        // Tests arrow function with typed return as method callback
        var source = """
            class Calculator {
                value: number = 0;

                apply(fn: (x: number) => number): void {
                    this.value = fn(this.value);
                }
            }
            let calc = new Calculator();
            calc.value = 5;
            calc.apply((x: number): number => x * 2);
            console.log(calc.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    #endregion
}
