using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// The callback comparison rule and its substitution-origin gate (type-AST migration slice 5,
/// tsc's covariantCallbacks / #51620 semantics): function-typed parameters relate covariantly
/// (callbacks are output positions) — EXCEPT where the parameter position's declared type was a
/// naked type parameter before instantiation, which stays bivariant.
/// </summary>
public class CallbackVarianceTests
{
    [Fact]
    public void CallbackParameters_RelateCovariantly()
    {
        // List-of-B is assignable to List-of-A (covariant via the cb parameter), not vice versa.
        TestHarness.RunInterpreted("""
            interface A { a: string }
            interface B extends A { b: string }
            interface AList { forEach(cb: (item: A) => void): void; }
            interface BList { forEach(cb: (item: B) => void): void; }
            var a: AList;
            var b: BList;
            a = b;
            """);
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            interface A { a: string }
            interface B extends A { b: string }
            interface AList { forEach(cb: (item: A) => void): void; }
            interface BList { forEach(cb: (item: B) => void): void; }
            var a: AList;
            var b: BList;
            b = a;
            """));
    }

    [Fact]
    public void InstantiatedNakedTypeParam_StaysBivariant()
    {
        // tsc #51620: `set(value: T)` instantiated with function types is NOT a callback
        // position — the function-ness came from the argument, not the declaration. Both
        // directions stay legal (method bivariance).
        TestHarness.RunInterpreted("""
            type Bivar<T> = { set(value: T): void }
            declare let bfu: Bivar<(x: unknown) => void>;
            declare let bfs: Bivar<(x: string) => void>;
            bfu = bfs;
            bfs = bfu;
            """);
    }

    [Fact]
    public void InstantiatedGetterPosition_RelatesAsReturnPosition()
    {
        // A getter position (get(): T) relates as a plain return type, not via the callback
        // rule. Under the harness's default (non-strict) settings function params are bivariant,
        // so both directions are legal here; the strict-mode covariance error for `sx = sy` is
        // pinned by the covariantCallbacks conformance baseline (which runs @strict).
        TestHarness.RunInterpreted("""
            type SetLike<T> = { set(value: T): void, get(): T }
            declare let sx: SetLike<(x: unknown) => void>;
            declare let sy: SetLike<(x: string) => void>;
            sy = sx;
            sx = sy;
            """);
    }
}
