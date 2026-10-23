using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.Compilation;

[Collection("CompilationService")]
public sealed class ClassicGlobalLoopVarBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["for-of-var-head-classic-global-issue", """
            for (var item of [3, 4]) {
                console.log(item, (globalThis as any).item);
            }
            console.log(Object.hasOwn(globalThis, "item"), (globalThis as any).item);
            """, "3 3\n4 4\ntrue 4\n"];
        yield return ["for-in-var-head-classic-global-issue", """
            for (var key in { first: 1, last: 2 }) {
                console.log(key, (globalThis as any).key);
            }
            console.log(Object.hasOwn(globalThis, "key"), (globalThis as any).key);
            """, "first first\nlast last\ntrue last\n"];
        yield return ["empty-heads-publish-before-rhs-with-classic-var-descriptors", """
            function values(): any[] {
                console.log("rhs", Object.hasOwn(globalThis, "emptyItem"),
                    (globalThis as any).emptyItem === undefined, Object.hasOwn(globalThis, "emptyKey"));
                return [];
            }
            for (var emptyItem of values()) {}
            for (var emptyKey in {}) {}
            const itemDescriptor = Object.getOwnPropertyDescriptor(globalThis, "emptyItem")!;
            const keyDescriptor = Object.getOwnPropertyDescriptor(globalThis, "emptyKey")!;
            console.log(itemDescriptor.value === undefined, itemDescriptor.writable,
                itemDescriptor.enumerable, itemDescriptor.configurable);
            console.log(keyDescriptor.value === undefined, keyDescriptor.writable,
                keyDescriptor.enumerable, keyDescriptor.configurable);
            """, "rhs true true true\ntrue true true false\ntrue true true false\n"];
        yield return ["var-shared-closures-and-let-const-per-iteration-isolation", """
            const sharedReads: any[] = [], lexicalReads: any[] = [], constantReads: any[] = [];
            for (var shared of [1, 2]) { sharedReads.push(() => shared); }
            for (let lexical of [3, 4]) { lexicalReads.push(() => lexical); }
            for (const constant in { first: 1, last: 2 }) { constantReads.push(() => constant); }
            console.log(sharedReads[0](), sharedReads[1](), (globalThis as any).shared, Object.hasOwn(globalThis, "shared"));
            console.log(lexicalReads[0](), lexicalReads[1](), Object.hasOwn(globalThis, "lexical"));
            console.log(constantReads[0](), constantReads[1](), Object.hasOwn(globalThis, "constant"));
            """, "2 2 2 true\n3 4 false\nfirst last false\n"];
        yield return ["local-parameter-collisions-and-predeclared-assignment-heads", """
            function local(item: number, key: string) {
                const readItem = () => item, readKey = () => key, reads: any[] = [];
                for (var item of [7, 8]) {}
                for (var key in { first: 1, last: 2 }) { reads.push(() => key); }
                console.log(item, readItem(), key, readKey(), reads[0](), reads[1](),
                    Object.hasOwn(globalThis, "item"), Object.hasOwn(globalThis, "key"));
            }
            function empty(item: number, key: string) {
                for (var item of [] as number[]) {}
                for (var key in {}) {}
                console.log(item, key);
            }
            local(5, "initial"); empty(9, "seed");
            var selected: any = 1;
            for (selected of [3, 4]) { console.log(selected, (globalThis as any).selected); }
            var assigned: any = "before";
            for (assigned in { last: 1 }) { console.log(assigned, (globalThis as any).assigned); }
            console.log((globalThis as any).selected, (globalThis as any).assigned);
            """, "8 8 last last last last false false\n9 seed\n3 3\n4 4\nlast last\n4 last\n"];
        yield return ["namespace-and-class-static-block-var-heads-stay-local", """
            namespace Scope {
                for (var item of [1, 2]) { console.log("namespace", item, Object.hasOwn(globalThis, "item")); }
            }
            class Declaration {
                static {
                    for (var item of [3, 4]) { console.log("declaration", item, Object.hasOwn(globalThis, "item")); }
                }
            }
            const Expression = class {
                static {
                    for (var item of [5, 6]) { console.log("expression", item, Object.hasOwn(globalThis, "item")); }
                }
            };
            """, "namespace 1 false\nnamespace 2 false\ndeclaration 3 false\ndeclaration 4 false\nexpression 5 false\nexpression 6 false\n"];
        yield return ["static-block-loop-var-owner-preserves-lexical-tdz", """
            const value = 99;
            class Declaration {
                static {
                    for (var item of [] as any[]) {}
                    const read = () => value;
                    try { read(); }
                    catch (error) { console.log("declaration", error instanceof ReferenceError); }
                    let value = 7;
                    console.log("declaration", read(), Object.hasOwn(globalThis, "item"));
                }
            }
            const Expression = class {
                static {
                    for (var key in {}) {}
                    const read = () => value;
                    try { read(); }
                    catch (error) { console.log("expression", error instanceof ReferenceError); }
                    let value = 8;
                    console.log("expression", read(), Object.hasOwn(globalThis, "key"));
                }
            };
            console.log("outer", value);
            """, "declaration true\ndeclaration 7 false\nexpression true\nexpression 8 false\nouter 99\n"];
    }

    public static IEnumerable<object[]> ModuleCases()
    {
        yield return ["module-var-head-slots-closures-and-empty-loop-nonpublication", """
            const itemReads: any[] = [], keyReads: any[] = [];
            const readItem = () => moduleItem, readKey = () => moduleKey;
            const readEmptyItem = () => moduleEmptyItem, readEmptyKey = () => moduleEmptyKey;
            function values(): any[] { console.log(readEmptyItem() === undefined, readEmptyKey() === undefined); return []; }
            for (var moduleItem of [3, 4]) { itemReads.push(() => moduleItem); }
            for (var moduleKey in { first: 1, last: 2 }) { keyReads.push(() => moduleKey); }
            for (var moduleEmptyItem of values()) {}
            for (var moduleEmptyKey in {}) {}
            console.log(readItem(), readKey(), itemReads[0](), itemReads[1](), keyReads[0](), keyReads[1](),
                readEmptyItem() === undefined, readEmptyKey() === undefined);
            console.log(Object.hasOwn(globalThis, "moduleItem"), Object.hasOwn(globalThis, "moduleKey"),
                Object.hasOwn(globalThis, "moduleEmptyItem"), Object.hasOwn(globalThis, "moduleEmptyKey"));
            export {};
            """, "true true\n4 last 4 4 last last true true\nfalse false false false\n"];
        yield return ["async-var-for-of-and-for-await-share-parameter-slots", """
            async function run(value: number) {
                const read = () => value, reads: any[] = [], asyncReads: any[] = [];
                for (var value of [3, 4]) { reads.push(() => value); }
                await Promise.resolve(0);
                console.log(read(), reads[0](), reads[1](), Object.hasOwn(globalThis, "value"));
                for await (var value of [Promise.resolve(7), Promise.resolve(8)]) { asyncReads.push(() => value); }
                console.log(read(), reads[0](), asyncReads[0](), asyncReads[1](), Object.hasOwn(globalThis, "value"));
            }
            async function empty(value: number) {
                const read = () => value;
                for (var value of [] as number[]) {}
                for await (var value of [] as number[]) {}
                console.log(read(), value, Object.hasOwn(globalThis, "value"));
            }
            run(1).then(() => empty(9)).catch((error: any) => console.log("unexpected", error && error.name));
            export {};
            """, "4 4 4 false\n8 8 8 8 false\n9 9 false\n"];
        yield return ["checked-module-loop-owner-companion", """
            const itemReads: any[] = [], keyReads: any[] = [];
            for (var moduleItem of [3, 4]) { itemReads.push(() => moduleItem); }
            for (var moduleKey in { first: 1, last: 2 }) { keyReads.push(() => moduleKey); }
            for (var moduleEmptyItem of [] as any[]) {}
            for (var moduleEmptyKey in {}) {}
            console.log(itemReads[0](), itemReads[1](), keyReads[0](), keyReads[1]());
            console.log(Object.hasOwn(globalThis, "moduleItem"), Object.hasOwn(globalThis, "moduleKey"),
                Object.hasOwn(globalThis, "moduleEmptyItem"), Object.hasOwn(globalThis, "moduleEmptyKey"));
            export {};
            """, "4 4 last last\nfalse false false false\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ClassicLoopVarHeadsUseTheContainingDeclarationBinding(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        // This API interprets a classic script. The normal CLI module route
        // intentionally has a different global-publication contract.
        var result = SourceExecutionService.Interpret(source);
        Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
    }

    [Theory, MemberData(nameof(ModuleCases))]
    public void ModuleLoopVarSlotsRemainLocalAndPreserveAsyncCaptures(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted));
    }

    [Fact]
    public void EmptyVarLoopHeadsPreserveFunctionsInAnExistingClassicRealm()
    {
        using var output = new StringWriter();
        using var interpreter = new Interpreter(output, TextWriter.Null);
        Interpret("""
            function keepOf(): number { return 7; }
            function keepIn(): number { return 8; }
            """);
        Interpret("""
            for (var keepOf of [] as any[]) {}
            for (var keepIn in {}) {}
            console.log(keepOf(), keepIn(), (globalThis as any).keepOf === keepOf,
                (globalThis as any).keepIn === keepIn);
            """);
        Assert.Equal("7 8 true true\n", output.ToString().Replace("\r\n", "\n"));

        // Embeddings can evaluate later scripts against existing realm names.
        // Resolve each unit directly, matching the established shared-realm tests.
        void Interpret(string source)
        {
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            new VariableResolver(interpreter).Resolve(statements);
            interpreter.Interpret(statements);
            Assert.Null(interpreter.LastUncaughtError);
        }
    }
}
