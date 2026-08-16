using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Writing through a numeric index of a readonly tuple or readonly array must be rejected with
/// TS2542 ("Index signature in type '…' only permits reading."), matching tsc (#509). The element
/// type may even be compatible — readonly is a property of the receiver's index signature, not of
/// the value — so the readonly check fires ahead of the element-type compatibility check. Keyed off
/// the tuple/array's own <c>IsReadonly</c>: an array whose only readonly part is its element stays
/// writable at the slot level. Type-checker-only; errors are mode-independent so these run interpreted.
/// </summary>
public class ReadonlyIndexWriteTests
{
    private static void AssertTsCode(string expected, string source)
    {
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal(expected, ex.Diagnostic.TsCode);
    }

    // ---- readonly tuples (`as const`, `readonly [...]`) reject index writes ----

    [Fact]
    public void AsConstTuple_IndexWrite_SameValue_RejectedTS2542()
    {
        // `arr[0] = 1` writes the value already there, but a readonly tuple permits reading only.
        AssertTsCode("TS2542", "const arr = [1, 2, 3] as const; arr[0] = 1;");
    }

    [Fact]
    public void AsConstTuple_IndexWrite_WrongValue_IsReadonly_NotElementMismatch()
    {
        // Before #509 a wrong value surfaced as TS2322 (element mismatch); the readonly receiver is
        // the more specific, correct diagnosis — TS2542 — and fires first.
        AssertTsCode("TS2542", "const arr = [1, 2, 3] as const; arr[0] = 9;");
    }

    [Fact]
    public void ReadonlyTupleAnnotation_IndexWrite_RejectedTS2542()
    {
        AssertTsCode("TS2542", "const a: readonly [number, number] = [1, 2]; a[0] = 5;");
    }

    // ---- readonly arrays (`readonly T[]`, `ReadonlyArray<T>`) reject index writes ----

    [Fact]
    public void ReadonlyArrayAnnotation_IndexWrite_RejectedTS2542()
    {
        // The pre-existing gap independent of `as const`: a `readonly number[]` annotation.
        AssertTsCode("TS2542", "const a: readonly number[] = [1, 2, 3]; a[0] = 5;");
    }

    [Fact]
    public void ReadonlyArrayUtility_IndexWrite_RejectedTS2542()
    {
        // ReadonlyArray<T> routes through the interface readonly-number-index guard; regression cover.
        AssertTsCode("TS2542", "const a: ReadonlyArray<number> = [1, 2, 3]; a[0] = 5;");
    }

    [Fact]
    public void ReadonlyArray_CanonicalStringKey_IndexWrite_RejectedTS2542()
    {
        // `a["0"]` is the canonical numeric-string key — an element write too, so also TS2542.
        AssertTsCode("TS2542", "const a: readonly number[] = [1]; a[\"0\"] = 5;");
    }

    // ---- readonly arrays/tuples reached through a *union* reject index writes (#594) ----

    [Fact]
    public void ReadonlyOrMutableArrayUnion_IndexWrite_RejectedTS2542()
    {
        // `readonly number[] | number[]` must keep BOTH members distinct (the string resolver used to
        // mis-parse it as `readonly (number[] | number[])`, dropping the readonly member). With both
        // present, the write is invalid for the readonly member, so the whole union write is TS2542.
        AssertTsCode("TS2542", "function f(u: readonly number[] | number[]): void { u[0] = 1; }");
    }

    [Fact]
    public void MutableOrReadonlyArrayUnion_IndexWrite_RejectedTS2542()
    {
        // Order-independent: the readonly member rejects the write regardless of its position.
        AssertTsCode("TS2542", "function f(u: number[] | readonly number[]): void { u[0] = 1; }");
    }

    [Fact]
    public void ReadonlyOrMutableTupleUnion_IndexWrite_RejectedTS2542()
    {
        // The union-distribution helper previously did not handle tuples at all; a readonly tuple
        // member now rejects the write too.
        AssertTsCode("TS2542", "function f(u: readonly [number, number] | [number, number]): void { u[0] = 1; }");
    }

    [Fact]
    public void MutableArrayTupleUnion_IndexWrite_Accepted()
    {
        // A union of mutable members (array + tuple), where the write is valid for every member,
        // still accepts — the new tuple/readonly guards must not over-reject writable members.
        TestHarness.RunInterpreted("function f(u: number[] | [number, number]): void { u[0] = 1; }\nf([1, 2]);");
    }

    // ---- mutable receivers and reads stay accepted (no over-rejection) ----

    [Fact]
    public void MutableArray_IndexWrite_Accepted()
    {
        TestHarness.RunInterpreted("const a: number[] = [1, 2, 3]; a[0] = 9;");
    }

    [Fact]
    public void MutableTuple_IndexWrite_Accepted()
    {
        TestHarness.RunInterpreted("const a: [number, number] = [1, 2]; a[0] = 9;");
    }

    [Fact]
    public void ReadonlyArray_IndexRead_Accepted()
    {
        // Reading a readonly array/tuple by index is always fine — only writes are blocked.
        TestHarness.RunInterpreted("const a = [1, 2, 3] as const; const x: number = a[0];");
    }

    [Fact]
    public void MutableArray_WithReadonlyElement_SlotWrite_Accepted()
    {
        // `[{ n: 1 } as const]` is a MUTABLE array of a readonly-element type — the slot is writable,
        // so the element-only readonly must not trigger TS2542 (the fix keys off the array's own flag).
        TestHarness.RunInterpreted("const arr = [{ n: 1 } as const]; arr[0] = arr[0];");
    }
}
