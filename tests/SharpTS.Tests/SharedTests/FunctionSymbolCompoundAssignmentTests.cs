using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FunctionSymbolCompoundAssignmentTests
{
    [Theory, ModeData]
    public void OrdinaryFunctionSymbolDataPropertiesSupportCompoundWrites(ExecutionMode mode)
    {
        const string source = """
            function C() {}
            const key = Symbol();
            const f: any = C;
            f[key] = 1;
            f[key] += 4;
            console.log(f[key]);
            const other = Symbol();
            f[other] = 3;
            console.log(f[key] *= 2, f[other]);
            """;
        Assert.Equal("5\n10 3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FunctionSymbolTargetAndKeyAreRetainedAcrossTheRightOperand(ExecutionMode mode)
    {
        const string source = """
            function C() {}
            function D() {}
            const f: any = C, other: any = D, key = Symbol();
            f[key] = 1; other[key] = 20;
            const events: string[] = [];
            let selected: any = f;
            function receiver(): any { events.push("receiver"); return selected; }
            function index(): symbol { events.push("key"); return key; }
            function right(): number { events.push("rhs"); f[key] = 100; selected = other; return 4; }
            console.log(receiver()[index()] += right(), f[key], other[key], events.join(","));
            """;
        Assert.Equal("5 5 20 receiver,key,rhs\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FunctionSymbolAccessorsAndKeyConversionUseOrdinarySemantics(ExecutionMode mode)
    {
        const string source = """
            function C() {}
            const f: any = C, key = Symbol();
            const events: string[] = [];
            let stored = 1;
            Object.defineProperty(f, key, {
                get() { events.push("get"); return stored; },
                set(value: number) { events.push("set:" + value); stored = value; },
                configurable: true
            });
            const index: any = { [Symbol.toPrimitive](hint: string) { events.push("key:" + hint); return key; } };
            function right(): number { events.push("rhs"); stored = 100; return 4; }
            console.log(f[index] += right(), stored, events.join(","));
            """;
        Assert.Equal("5 5 key:string,get,rhs,key:string,set:5\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void NonNumericArrayIndexStillFailsChecking()
        => Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(
            TestHarness.ParseOrThrow("const array = [1]; const key = Symbol(); array[key] += 1;")));
}
