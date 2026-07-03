using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// End-state guards for the type-AST migration (docs/plans/type-ast-design.md, slice 6): the
/// char-scanning string parser is deleted, every annotation consumer resolves node-first, and
/// <c>ToTypeInfo(string)</c> survives only as parse-to-node + convert (ResolveAnnotation's
/// defensive fallback and the embedding surface). These tests pin that the fallback is DEAD in
/// practice and that the fragment parser preserves the old string entry's semantics.
/// </summary>
public class TypeNodeEndStateTests
{
    [Fact]
    public void EndState_NoStringFallbacks_AcrossConstructCorpus()
    {
        // One program touching every construct family the scanner used to own. If ANY
        // annotation consumer regresses to the string fallback (or a parser change stops
        // producing a node), the fallback counter trips and this fails — the invariant that
        // justified deleting the scanner.
        TypeNodeStats.Reset();
        TestHarness.RunInterpreted("""
            namespace N { export type Id = number; }
            interface P { id: number; m(x: string): boolean; }
            interface Sigs { (x: number): string; new (x: number): P; [k: string]: unknown; }
            type Pair<A, B> = [A, B];
            type Tree<T> = { value: T; children: Tree<T>[] };
            type RO<T> = { readonly [K in keyof T]: T[K] };
            type Renamed<T> = { [K in keyof T as `get_${string & K}`]: T[K] };
            type Elem<T> = T extends (infer U)[] ? U : never;
            type StrElem<T> = T extends Array<infer U extends string> ? U : never;
            type IsNum<T> = T extends number ? "yes" : "no";
            type Dir = "left" | "right";

            var union: string | number = 1;
            var inter: { a: number } & { b: string } = { a: 1, b: "x" };
            var arr: readonly number[] = [1];
            var tup: [first: string, rest: number] = ["x", 1];
            var tup2: [string, number?] = ["x"];
            var tup3: [string, ...boolean[]] = ["x", true];
            var obj: { name: string; greet?(x: number): string } = { name: "x" };
            var key: keyof P = "id";
            var idx: P["id"] = 1;
            var chain: { a: { b: number } }["a"]["b"] = 1;
            var fn: (this: P, x: number, y?: string) => void;
            var gfn: <T extends object, K extends keyof T>(o: T, k: K) => T[K];
            var ctor: new (this: P, id: number) => P;
            var gctor: new <T>(this: object, x: T) => T[];
            var tmpl: `padding-${Dir}` = "padding-left";
            var qual: N.Id = 5;
            var pred: (x: unknown) => x is string;
            var assertFn: (x: unknown) => asserts x;
            var boxed: Pair<string, number> = ["x", 1];
            var tree: Tree<number> = { value: 1, children: [] };
            var ro: RO<{ a: number }> = { a: 1 };
            var cond: IsNum<number> = "yes";
            var elem: Elem<number[]> = 1;
            var big: 1n | 2n;
            var partial: Partial<{ a: number; b: string }> = { a: 1 };
            const sym: unique symbol = Symbol();
            var q: typeof qual = 5;
            function f<T>(x: T, e: Elem<T[]>): IsNum<T> { return null as any; }
            """);
        Assert.Equal(0, TypeNodeStats.StringFallbacks);
        Assert.True(TypeNodeStats.NodeHits > 20,
            $"expected the corpus to resolve through the node path, got {TypeNodeStats.NodeHits} hits");
    }

    [Fact]
    public void TryParseTypeFragment_ParsesCompleteTypes()
    {
        Assert.IsType<UnionTypeNode>(Parser.TryParseTypeFragment("string | number"));
        Assert.IsType<ObjectTypeNode>(Parser.TryParseTypeFragment("{ a: number }"));
        Assert.IsType<TemplateLiteralTypeNode>(Parser.TryParseTypeFragment("`a${string}`"));
        Assert.IsType<FunctionTypeNode>(Parser.TryParseTypeFragment("(x: number) => void"));
        var lit = Assert.IsType<LiteralTypeNode>(Parser.TryParseTypeFragment("true"));
        Assert.Equal(true, lit.Value);
        var named = Assert.IsType<NamedTypeNode>(Parser.TryParseTypeFragment("Foo.Bar"));
        Assert.Equal("Foo.Bar", named.Name);
    }

    [Fact]
    public void TryParseTypeFragment_RejectsGarbageAndPartialParses()
    {
        // Lex/parse failures and trailing tokens yield null — the checker's ToTypeInfo(string)
        // then resolves to `any`, the same verdict the retired scanner's unknown tail produced.
        Assert.Null(Parser.TryParseTypeFragment("%%%"));
        Assert.Null(Parser.TryParseTypeFragment(""));
        Assert.Null(Parser.TryParseTypeFragment("number garbage"));
        Assert.Null(Parser.TryParseTypeFragment("{ a: number"));
    }
}
