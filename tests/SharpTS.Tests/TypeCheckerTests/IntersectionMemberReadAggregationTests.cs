using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class IntersectionMemberReadAggregationTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("""
                class Box { value: number = 1; }
                declare let box: Box & { value: string };
                const dot: never = box.value;
                const bracket: never = box['value'];
                """, []),
            ("""
                class Box { value: number = 1; }
                declare let box: { value: string } & Box;
                const dot: never = box.value;
                const bracket: never = box['value'];
                """, []),
            ("""
                class Box { value: number = 1; }
                function read<T extends Box & { value: string }>(box: T): never { return box.value; }
                function indexed<T extends Box & { value: string }>(box: T): never { return box['value']; }
                """, []),
            ("""
                class Box { value: number = 1; }
                function read<T>(box: T & Box & { value: string }): never { return box.value; }
                """, []),
            ("""
                class Box { first = 1; second = 's'; 0 = 1; }
                declare let box: Box & { first: string; second: number; 0: string };
                declare let key: 'first' | 'second';
                const union: never = box[key];
                const numeric: never = box[0];
                """, []),
            ("""
                class Box { get value(): number { return 1; } set value(next: number | string) {} }
                declare let box: Box & { value: string };
                const dot: never = box.value;
                const bracket: never = box['value'];
                """, []),
            ("""
                class Base<T> { value!: T; }
                class Box extends Base<number> {}
                declare let box: Box & { value: string };
                const value: never = box.value;
                """, []),
            ("""
                class Box { value?: number; }
                declare let box: Box & { value: number };
                const value: number = box.value + 1;
                const indexed: number = box['value'] + 1;
                """, []),
            ("""
                class Box { value: number | string = 1; }
                declare let box: Box & { value: number };
                const value: number = box.value + 1;
                """, []),
            ("""
                class Box { value: number = 1; }
                declare let box: Box & { value: 1 };
                const value: 1 = box.value;
                """, []),
            ("""
                class Box { read(input: number): number { return input; } }
                declare let box: Box & { read(input: string): string };
                const numeric: number = box.read(1);
                const text: string = box.read('ok');
                """, []),
            ("""
                class Box { read<T>(input: T): T { return input; } }
                declare let box: Box & { read(input: number): number };
                const numeric: number = box.read(1);
                const text: string = box.read('ok');
                """, []),
            ("""
                class Box { read(input: number): number { return input; } }
                declare let box: Box & { read?: (input: string) => string };
                const numeric: number = box.read(1);
                const text: string = box.read('ok');
                """, []),
            ("""
                class Box { read?: (input: number) => number; }
                declare let box: Box & { read?: (input: string) => string };
                box.read(1);
                """, ["TS2722"]),
            ("""
                class Box { read(input: number): number { return input; } }
                declare let box: Box & { read: unknown };
                const numeric: number = box.read(1);
                box.read('wrong');
                """, ["TS2345"]),
            ("""
                class Box { value: number = 1; }
                function read<T extends Record<string, string>>(box: T & Box): number { return box.value; }
                function indexed<T extends Record<string, string>>(box: T & Box): number { return box['value']; }
                """, []),
            ("""
                function read<T>(box: T & { value: number }): number { return box.value; }
                function indexed<T>(box: T & { value: number }): number { return box['value']; }
                """, []),
            ("""
                class Box { private value = 1; }
                declare let box: Box & { other: number };
                const value: number = box.value;
                """, ["TS2341"]),
            ("""
                class Box { protected value = 1; }
                declare let box: Box & { other: number };
                const value: number = box.value;
                """, ["TS2445"]),
            ("""
                class Base { protected value = 1; }
                class Derived extends Base { read<T>(box: T & Base & Derived): number { return box.value; } }
                """, []),
            ("""
                class Box { private value = 1; }
                declare let box: Box & { other: number };
                const value: number = box['value'];
                """, []),
            ("""
                class Box { private value = 1; other = 1; }
                declare let box: Box & { value: number };
                const value = box.other;
                """, ["TS2339"]),
            ("""
                class Box { private value = 1; other = 1; }
                declare let box: Box & { value: number };
                const value: never = box['other'];
                """, []),
            ("""
                class Box { values = 1; }
                declare let box: Box & { value: number };
                const value: number = box.value;
                const invalid: string = box.value;
                """, ["TS2322"])
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void IntersectionReadsCombineNamedSlotsAndPreserveAccessAndCallableSemantics(string source, string[] codes, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(d => d.TsCode));
        else
        {
            string path = Path.Combine(Path.GetTempPath(), "intersection-read-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
        }
    }

    [Fact]
    public void CallableAggregationRetainsOriginalSignaturesWithoutInventingStructuralSourceIdentity()
    {
        const string source = """
            class Box { read(input: number): number { return input; } }
            declare let box: Box & { read(input: string): string };
            const value = box.read(1);
            """;
        var document = new SourceDocument("intersection-read-facts.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata().WithMemberProvenance();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.Empty(result.Diagnostics);
        var call = Assert.IsType<Expr.Call>(Assert.Single(document.EditorSyntax!.Invocations).Owner);
        var member = Assert.IsType<Expr.Get>(call.Callee);
        var overload = Assert.IsType<TypeInfo.OverloadedFunction>(result.TypeMap.Get(member));
        var declared = result.TypeMap.GetClassType(Assert.IsType<Stmt.Class>(statements[0]))!.Methods["read"];
        Assert.Contains(overload.Signatures, signature => ReferenceEquals(signature, declared));
        var fact = Assert.IsType<EditorInvocationFact>(checker.EditorFacts.Freeze().GetInvocation(document, call));
        Assert.Equal(2, fact.Candidates.Count);
        Assert.All(fact.Candidates, candidate => Assert.True(candidate.Declared.IsAvailable));
        Assert.False(checker.Members.GetResolution(document, member.Name, MemberOperation.Read).IsComplete);
    }
}
