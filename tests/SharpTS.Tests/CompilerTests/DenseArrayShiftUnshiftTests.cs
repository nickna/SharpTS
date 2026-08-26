using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class DenseArrayShiftUnshiftTests
{
    [Fact]
    public void PromotedPrimitiveArrays_PreserveValuesLengthAndEmptyResult()
    {
        const string source = """
            function numbers(): void {
                const values: number[] = [];
                values.push(2, 3);
                console.log(values.unshift(0, 1));
                console.log(values.shift(), values.shift(), values.length);
                values.shift();
                values.shift();
                console.log(values.shift() === undefined, values.length);
            }

            function booleans(): void {
                const values: boolean[] = [];
                values.push(false);
                console.log(values.unshift(true), values.shift(), values.shift());
                console.log(values.shift() === undefined);
            }

            numbers();
            booleans();
            """;

        Assert.Equal(
            "4\n0 1 2\ntrue 0\n2 true false\ntrue\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void PromotedUnshift_EvaluatesAllArgumentsBeforeMutation()
    {
        const string source = """
            const values: number[] = [];
            values.push(10);
            console.log(values.unshift(values.length, values.length));
            console.log(values[0], values[1], values[2]);
            """;

        Assert.Equal("3\n1 1 10\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void EscapingNumberArray_UsesPackedMutatorsWithoutChangingSemantics()
    {
        const string source = """
            function edit(values: number[]): void {
                console.log(values.unshift(-2, -1));
                console.log(values.shift(), values.shift(), values.length);
            }

            const values: number[] = [];
            values.push(0, 1, 2);
            edit(values);
            console.log(values[0], values[2]);

            const boxed: number[] = [1, 2];
            (boxed as any)[0] = "x";
            console.log(boxed.unshift(9), boxed.shift(), boxed.shift(), boxed.length);
            """;

        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("5\n-2 -1 3\n0 2\n3 9 x 1\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void ReplacedShiftAndUnshift_RemainObservable()
    {
        const string source = """
            const originalShift: any = Array.prototype.shift;
            const originalUnshift: any = Array.prototype.unshift;
            Array.prototype.shift = function(): number { return 77; };
            Array.prototype.unshift = function(...items: any[]): number {
                return 80 + items.length;
            };
            console.log((Array.prototype as any).shift === originalShift);

            const values: number[] = [];
            values.push(1);
            console.log(values.shift(), values.unshift(2, 3), values.length);

            Array.prototype.shift = originalShift;
            Array.prototype.unshift = originalUnshift;
            """;

        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        Assert.True(new RuntimeFeatureDetector()
            .Detect(statements, typeMap).UsesArrayPrototypeMutation);
        Assert.Equal("false\n77 82 1\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void HoleAccessorFrozenAndBorrowedCases_UseGenericSemantics()
    {
        const string source = """
            const holey: any[] = [, 2];
            console.log(holey.shift() === undefined, holey[0], holey.length);

            const observed: any[] = [1, 2];
            let getterCalls: number = 0;
            Object.defineProperty(observed, 1, {
                get(): number { getterCalls = getterCalls + 1; return 9; }
            });
            console.log(observed.shift(), observed[0], getterCalls);

            const frozen: number[] = [];
            Object.freeze(frozen);
            try { frozen.unshift(1); } catch (error) {
                console.log(error instanceof TypeError);
            }

            const sealed: number[] = [1, 2];
            Object.seal(sealed);
            try { sealed.shift(); } catch (error) {
                console.log(error instanceof TypeError);
            }

            Object.defineProperty(Array.prototype, "0", {
                configurable: true,
                get(): number { return 41; }
            });
            const inherited: any[] = [];
            inherited.length = 1;
            console.log(inherited.shift(), inherited.length);
            delete (Array.prototype as any)[0];

            const generic: any = { 0: "tail", length: 1 };
            console.log(Array.prototype.unshift.call(generic, "head"));
            console.log(Array.prototype.shift.call(generic), generic[0], generic.length);
            """;

        Assert.Equal(
            "true 2 1\n1 9 1\ntrue\ntrue\n41 0\n2\nhead tail 1\n",
            TestHarness.RunCompiled(source));
    }


    [Fact]
    public void DeletedShift_RemainsObservable()
    {
        const string source = """
            const originalShift: any = Array.prototype.shift;
            delete (Array.prototype as any).shift;
            const values: number[] = [1];
            try { values.shift(); } catch (error) {
                console.log(error instanceof TypeError);
            }
            Array.prototype.shift = originalShift;
            """;

        Assert.Equal("true\n", TestHarness.RunCompiled(source));
    }
}
