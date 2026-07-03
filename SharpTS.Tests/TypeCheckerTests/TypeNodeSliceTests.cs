using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Type-AST migration slice (docs/plans/type-ast-design.md): variable annotations resolve
/// node-first for named/literal/array/union constructs, with string fallback elsewhere. These
/// tests pin (a) that the node path actually engages, and (b) that node-resolved types behave
/// identically to string-resolved ones.
/// </summary>
public class TypeNodeSliceTests
{
    [Fact]
    public void NodePath_EngagesForSupportedAnnotations()
    {
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            var a: number = 1;
            var b: string[] = ["x"];
            var c: "on" | "off" = "on";
            var d: number | string = 1;
            """);
        Assert.True(TypeNodeStats.NodeHits >= 4,
            $"expected the node path for all four annotations, got {TypeNodeStats.NodeHits}");
    }

    [Fact]
    public void NodePath_EngagesForBigintLiteralType()
    {
        // Bigint literal types (1n) now carry a node. Resolution keeps parity with the string
        // path, which had no bigint handling and let "1n" fall through its unknown-name tail to
        // any — so the annotation resolves (node-first) without constraining the value.
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            let x: 1n;
            let y: 1n | 2n;
            """);
        Assert.True(TypeNodeStats.NodeHits >= 2,
            $"expected the bigint-literal-type annotations on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodePath_EngagesForConstructorTypeWithThisParam()
    {
        // Constructor types with a `this:` pseudo-parameter now carry nodes (plain and generic).
        // The this-type resolves but is dropped from the construct signature, exactly like the
        // string path's ConvertConstructSignatures.
        // Plain field (not a constructor parameter property) — parameter-property fields are a
        // separate consumer site not yet node-wired.
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            class Widget { id!: number; }
            let make: new (this: Widget, id: number) => Widget;
            let gmake: new <T>(this: object, x: T) => T[];
            """);
        Assert.True(TypeNodeStats.NodeHits >= 2,
            $"expected the this-param constructor-type annotations on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_ConstructorTypeWithThisParamStillEnforcesShape()
    {
        // Dropping the this-type must not loosen the signature: the parameter list still binds.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var ctor: new (this: object, x: number) => object = 42;
            """));
    }

    [Fact]
    public void NodeResolved_UniqueSymbolMatchesStringPath()
    {
        // Valid form: const initialized with Symbol() — special-cased BEFORE resolution on both
        // paths, so nothing resolves (and nothing falls back).
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            const s: unique symbol = Symbol();
            """);
        Assert.Equal(0, TypeNodeStats.StringFallbacks);

        // Any other position reaches resolution and throws the same TS1331 as the string path.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            let x: unique symbol;
            """));
    }

    [Fact]
    public void NodePath_EngagesForMappedAlias()
    {
        // Mapped-type alias bodies now carry nodes, so the reference expands node-first.
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            type RO<T> = { readonly [K in keyof T]: T[K] };
            type Partialize<T> = { [K in keyof T]?: T[K] };
            var r: RO<{ a: number }> = { a: 1 };
            var p: Partialize<{ a: number; b: string }> = { a: 1 };
            """);
        Assert.True(TypeNodeStats.NodeHits >= 2,
            $"expected the mapped-alias annotations on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_MappedTypePreservesValueType()
    {
        // RO<{ a: number }> maps to { readonly a: number }; a string value must be rejected.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type RO<T> = { readonly [K in keyof T]: T[K] };
            var r: RO<{ a: number }> = { a: "x" };
            """));
    }

    [Fact]
    public void NodeResolved_MappedTypeOptionalModifierMatchesStringPath()
    {
        // Partialize makes every member optional, so omitting one is allowed.
        TestHarness.RunInterpreted("""
            type Partialize<T> = { [K in keyof T]?: T[K] };
            var p: Partialize<{ a: number; b: string }> = { a: 1 };
            """);
    }

    [Fact]
    public void NodePath_EngagesForGenericAliasReferences()
    {
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            type Box<T> = { value: T };
            type PairOf<A, B> = [A, B];
            type Handler<T> = (item: T) => void;
            var b: Box<number> = { value: 1 };
            var p: PairOf<string, number> = ["x", 1];
            var h: Handler<string> = (s: string) => {};
            """);
        Assert.True(TypeNodeStats.NodeHits >= 3,
            $"expected the node path for all three alias annotations, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_GenericAliasEnforcesSubstitutedArguments()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type Box<T> = { value: T };
            var b: Box<number> = { value: "no" };
            """));
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type Handler<T> = (item: T) => void;
            var h: Handler<string> = (n: number) => {};
            """));
        // Wrong arity carries the string path's TS2314.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type Box<T> = { value: T };
            var b: Box<number, string> = { value: 1 };
            """));
    }

    [Fact]
    public void NodeResolved_RecursiveAliasStillConverges()
    {
        // Self-referential alias: the recursion placeholder must fire on the node path the
        // same way it does on the string path (no stack overflow, no spurious error).
        TestHarness.RunInterpreted("""
            type Tree<T> = { value: T; children: Tree<T>[] };
            var t: Tree<number> = { value: 1, children: [{ value: 2, children: [] }] };
            """);
    }

    [Fact]
    public void NodePath_EngagesForGenericReferences()
    {
        TypeNodeStats.Reset();
        // Plain fields (not constructor parameter properties) so this stays focused on the generic
        // references — parameter-property fields are a separate consumer site not yet node-wired.
        TestHarness.RunInterpreted("""
            class Pair<A, B> { first!: A; second!: B; constructor(a: A, b: B) {} }
            var xs: Array<number> = [1, 2];
            var p: Promise<string> = Promise.resolve("x");
            var pr: Pair<string, number> = new Pair<string, number>("x", 1);
            var part: Partial<{ a: number; b: string }> = { a: 1 };
            """);
        Assert.True(TypeNodeStats.NodeHits >= 4,
            $"expected the node path for all four generic annotations, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_GenericReferencesEnforceArguments()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var xs: Array<number> = ["not a number"];
            """));
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            class Pair<A, B> { constructor(public first: A, public second: B) {} }
            var p: Pair<string, number> = new Pair<number, string>(1, "x");
            """));
        // Utility-type expansion through the node path: Partial makes members optional,
        // but still rejects mistyped ones.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var part: Partial<{ a: number }> = { a: "no" };
            """));
    }

    [Fact]
    public void NodePath_EngagesForObjectAndTupleTypes()
    {
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            var o: { name: string; age?: number } = { name: "x" };
            var m: { greet(x: number): string } = { greet: (x: number) => "hi" };
            var ix: { [k: string]: number } = { a: 1 };
            var t: [string, number?] = ["x"];
            var nt: [first: string, rest: number] = ["x", 1];
            """);
        Assert.True(TypeNodeStats.NodeHits >= 5,
            $"expected the node path for all five annotations, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_ObjectTypeEnforcesMembers()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var o: { name: string } = { name: 1 };
            """));
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var o: { name: string } = {};
            """));
        // Optional members may be absent but not mistyped.
        TestHarness.RunInterpreted("""
            var o: { name: string; age?: number } = { name: "x" };
            """);
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var o: { name: string; age?: number } = { name: "x", age: "old" };
            """));
    }

    [Fact]
    public void NodeResolved_TupleEnforcesArityAndElementTypes()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var t: [string, number] = ["x"];
            """));
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var t: [string, number] = [1, "x"];
            """));
        TestHarness.RunInterpreted("""
            var t: [string, ...number[]] = ["x", 1, 2, 3];
            """);
    }

    [Fact]
    public void NodeResolved_IndexSignatureEnforcesValueType()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var ix: { [k: string]: number } = { a: "not a number" };
            """));
    }

    [Fact]
    public void NodePath_EngagesForFunctionTypes()
    {
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            var f: (x: number) => string = (x) => "s";
            var g: () => void = () => {};
            var h: (x: number, y?: string, ...rest: boolean[]) => number = (x) => x;
            const k: (cb: (n: number) => void) => void = (cb) => cb(1);
            """);
        Assert.True(TypeNodeStats.NodeHits >= 4,
            $"expected the node path for all four function-type annotations, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_FunctionTypeEnforcesParamAndReturn()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var f: (x: number) => string = (x: string) => x;
            """));
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var f: () => string = () => 1;
            """));
    }

    [Fact]
    public void NodeResolved_FunctionTypeArityHonorsOptionalAndRest()
    {
        // A two-required-param target rejects a source requiring three; optional/rest params
        // must not count toward the node-built signature's required arity.
        TestHarness.RunInterpreted("""
            var f: (a: number, b: number, c?: number) => void = (a: number, b: number) => {};
            """);
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var g: (a: number) => void = (a: number, b: number, c: number) => {};
            """));
    }

    [Fact]
    public void NodeResolved_ConstructorTypeIsConstructable()
    {
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            class Widget { constructor(public id: number) {} }
            var make: new (id: number) => Widget = Widget;
            const w: Widget = new make(1);
            """);
        Assert.True(TypeNodeStats.NodeHits >= 1,
            $"expected the constructor-type annotation on the node path, got {TypeNodeStats.NodeHits}");
    }

    [Fact]
    public void NodeResolved_UnionEnforcesMembers()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var c: "on" | "off" = "neither";
            """));
    }

    [Fact]
    public void NodeResolved_ArrayEnforcesElementType()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var b: string[] = [1];
            """));
    }

    [Fact]
    public void NodeResolved_NamedTypesResolveInScope()
    {
        // Type parameters and class names must resolve identically to the string path —
        // the node path delegates bare names to the same single-name resolution.
        TestHarness.RunInterpreted("""
            class Base { foo: string; }
            function f<T>(x: T) {
                var y: T = x;
                var z: Base = new Base();
                var u: Base | null = null;
            }
            """);
    }

    [Fact]
    public void NodeResolved_UnionNormalizesAnyAndNever()
    {
        TestHarness.RunInterpreted("""
            var a: any | never = null;
            """);
    }

    [Fact]
    public void NodePath_EngagesForIntersectionKeyofIndexedAndTypeof()
    {
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            type A = { a: number };
            type B = { b: string };
            var ab: A & B = { a: 1, b: "x" };
            var k: keyof A = "a";
            var v: A["a"] = 1;
            const origin = { n: 5 };
            var t: typeof origin = origin;
            """);
        Assert.True(TypeNodeStats.NodeHits >= 4,
            $"expected the node path for intersection/keyof/indexed/typeof, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_IntersectionMergesMembers()
    {
        // Both members' properties are required in the merged type.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type A = { a: number };
            type B = { b: string };
            var ab: A & B = { a: 1 };
            """));
    }

    [Fact]
    public void NodeResolved_KeyofEnforcesKeys()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type A = { a: number; b: string };
            var k: keyof A = "c";
            """));
    }

    [Fact]
    public void NodeResolved_IndexedAccessEnforcesValueType()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type A = { a: number };
            var v: A["a"] = "not a number";
            """));
    }

    [Fact]
    public void NodePath_EngagesForConditionalAliasWithInfer()
    {
        // The alias definition is now a ConditionalTypeNode carrying an InferTypeNode, so the
        // reference expands through the node path (no string fallback).
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            type Elem<T> = T extends (infer U)[] ? U : never;
            var e: Elem<number[]> = 1;
            """);
        Assert.True(TypeNodeStats.NodeHits >= 1,
            $"expected the conditional/infer alias on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_ConditionalEvaluatesToSelectedBranch()
    {
        // IsNum<number> resolves to the true branch ("yes"), so "no" must be rejected — the same
        // verdict the string path produces (EvaluateConditionalType is path-independent).
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type IsNum<T> = T extends number ? "yes" : "no";
            var r: IsNum<number> = "no";
            """));
        // The false branch is selected for a non-number argument.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type IsNum<T> = T extends number ? "yes" : "no";
            var r: IsNum<string> = "yes";
            """));
    }

    [Fact]
    public void NodePath_EngagesForGenericFunctionType()
    {
        // Declaration-only so the (pre-existing, path-independent) generic-arrow assignment
        // limitation doesn't mask which path resolved the annotation.
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            let id: <T>(x: T) => T;
            let pick: <T extends object, K extends keyof T>(o: T, k: K) => T[K];
            """);
        Assert.True(TypeNodeStats.NodeHits >= 2,
            $"expected the generic-function-type annotations on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_GenericFunctionTypeResolvesWithoutError()
    {
        // A generic function type whose body references its own type parameters (keyof T, T[K])
        // must resolve cleanly — the parameters resolve in the signature's fresh scope, not to any.
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            let get: <T, K extends keyof T>(o: T, k: K) => T[K];
            let map: <T, U>(items: T[], f: (x: T) => U) => U[];
            """);
        Assert.True(TypeNodeStats.NodeHits >= 2,
            $"expected both generic-function annotations on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodePath_EngagesForTemplateLiteralType()
    {
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            type Dir = "left" | "right";
            var s: `padding-${Dir}` = "padding-left";
            var f: `f-${string}` = "f-anything";
            """);
        Assert.True(TypeNodeStats.NodeHits >= 2,
            $"expected the template-literal annotations on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodePath_EngagesForClassFieldAnnotations()
    {
        // Class field type annotations now resolve node-first (consumer wired off the string path).
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            class C {
                a: number = 1;
                b: string[] = [];
                c: { x: number } = { x: 0 };
            }
            """);
        Assert.True(TypeNodeStats.NodeHits >= 3,
            $"expected the class-field annotations on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_ClassFieldEnforcesAnnotatedType()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            class C { a: number = "not a number"; }
            """));
    }

    [Fact]
    public void NodeResolved_TemplateLiteralExpandsConcreteUnion()
    {
        // `padding-${Dir}` expands to "padding-left" | "padding-right"; another value is rejected.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type Dir = "left" | "right";
            var s: `padding-${Dir}` = "padding-up";
            """));
    }

    [Fact]
    public void NodePath_EngagesForReadonlyAndQualifiedAndPredicate()
    {
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            namespace N { export type Id = number; }
            var ro: readonly number[] = [1, 2];
            var rt: readonly [number, string] = [1, "x"];
            var q: N.Id = 5;
            var pred: (x: unknown) => x is string;
            """);
        Assert.True(TypeNodeStats.NodeHits >= 4,
            $"expected readonly/qualified/predicate annotations on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_ReadonlyArrayStillEnforcesElementType()
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var a: readonly number[] = ["x"];
            """));
    }

    [Fact]
    public void NodeResolved_QualifiedNameMatchesStringPath()
    {
        // The dotted name is handed to the same single-name resolution as the string path; the
        // checker resolves namespace type-alias exports permissively (to any), so this assigns
        // cleanly on BOTH paths — the node path introduces no divergence.
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            namespace N { export type Id = number; }
            var q: N.Id = 5;
            """);
        Assert.True(TypeNodeStats.NodeHits >= 1,
            $"expected the qualified-name annotation on the node path, got {TypeNodeStats.NodeHits}");
    }

    [Fact]
    public void NodePath_EngagesForGenericConstructorType()
    {
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            let make: new <T>(x: T) => T[];
            let build: new <T extends object, K extends keyof T>(o: T, k: K) => T[K];
            """);
        Assert.True(TypeNodeStats.NodeHits >= 2,
            $"expected the generic-constructor-type annotations on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodePath_EngagesForObjectTypeWithGenericSignatures()
    {
        // Overloaded generic call signatures and a generic construct signature inside object types.
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            let overloads: { <T>(x: T): T; <U, V>(a: U, b: V): U };
            let factory: { new <T>(x: T): T[] };
            """);
        Assert.True(TypeNodeStats.NodeHits >= 2,
            $"expected the object-type generic-signature annotations on the node path, got {TypeNodeStats.NodeHits}");
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }

    [Fact]
    public void NodeResolved_GenericConstructorTypeIsConstructable()
    {
        // A generic constructor type resolves to a constructable object type; a non-constructable
        // value must be rejected — the same verdict the string path produces.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            var ctor: new <T>(x: T) => T[] = 42;
            """));
    }

    [Fact]
    public void NodePath_EngagesForConstrainedInferAlias()
    {
        // `infer U extends string` now carries a node, so the conditional alias expands node-first.
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            type StrElem<T> = T extends Array<infer U extends string> ? U : never;
            var e: StrElem<string[]> = "x";
            """);
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
    }
}
