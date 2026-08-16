using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Conditional-type <c>infer</c> extracted from a generic INSTANTIATION on the extends side —
/// <c>T extends Box&lt;infer V&gt; ? V : …</c>, <c>T extends Map&lt;any, infer V&gt; ? V : …</c> (#347).
/// These flow through the string-expansion instantiation path (TypeChecker.Generics.cs), which
/// previously lost the binding two ways: the check-side generic-class instance is wrapped in
/// <c>TypeInfo.Instance</c> so the structural match never reached the type arguments, and the
/// true-branch reference to the infer name was parsed with no binding in scope (collapsing to
/// <c>any</c>). The built-in collections (Map/Set/WeakMap/WeakSet) additionally needed their type
/// REFERENCES to resolve to their dedicated TypeInfo records rather than the <c>any</c> fallback.
/// </summary>
public class ConditionalInferFromGenericInstantiationTests
{
    // ---- user-defined generic class (the Box example) ----

    [Fact]
    public void UserGenericClass_InfersTypeArgument()
    {
        // Unbox<Box<number>> resolves to number; returning a string violates it.
        var source = """
            class Box<T> { value!: T; }
            type Unbox<T> = T extends Box<infer V> ? V : "no";
            function f(): Unbox<Box<number>> { return "hello"; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void UserGenericClass_InfersTypeArgument_CorrectAssignmentAccepted()
    {
        var source = """
            class Box<T> { value!: T; }
            type Unbox<T> = T extends Box<infer V> ? V : "no";
            function f(): Unbox<Box<number>> { return 42; }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void UserGenericClass_NoMatch_TakesFalseBranch()
    {
        // `string` is not a Box, so V never binds and the conditional resolves to its false branch
        // ("no"); returning a number violates that literal.
        var source = """
            class Box<T> { value!: T; }
            type Unbox<T> = T extends Box<infer V> ? V : "no";
            function f(): Unbox<string> { return 42; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void UserGenericClass_NoMatch_FalseBranchValueAccepted()
    {
        var source = """
            class Box<T> { value!: T; }
            type Unbox<T> = T extends Box<infer V> ? V : "no";
            function f(): Unbox<string> { return "no"; }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void UserGenericClass_TwoTypeParams_InfersSelectedArgument()
    {
        // The infer is the SECOND type argument; the first matches `any`.
        var source = """
            class Pair<A, B> { a!: A; b!: B; }
            type Second<T> = T extends Pair<any, infer V> ? V : never;
            function f(): Second<Pair<string, number>> { return "hello"; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- built-in collection instantiations (the Map example) ----

    [Fact]
    public void BuiltInMap_InfersValueType()
    {
        var source = """
            type MapValue<T> = T extends Map<any, infer V> ? V : never;
            let m: MapValue<Map<string, number>> = "not a number";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void BuiltInMap_InfersValueType_CorrectAssignmentAccepted()
    {
        var source = """
            type MapValue<T> = T extends Map<any, infer V> ? V : never;
            let m: MapValue<Map<string, number>> = 5;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void BuiltInMap_InfersKeyType()
    {
        var source = """
            type MapKey<T> = T extends Map<infer K, any> ? K : never;
            function f(): MapKey<Map<string, number>> { return 42; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void BuiltInSet_InfersElementType()
    {
        var source = """
            type Elem<T> = T extends Set<infer V> ? V : "none";
            function f(): Elem<Set<number>> { return "hello"; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void BuiltInWeakMap_InfersValueType()
    {
        var source = """
            type WMValue<T> = T extends WeakMap<any, infer V> ? V : never;
            function f(): WMValue<WeakMap<object, number>> { return "hello"; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void BuiltInGenerator_InfersYieldType()
    {
        var source = """
            type Yield<T> = T extends Generator<infer V> ? V : "none";
            function f(): Yield<Generator<number>> { return "hello"; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- array instantiation through the alias string-expansion path ----

    [Fact]
    public void ArrayElement_InfersElementType()
    {
        var source = """
            type Elem<T> = T extends (infer V)[] ? V : "none";
            function f(): Elem<number[]> { return "hello"; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- the dedicated-collection type references are now strongly typed (#347 sub-fix) ----

    [Fact]
    public void MapTypeReference_IsStronglyTyped_RejectsMismatchedValue()
    {
        // A Map<string, number> reference resolves to the dedicated Map type (not `any`), so an
        // incompatible Map assignment is now caught.
        var source = """
            let m: Map<string, number> = new Map<string, string>();
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void MapTypeReference_CommonOperationsTypeCheck()
    {
        // Regression guard: strongly-typed Map/Set references must not produce spurious errors on
        // ordinary member access and iteration.
        var source = """
            function use(m: Map<string, number>, s: Set<number>): void {
              m.set("a", 1);
              const v: number | null = m.get("a");
              const sz: number = m.size + s.size;
              for (const [k, val] of m) { console.log(k, val); }
              for (const x of s) { console.log(x); }
            }
            """;
        TestHarness.RunInterpreted(source);
    }
}
