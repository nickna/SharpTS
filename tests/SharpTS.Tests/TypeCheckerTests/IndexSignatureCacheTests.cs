using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Regression tests for a compatibility-cache collision: the structural cache keys on
/// <c>TypeInfo.ToString()</c>, which previously omitted index signatures — so index-only object types
/// that differ only in their index value type (e.g. <c>{ [x: number]: Base }</c> vs
/// <c>{ [x: number]: Derived }</c>) collapsed to the same string and shared a cached (incorrect) result.
/// </summary>
public class IndexSignatureCacheTests
{
    [Fact]
    public void FailedIndexAssignment_DoesNotPoisonLaterValidAssignment()
    {
        // The first assignment is an error (Base is not assignable to a Derived index); the second is
        // valid and must NOT be reported as an error due to a cache collision between the two index types.
        var source = """
            class Base { foo: string; }
            class Derived extends Base { bar: string; }
            function f(b: { [x: number]: Base }, c: { [x: number]: Derived }) {
                var a: { [x: number]: Derived };
                a = c; // ok
                a = c; // ok
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void DistinctIndexValueTypes_AreNotConflated()
    {
        // `{ [x: number]: Base }` is not assignable to `{ [x: number]: Derived }`.
        var source = """
            class Base { foo: string; }
            class Derived extends Base { bar: string; }
            function f(b: { [x: number]: Base }) {
                var a: { [x: number]: Derived };
                a = b;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void IndexOnlyObjectType_RendersIndexSignature()
    {
        // Guard that the index signature is part of ToString (the cache key) — distinct value types differ.
        var a = new SharpTS.TypeSystem.TypeInfo.Record(
            System.Collections.Frozen.FrozenDictionary<string, SharpTS.TypeSystem.TypeInfo>.Empty,
            NumberIndexType: new SharpTS.TypeSystem.TypeInfo.String());
        var b = new SharpTS.TypeSystem.TypeInfo.Record(
            System.Collections.Frozen.FrozenDictionary<string, SharpTS.TypeSystem.TypeInfo>.Empty,
            NumberIndexType: new SharpTS.TypeSystem.TypeInfo.Primitive(SharpTS.Parsing.TokenType.TYPE_NUMBER));
        Assert.NotEqual(a.ToString(), b.ToString());
    }
}
