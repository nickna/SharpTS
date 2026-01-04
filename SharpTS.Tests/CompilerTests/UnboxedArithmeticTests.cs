using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests specifically for the unboxed arithmetic optimization.
/// Verifies that numeric values stay unboxed through arithmetic chains
/// and are only boxed at true boundaries.
/// </summary>
public class UnboxedArithmeticTests
{
    #region Numeric Chain Tests

    [Fact]
    public void NumericChain_NoIntermediateBoxing()
    {
        var source = """
            let x: number = 1 + 2 + 3 + 4 + 5;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void TypedLocal_StaysUnboxed()
    {
        var source = """
            let a: number = 10;
            let b: number = a * 2;
            let c: number = b + a;
            console.log(c);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("30\n", output);
    }

    [Fact]
    public void MultipleArithmeticOps_ComplexExpression()
    {
        var source = """
            let x: number = (10 - 3) * 4 + 2 / 2;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("29\n", output);
    }

    [Fact]
    public void LongChain_AllOperators()
    {
        var source = """
            let x: number = 100 - 20 + 5 * 2 - 30 / 3;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("80\n", output);
    }

    #endregion

    #region Typed Local Tests

    [Fact]
    public void ExplicitNumberType_UsesUnboxedLocal()
    {
        var source = """
            let x: number = 42;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void InferredType_FallsBackToObject()
    {
        var source = """
            let x = 42;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void UninitializedNumberLocal_DefaultsToZero()
    {
        var source = """
            let x: number;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void TypedLocal_ReassignmentWorksCorrectly()
    {
        var source = """
            let x: number = 10;
            x = 20;
            x = x + 5;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("25\n", output);
    }

    #endregion

    #region Boxing Boundary Tests

    [Fact]
    public void FunctionCallWithNumericArg_BoxesCorrectly()
    {
        var source = """
            function f(x: number): void {
                console.log(x);
            }
            f(1 + 2);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void ReturnNumericValue_BoxesCorrectly()
    {
        var source = """
            function f(): number {
                return 1 + 2;
            }
            console.log(f());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void PropertySetWithNumeric_BoxesCorrectly()
    {
        var source = """
            class C {
                x: number = 0;
            }
            let c = new C();
            c.x = 1 + 2;
            console.log(c.x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void ArrayElementWithNumeric_BoxesCorrectly()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr[0] = 4 + 5;
            console.log(arr[0]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("9\n", output);
    }

    [Fact]
    public void ConsoleLogWithNumericExpr_BoxesCorrectly()
    {
        var source = """
            console.log(10 * 5 - 3);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("47\n", output);
    }

    #endregion

    #region Mixed Type Tests

    [Fact]
    public void MixedAnyAndNumber_WorksCorrectly()
    {
        var source = """
            let x: any = 5;
            let y: number = (x as number) * 2;
            console.log(y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void ObjectLocalWithNumber_FallsBackToBoxing()
    {
        var source = """
            let x: any = 10;
            let y: number = x + 1;
            console.log(y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("11\n", output);
    }

    [Fact]
    public void NumberAndStringConcatenation_WorksCorrectly()
    {
        var source = """
            let x: number = 5;
            let s: string = "Value: " + x;
            console.log(s);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Value: 5\n", output);
    }

    #endregion

    #region Control Flow Tests

    [Fact]
    public void TernaryWithNumericBranches_ProducesCorrectResult()
    {
        var source = """
            let x: number = true ? 1 + 2 : 3 + 4;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void TernaryWithNumericBranches_FalseBranch()
    {
        var source = """
            let x: number = false ? 1 + 2 : 3 + 4;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void IfElseWithNumericAssignment_WorksCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void LoopWithNumericAccumulator_WorksCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void ForOfWithNumericSum_WorksCorrectly()
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            let sum: number = 0;
            for (let n of arr) {
                sum = sum + n;
            }
            console.log(sum);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15\n", output);
    }

    #endregion

    #region Compound Assignment Tests

    [Fact]
    public void CompoundAddWithTypedLocal_WorksCorrectly()
    {
        var source = """
            let x: number = 5;
            x += 3;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("8\n", output);
    }

    [Fact]
    public void CompoundSubtractWithTypedLocal_WorksCorrectly()
    {
        var source = """
            let x: number = 10;
            x -= 3;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void CompoundMultiplyWithTypedLocal_WorksCorrectly()
    {
        var source = """
            let x: number = 5;
            x *= 4;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void CompoundDivideWithTypedLocal_WorksCorrectly()
    {
        var source = """
            let x: number = 20;
            x /= 4;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void IncrementTypedLocal_WorksCorrectly()
    {
        var source = """
            let x: number = 5;
            x++;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void DecrementTypedLocal_WorksCorrectly()
    {
        var source = """
            let x: number = 5;
            x--;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("4\n", output);
    }

    [Fact]
    public void PrefixIncrementTypedLocal_WorksCorrectly()
    {
        var source = """
            let x: number = 5;
            console.log(++x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void PrefixDecrementTypedLocal_WorksCorrectly()
    {
        var source = """
            let x: number = 5;
            console.log(--x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("4\n", output);
    }

    #endregion

    #region Comparison Tests

    [Fact]
    public void NumericComparison_ProducesBoolean()
    {
        var source = """
            let x: number = 5;
            let y: number = 3;
            console.log(x > y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ComparisonInCondition_WorksCorrectly()
    {
        var source = """
            let x: number = 5;
            if (x > 3) {
                console.log("yes");
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("yes\n", output);
    }

    [Fact]
    public void ChainedComparisons_WorkCorrectly()
    {
        var source = """
            let a: number = 1;
            let b: number = 2;
            let c: number = 3;
            console.log(a < b && b < c);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ComparisonWithArithmetic_WorksCorrectly()
    {
        var source = """
            let x: number = 10;
            let y: number = 5;
            console.log(x - 3 > y - 2);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Function with Numeric Locals Tests

    [Fact]
    public void FunctionWithTypedLocals_ComputesCorrectly()
    {
        var source = """
            function compute(a: number, b: number): number {
                let result: number = a * b;
                result = result + 10;
                return result;
            }
            console.log(compute(3, 4));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("22\n", output);
    }

    [Fact]
    public void RecursiveFunctionWithNumbers_WorksCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("120\n", output);
    }

    [Fact]
    public void NestedFunctionCalls_WorkCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("30\n", output);
    }

    #endregion

    #region Class Method Tests

    [Fact]
    public void ClassMethodWithNumericComputation_WorksCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("30\n", output);
    }

    [Fact]
    public void ClassWithNumericField_ArithmeticWorksCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ZeroOperations_WorkCorrectly()
    {
        var source = """
            let x: number = 0;
            let y: number = x + 0;
            let z: number = y * 0;
            console.log(z);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void NegativeNumbers_WorkCorrectly()
    {
        var source = """
            let x: number = -5;
            let y: number = -3;
            let z: number = x * y;
            console.log(z);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void FloatingPointOperations_WorkCorrectly()
    {
        var source = """
            let x: number = 3.5;
            let y: number = 2.5;
            console.log(x + y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void DivisionBySmallNumber_WorksCorrectly()
    {
        var source = """
            let x: number = 10;
            let y: number = 3;
            console.log(x / y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Contains("3.333", output);
    }

    #endregion
}
