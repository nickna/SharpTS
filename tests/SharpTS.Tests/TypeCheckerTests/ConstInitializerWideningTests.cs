using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Regression tests for #458: a <c>const</c>-bound object/array literal must infer WIDENED
/// property/element types (<c>const o = { n: 1 }</c> ⇒ <c>{ n: number }</c>), so reassigning a
/// property to another value of the same base type is allowed. <c>const</c> freezes the binding,
/// not the mutability or declared type of an object literal's members — only <c>as const</c>
/// produces readonly literal-typed members. This is type-checker behaviour, shared by the
/// interpreted and compiled modes.
/// </summary>
public class ConstInitializerWideningTests
{
    // --- The headline bug: property/element reassignment on a const object/array literal ---

    [Theory, ModeData]
    public void ConstObjectLiteral_PropertyReassign_Allowed(ExecutionMode mode)
    {
        // `const o = { n: 1 }` has type `{ n: number }`, so `o.n = 9` is legal TypeScript.
        var output = TestHarness.Run("const o = { n: 1 }; o.n = 9; console.log(o.n);", mode);
        Assert.Equal("9\n", output);
    }

    [Fact]
    public void ConstObjectLiteral_StringProperty_Reassign_Ok()
    {
        TestHarness.RunInterpreted("const o = { s: \"a\" }; o.s = \"b\"; console.log(o.s);");
    }

    [Fact]
    public void ConstObjectLiteral_NestedProperty_Reassign_Ok()
    {
        // Widening recurses: `o.p` is `{ n: number }`, so `o.p.n = 9` is allowed.
        TestHarness.RunInterpreted("const o = { p: { n: 1 } }; o.p.n = 9; console.log(o.p.n);");
    }

    [Fact]
    public void ConstArrayLiteral_ElementReassign_Ok()
    {
        // `const arr = [1, 1, 1]` is `number[]`, not `1[]`, so `arr[0] = 9` is allowed.
        TestHarness.RunInterpreted("const arr = [1, 1, 1]; arr[0] = 9; console.log(arr[0]);");
    }

    [Fact]
    public void ConstObjectLiteral_SatisfiesObjectType_Reassign_Ok()
    {
        // `satisfies` is transparent to literal widening: `o` is still `{ n: number }`.
        TestHarness.RunInterpreted("const o = { n: 1 } satisfies { n: number }; o.n = 9; console.log(o.n);");
    }

    [Fact]
    public void ExportConstObjectLiteral_PropertyReassign_Ok()
    {
        // `export const` routes through the same VisitConst inference path.
        TestHarness.RunInterpreted("export const o = { n: 1 }; o.n = 9;");
    }

    // --- const still preserves TOP-LEVEL primitive literal types ---

    [Fact]
    public void ConstPrimitiveLiteral_KeepsLiteralType()
    {
        // `const x = 1` is the literal type `1` (not `number`), assignable to a `1`-typed binding.
        TestHarness.RunInterpreted("const x = 1; const y: 1 = x; console.log(y);");
    }

    // --- `as const` must STILL reject member reassignment (readonly literal types) ---

    [Fact]
    public void AsConstObject_PropertyReassign_Rejected()
    {
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("const o = { n: 1 } as const; o.n = 9;"));
    }

    [Fact]
    public void AsConstArray_ElementReassign_Rejected()
    {
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("const arr = [1, 2, 3] as const; arr[0] = 9;"));
    }

    [Fact]
    public void ConstAliasOfAsConst_PropertyReassign_Rejected()
    {
        // Aliasing an `as const` value is not a fresh object literal — its literal types are
        // preserved, so the reassignment through the alias is still rejected.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("const a = { n: 1 } as const; const o = a; o.n = 9;"));
    }

    // --- A nested `as const` member inside a plain const object literal is still preserved ---

    [Fact]
    public void ConstObjectLiteral_NestedAsConstMember_KeepsLiteralType()
    {
        // `o.p.n` is `1` (the nested `as const` is not widened away), so it is assignable to `1`.
        TestHarness.RunInterpreted("const o = { p: { n: 1 } as const }; const y: 1 = o.p.n; console.log(y);");
    }

    [Fact]
    public void ConstObjectLiteral_NestedAsConstMember_Reassign_Rejected()
    {
        // The nested `as const` keeps `o.p.n` at literal type `1`, so reassigning it is rejected.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("const o = { p: { n: 1 } as const }; o.p.n = 9;"));
    }

    [Theory, ModeData]
    public void ConstObjectLiteral_MixedPlainAndAsConstMembers(ExecutionMode mode)
    {
        // In the same object literal, a plain member widens (`o.b.m = 5` is allowed) while an
        // `as const` member keeps its literal types (`o.a.n` stays `1`).
        var source = "const o = { a: { n: 1 } as const, b: { m: 2 } }; o.b.m = 5; console.log(o.b.m);";
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    // --- #493: `as const` literal types survive `const`-initializer widening regardless of the
    // nesting position (array element, spread member), and `as const` members model readonly. ---

    [Theory, ModeData]
    public void AsConstArrayElement_KeepsLiteralType(ExecutionMode mode)
    {
        // `as const` on an *array element* inside a fresh array literal is no longer widened away:
        // `arr` is `{ readonly n: 1 }[]`, so `arr[0].n` is `1`, assignable to a `1`-typed binding.
        var source = "const arr = [{ n: 1 } as const]; const y: 1 = arr[0].n; console.log(y);";
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SpreadOfAsConstObject_KeepsLiteralType(ExecutionMode mode)
    {
        // Spreading an `as const` object into a fresh object literal preserves the (already-fixed)
        // literal member types: `o.a` is `1` (not widened to `number`).
        var source = "const x = { a: 1 } as const; const o = { ...x }; const y: 1 = o.a; console.log(y);";
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void AsConstObject_MemberWrite_ReportsReadonlyTS2540()
    {
        // Writing through an `as const` member is a read-only violation (TS2540), not a literal-type
        // mismatch (TS2322).
        var ex = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("const x = { a: 1 } as const; x.a = 9;"));
        Assert.Equal("TS2540", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void AsConstArrayElement_MemberWrite_ReportsReadonlyTS2540()
    {
        // The `as const` element is a readonly record, so writing its member is rejected with TS2540.
        var ex = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("const arr = [{ n: 1 } as const]; arr[0].n = 9;"));
        Assert.Equal("TS2540", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void DeepNestedAsConst_MemberWrite_ReportsReadonlyTS2540()
    {
        // `as const` is deep: every nested member is readonly, so `o.a.b.c = 9` is a TS2540 violation.
        var ex = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("const o = { a: { b: { c: 1 } } } as const; o.a.b.c = 9;"));
        Assert.Equal("TS2540", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void SpreadOfAsConst_FieldIsWritable_ReadonlyDropped()
    {
        // The spread *drops* readonly (the result object is a plain literal), so the field is writable
        // — assigning the same literal value is allowed.
        TestHarness.RunInterpreted("const x = { a: 1 } as const; const o = { ...x }; o.a = 1;");
    }

    [Fact]
    public void SpreadOfAsConst_FieldKeepsLiteralType_RejectsOtherValueWithTS2322()
    {
        // The spread preserves the literal type `1` (mutable), so writing a different value is a
        // literal-type mismatch (TS2322), NOT a readonly violation.
        var ex = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("const x = { a: 1 } as const; const o = { ...x }; o.a = 2;"));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Theory, ModeData]
    public void SpreadOfFreshObjectLiteral_Widens(ExecutionMode mode)
    {
        // Spreading a *fresh* inline object literal still widens its members (unlike an `as const`
        // source), so `o.a` is `number` and `o.a = 2` is allowed.
        var source = "const o = { ...{ a: 1 } }; o.a = 2; console.log(o.a);";
        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MixedSpread_PlainSourceWidens_AsConstSourcePreserved(ExecutionMode mode)
    {
        // A plain spread member widens (`o.a` is `number`, writable) while a later `as const` spread
        // member preserves its literal type (`o.b` is `2`). Later members win.
        var source = "const base = { a: 1 }; const ov = { b: 2 } as const; " +
                     "const o = { ...base, ...ov }; const yb: 2 = o.b; o.a = 99; console.log(o.a, yb);";
        Assert.Equal("99 2\n", TestHarness.Run(source, mode));
    }
}
