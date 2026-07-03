using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Bigint literal types (#1207): <c>type T = 1n</c> resolves to TypeInfo.BigIntLiteral with
/// literal identity (1n vs 2n distinct), widening to bigint, tsc const/let widening rules,
/// and typeof/equality/truthiness narrowing — mirroring number literal types throughout.
/// Before #1207 these annotations resolved to <c>any</c> (string-scanner parity placeholder).
/// </summary>
public class BigIntLiteralTypeTests
{
    #region Annotation resolution + assignability

    [Fact]
    public void Annotation_AcceptsMatchingLiteral()
    {
        TestHarness.RunInterpreted("""
            type One = 1n;
            let a: One = 1n;
            let u: 1n | 2n = 2n;
            """);
    }

    [Fact]
    public void Annotation_RejectsDifferentLiteral()
    {
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            let x: 1n = 2n;
            """));
        Assert.Contains("'2n' is not assignable to type '1n'", ex.Message);
    }

    [Fact]
    public void Literal_WidensToBigint()
    {
        TestHarness.RunInterpreted("""
            let w: bigint = 1n;
            function f(x: bigint): bigint { return x; }
            f(42n);
            """);
    }

    [Fact]
    public void Bigint_NotAssignableToLiteral()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            let l = 1n; // let widens to bigint
            let z: 1n = l;
            """));
    }

    [Fact]
    public void Union_RejectsNonMember()
    {
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            let x: 1n | 2n = 3n;
            """));
        Assert.Contains("'3n' is not assignable to type '1n | 2n'", ex.Message);
    }

    [Fact]
    public void NumberAndBigintLiterals_AreNotInterchangeable()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            let x: 1n = 1;
            """));
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            let y: 1 = 1n;
            """));
    }

    #endregion

    #region const/let widening

    [Fact]
    public void ConstKeepsLiteral_LetWidens()
    {
        TestHarness.RunInterpreted("""
            const c = 1n;      // const x = 1n ⇒ 1n
            const k: 1n = c;
            let l = 1n;        // let x = 1n ⇒ bigint
            l = 99n;
            """);
    }

    [Fact]
    public void ObjectLiteralMember_WidensToBigint()
    {
        // Freshness widening inside an object literal mirrors the number rule:
        // const o = { v: 1n } ⇒ { v: bigint }, keeping o.v = 9n legal.
        TestHarness.RunInterpreted("""
            const o = { v: 1n };
            o.v = 9n;
            """);
    }

    #endregion

    #region Narrowing

    [Fact]
    public void TypeofNarrowing_KeepsLiteralConstituents()
    {
        TestHarness.RunInterpreted("""
            function f(x: 1n | 2n | string): string {
                if (typeof x === "bigint") {
                    const y: 1n | 2n = x;
                    return (x * 2n).toString();
                }
                return x;
            }
            console.log(f(2n), f("s"));
            """);
    }

    [Fact]
    public void EqualityNarrowing_NarrowsLiteralUnion()
    {
        TestHarness.RunInterpreted("""
            function g(x: 1n | 2n): string {
                if (x === 1n) {
                    const y: 1n = x;
                    return "one";
                }
                const z: 2n = x;
                return "two";
            }
            console.log(g(1n), g(2n));
            """);
    }

    [Fact]
    public void EqualityNarrowing_GeneralBigintNarrowsToLiteral()
    {
        TestHarness.RunInterpreted("""
            function h(x: bigint | string): string {
                if (x === 1n) {
                    const y: 1n = x;
                    return "one";
                }
                return "other";
            }
            console.log(h(1n), h("s"));
            """);
    }

    [Fact]
    public void TruthinessNarrowing_ZeroBigintIsFalsy()
    {
        TestHarness.RunInterpreted("""
            function t(x: 0n | 1n): string {
                if (x) {
                    const y: 1n = x;
                    return "one";
                }
                const z: 0n = x;
                return "zero";
            }
            console.log(t(0n), t(1n));
            """);
    }

    #endregion

    #region Interactions

    [Fact]
    public void TemplateLiteralType_ExpandsBigintLiteralWithoutSuffix()
    {
        // `${1n}` stringifies as "1" (no 'n'), exactly like runtime template interpolation.
        TestHarness.RunInterpreted("""
            type V = `v${1n}`;
            const v: V = "v1";
            """);
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type V = `v${1n}`;
            const w: V = "v1n";
            """));
    }

    [Fact]
    public void ConstTypeParameter_PreservesBigintLiteral()
    {
        TestHarness.RunInterpreted("""
            function id<const T>(x: T): T { return x; }
            const r: 5n = id(5n);
            """);
    }

    [Fact]
    public void LiteralTypedValue_HasBigintMembers()
    {
        // Member lookup on a literal-typed receiver resolves through the bigint category.
        TestHarness.RunInterpreted("""
            const c = 255n;
            const s: string = c.toString(16);
            console.log(s);
            """);
    }

    [Fact]
    public void BigIntConstructor_AcceptsLiteralTypedArgument()
    {
        TestHarness.RunInterpreted("""
            const c = 5n;
            let b: bigint = BigInt(c);
            console.log(b);
            """);
    }

    #endregion
}
