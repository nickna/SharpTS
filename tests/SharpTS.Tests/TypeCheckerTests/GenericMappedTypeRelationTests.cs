using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Relation rules for deferred mapped / key-filter types over an open type parameter
/// (issue #337, items 1 &amp; 2 — the conditionalTypes1 f7/f8 and DeepReadonly cluster):
/// <list type="bullet">
/// <item>the key-filter idiom <c>{ [K in keyof T]: T[K] extends Function ? K : never }[keyof T]</c>
/// resolves to a deferred conditional so FunctionPropertyNames / NonFunctionPropertyNames relate
/// structurally instead of collapsing to <c>any</c>;</item>
/// <item><c>Pick&lt;T, K&gt;</c> over a deferred key filter relates homomorphically to <c>T</c>;</item>
/// <item>a <c>readonly [P in K]</c> mapped type marks its members read-only and the key filter
/// drops <c>never</c> arms (so function-typed keys disappear);</item>
/// <item>an interface may extend <c>ReadonlyArray&lt;…&gt;</c>, yielding a read-only numeric index.</item>
/// </list>
/// </summary>
public class GenericMappedTypeRelationTests
{
    private const string KeyFilterPreamble = """
        type FunctionPropertyNames<T> = { [K in keyof T]: T[K] extends Function ? K : never }[keyof T];
        type NonFunctionPropertyNames<T> = { [K in keyof T]: T[K] extends Function ? never : K }[keyof T];
        type FunctionProperties<T> = Pick<T, FunctionPropertyNames<T>>;
        type NonFunctionProperties<T> = Pick<T, NonFunctionPropertyNames<T>>;
        """;

    private static void ExpectOk(string body)
    {
        var src = KeyFilterPreamble + "\n" + body + "\nconsole.log(\"ok\");\n";
        Assert.Equal("ok\n", TestHarness.RunInterpreted(src));
    }

    private static void ExpectError(string body)
    {
        var src = KeyFilterPreamble + "\n" + body + "\nconsole.log(\"ok\");\n";
        Assert.ThrowsAny<SharpTS.TypeSystem.Exceptions.TypeCheckException>(
            () => TestHarness.RunInterpreted(src));
    }

    // ---- f8: keyof T vs the key-filter name unions ----

    [Fact]
    public void KeyFilterNames_SubsetOfKeyof_AssignableToKeyof() =>
        ExpectOk("function f<T>(x: keyof T, y: FunctionPropertyNames<T>) { x = y; }");

    [Fact]
    public void Keyof_NotAssignableToKeyFilterName() =>
        ExpectError("function f<T>(x: keyof T, y: FunctionPropertyNames<T>) { y = x; }");

    [Fact]
    public void FunctionNames_NotAssignableToNonFunctionNames() =>
        ExpectError("function f<T>(y: FunctionPropertyNames<T>, z: NonFunctionPropertyNames<T>) { y = z; }");

    // ---- f7: T vs Pick over the key filter ----

    [Fact]
    public void TypeParam_AssignableToHomomorphicPick() =>
        ExpectOk("function f<T>(x: T, y: FunctionProperties<T>) { y = x; }");

    [Fact]
    public void HomomorphicPick_NotAssignableToTypeParam() =>
        ExpectError("function f<T>(x: T, y: FunctionProperties<T>) { x = y; }");

    [Fact]
    public void DifferentKeyFilterPicks_NotAssignable() =>
        ExpectError("function f<T>(y: FunctionProperties<T>, z: NonFunctionProperties<T>) { y = z; }");

    // ---- item 2: readonly mapped properties + function-key drop ----

    [Fact]
    public void DeepReadonlyObject_DropsFunctionProperties_AndIsReadonly()
    {
        // NonFunctionPropertyNames<Part> drops `updatePart`, so it is absent (TS2339); the
        // remaining properties are readonly (TS2540 on write).
        var src = """
            interface Part { id: number; name: string; updatePart(n: string): void; }
            type NonFunctionPropertyNames<T> = { [K in keyof T]: T[K] extends Function ? never : K }[keyof T];
            type DRO<T> = { readonly [P in NonFunctionPropertyNames<T>]: T[P]; };
            function f(p: DRO<Part>) { let n: string = p.name; p.updatePart("x"); }
            """;
        Assert.ThrowsAny<SharpTS.TypeSystem.Exceptions.TypeCheckException>(
            () => TestHarness.RunInterpreted(src));
    }

    [Fact]
    public void ReadonlyMappedProperty_RejectsAssignment()
    {
        var src = """
            type NonFunctionPropertyNames<T> = { [K in keyof T]: T[K] extends Function ? never : K }[keyof T];
            type DRO<T> = { readonly [P in NonFunctionPropertyNames<T>]: T[P]; };
            function f(p: DRO<{ id: number }>) { p.id = 5; }
            """;
        Assert.ThrowsAny<SharpTS.TypeSystem.Exceptions.TypeCheckException>(
            () => TestHarness.RunInterpreted(src));
    }

    // ---- item 2: interface extends ReadonlyArray / Array ----

    [Fact]
    public void InterfaceExtendsReadonlyArray_AcceptedAndIndexReadable()
    {
        var src = """
            interface RA extends ReadonlyArray<number> {}
            function f(r: RA) { let x: number = r[0]; }
            console.log("ok");
            """;
        Assert.Equal("ok\n", TestHarness.RunInterpreted(src));
    }

    [Fact]
    public void ReadonlyArrayIndex_RejectsWrite()
    {
        var src = """
            interface RA extends ReadonlyArray<number> {}
            function f(r: RA) { r[0] = 5; }
            """;
        Assert.ThrowsAny<SharpTS.TypeSystem.Exceptions.TypeCheckException>(
            () => TestHarness.RunInterpreted(src));
    }

    [Fact]
    public void MutableArrayIndex_AllowsWrite()
    {
        var src = """
            interface MA extends Array<number> {}
            function f(m: MA) { m[0] = 5; }
            console.log("ok");
            """;
        Assert.Equal("ok\n", TestHarness.RunInterpreted(src));
    }

    [Fact]
    public void GenericInterfaceExtendsReadonlyArray_IndexReadsElement_RejectsWrite()
    {
        // Exercises the instantiated-generic flatten path: RA<number> flattens to an interface
        // whose substituted numeric index signature is `number` and read-only.
        ExpectOk("interface RA<T> extends ReadonlyArray<T> {}\nfunction f(r: RA<number>) { let x: number = r[0]; }");
        Assert.ThrowsAny<SharpTS.TypeSystem.Exceptions.TypeCheckException>(() => TestHarness.RunInterpreted(
            "interface RA<T> extends ReadonlyArray<T> {}\nfunction f(r: RA<number>) { r[0] = 5; }\n"));
    }

    // ---- #365: recursive DeepReadonly over a self-referential interface ----
    //
    // `part.subparts` is the deferred alias `DeepReadonly<Part[]>`; indexing it resolves one level
    // to `DeepReadonlyArray<Part>` (read-only numeric index of the still-deferred `DeepReadonly<Part>`),
    // which only expands its members when one is touched. Exercises the conditional short-circuit,
    // the concrete `T[number]` simplification, the `extends` type-param scope fix, and lazy
    // member expansion on the indexed element.

    private const string DeepReadonlyPreamble = """
        interface Part { id: number; name: string; subparts: Part[]; updatePart(n: string): void; }
        type NonFunctionPropertyNames<T> = { [K in keyof T]: T[K] extends Function ? never : K }[keyof T];
        type DeepReadonly<T> =
            T extends any[] ? DeepReadonlyArray<T[number]> :
            T extends object ? DeepReadonlyObject<T> : T;
        interface DeepReadonlyArray<T> extends ReadonlyArray<DeepReadonly<T>> {}
        type DeepReadonlyObject<T> = { readonly [P in NonFunctionPropertyNames<T>]: DeepReadonly<T[P]>; };
        """;

    private static SharpTS.TypeSystem.Exceptions.TypeCheckException DeepReadonlyError(string body) =>
        Assert.ThrowsAny<SharpTS.TypeSystem.Exceptions.TypeCheckException>(
            () => TestHarness.RunInterpreted(DeepReadonlyPreamble + "\n" + body));

    [Fact]
    public void RecursiveDeepReadonly_IndexesArrayElement_ReadsMemberType()
    {
        // No TS7053 ("can't index") and the element member reads as `number`.
        Assert.Equal("ok\n", TestHarness.RunInterpreted(DeepReadonlyPreamble + "\n" +
            "function f10(part: DeepReadonly<Part>) { let id: number = part.subparts[0].id; }\n" +
            "console.log(\"ok\");\n"));
    }

    [Fact]
    public void RecursiveDeepReadonly_IndexedElementMember_HasPreciseType()
    {
        // The element member is `number`, not `any`: a string annotation is rejected (TS2322).
        Assert.Contains("not assignable",
            DeepReadonlyError("function f10(part: DeepReadonly<Part>) { let bad: string = part.subparts[0].id; }").Message);
    }

    [Fact]
    public void RecursiveDeepReadonly_WriteThroughReadonlyIndex_Rejected()
    {
        // `part.subparts[0] = …` writes through the read-only numeric index → TS2542.
        Assert.Contains("only permits reading",
            DeepReadonlyError("function f10(part: DeepReadonly<Part>) { part.subparts[0] = part.subparts[0]; }").Message);
    }

    [Fact]
    public void RecursiveDeepReadonly_WriteReadonlyElementMember_Rejected()
    {
        // `part.subparts[0].id = …` writes a read-only member of the deferred element → TS2540.
        Assert.Contains("read-only property",
            DeepReadonlyError("function f10(part: DeepReadonly<Part>) { part.subparts[0].id = part.subparts[0].id; }").Message);
    }

    [Fact]
    public void ConcreteIndexedAccess_OverArray_ResolvesElementType()
    {
        // `Part[][number]` over a concrete array simplifies to the element type (not `any`).
        const string preamble = "interface P { id: number }\ntype Elem = P[][number];\n";
        Assert.Equal("ok\n", TestHarness.RunInterpreted(
            preamble + "function f(e: Elem) { let n: number = e.id; }\nconsole.log(\"ok\");\n"));
        Assert.ThrowsAny<SharpTS.TypeSystem.Exceptions.TypeCheckException>(() => TestHarness.RunInterpreted(
            preamble + "function f(e: Elem) { let s: string = e.id; }\nconsole.log(\"ok\");\n"));
    }
}
