using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Tests for narrowing that flows across property assignments (issue #48).
/// TypeScript's CFA narrows a slot to the RHS type after a write, so
/// `obj.x = "s"` followed by `obj.x.length` type-checks even when `obj.x`
/// is declared `string | null`. These tests lock in that behavior.
/// Companion to <see cref="AssignmentInvalidationTests"/>.
/// </summary>
public class AssignmentFlowNarrowingTests
{
    [Fact]
    public void InterfaceProperty_WriteThenRead_NarrowsToRhsType()
    {
        // Primary reproduction from issue #48.
        var source = """
            interface O { x: string | null }
            function f(o: O): number {
                o.x = "hello";
                return o.x.length;
            }
            console.log(f({ x: null }));
            """;

        Assert.Equal("5\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void RecordProperty_WriteThenRead_NarrowsToRhsType()
    {
        var source = """
            type O = { x: string | null };
            function f(o: O): number {
                o.x = "hello";
                return o.x.length;
            }
            console.log(f({ x: null }));
            """;

        Assert.Equal("5\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void UnionRhs_NarrowsToMatchingDeclaredMembers()
    {
        // Writing a union to a wider union narrows to the intersection of members.
        // Here `string | number` written to `string | number | null` excludes null,
        // so the subsequent read is assignable to a `string | number` return slot.
        var source = """
            type O = { x: string | number | null };
            function f(o: O, cond: boolean): string | number {
                o.x = cond ? "hi" : 42;
                return o.x;
            }
            console.log(f({ x: null }, true));
            console.log(f({ x: null }, false));
            """;

        Assert.Equal("hi\n42\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void DeclaredNonUnion_NoOp()
    {
        // When the declared type isn't a union, there's no narrowing to install.
        // Should still typecheck and run without issue.
        var source = """
            type O = { x: string };
            function f(o: O): number {
                o.x = "hello";
                return o.x.length;
            }
            console.log(f({ x: "world" }));
            """;

        Assert.Equal("5\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void GuardedThenBranch_WriteNarrows()
    {
        // Inside a guarded branch (which creates its own narrowing scope in SharpTS),
        // a write to a union-typed property narrows subsequent reads in the same branch.
        var source = """
            interface O { x: string | null }
            function f(o: O): number {
                if (o.x === null) {
                    o.x = "hi";
                    return o.x.length;
                }
                return o.x.length;
            }
            console.log(f({ x: null }));
            console.log(f({ x: "ok" }));
            """;

        Assert.Equal("2\n2\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void OpaqueCall_InvalidatesPostWriteNarrowing()
    {
        // A function call between the write and the read could mutate o.x,
        // so InvalidatePropertiesOf should kill our narrowing.
        var source = """
            interface O { x: string | null }
            function mutate(o: O): void { o.x = null; }
            function f(o: O): number {
                o.x = "hello";
                mutate(o);
                return o.x.length;
            }
            """;

        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void AliasedObject_WriteThroughAlias_NarrowsOriginalToo()
    {
        // `const b = a; b.x = "s"` should narrow `a.x` to `string` because
        // _variableAliases tracks that b and a refer to the same object.
        var source = """
            interface O { x: string | null }
            function f(a: O): number {
                const b = a;
                b.x = "hello";
                return a.x.length;
            }
            console.log(f({ x: null }));
            """;

        Assert.Equal("5\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void LoopBody_NarrowingInvalidatedAtIterationBoundary()
    {
        // Narrowing installed by an in-loop write must not be assumed at the
        // top of the next iteration. LoopAssignmentAnalyzer handles this.
        var source = """
            interface O { x: string | null }
            function f(o: O): void {
                for (let i = 0; i < 3; i++) {
                    console.log(o.x!.length);
                    o.x = "hi";
                }
            }
            f({ x: "ok" });
            """;

        // Without the `!`, `o.x` at the top of iteration 2+ could be anything
        // compatible with `string | null` because LoopAssignmentAnalyzer
        // invalidates narrowings assigned in the loop body across iterations.
        // With `!`, this runs.
        Assert.Equal("2\n2\n2\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ClassSetter_WriteNarrowsSubsequentReads()
    {
        // Class instance setter writes narrow subsequent reads.
        var source = """
            class Box {
                private _value: string | null = null;
                set value(v: string | null) { this._value = v; }
                get value(): string | null { return this._value; }
            }
            function f(b: Box): string | null {
                b.value = "hello";
                return b.value;
            }
            console.log(f(new Box()));
            """;

        Assert.Equal("hello\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void WriteOfNullableRhs_DoesNotNarrow()
    {
        // If the RHS is itself the full declared type, there's no narrowing
        // benefit — reads should still error on the unguarded access.
        var source = """
            interface O { x: string | null }
            function getNullable(): string | null { return null; }
            function f(o: O): number {
                o.x = getNullable();
                return o.x.length;
            }
            """;

        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void NullWrite_NarrowsToNull()
    {
        // Writing `null` to a `string | null` slot narrows to `null`. Assigning
        // the narrowed read to a non-nullable `string` slot should fail with
        // a 'null'-specific message (not the pre-fix 'string | null').
        var source = """
            interface O { x: string | null }
            function f(o: O): string {
                o.x = null;
                return o.x;
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("null", ex.Message);
        Assert.DoesNotContain("string | null", ex.Message);
    }

    [Fact]
    public void WriteThenMethodCallOnSameProperty_KeepsNarrowing()
    {
        // Issue #56: a method call on the just-narrowed property used to invalidate
        // its own narrowing because InvalidatePropertiesOf treated the receiver as
        // one of "its own properties". `obj.search.substring(...)` is the call —
        // the receiver is `obj.search`, and only properties strictly deeper than
        // it (e.g. `obj.search.foo`) should be invalidated.
        var source = """
            interface R { search: string | null }
            function f(o: R): number {
                o.search = "hello";
                return o.search.substring(1).length;
            }
            console.log(f({ search: null }));
            """;

        Assert.Equal("4\n", TestHarness.RunInterpreted(source));
    }
}
