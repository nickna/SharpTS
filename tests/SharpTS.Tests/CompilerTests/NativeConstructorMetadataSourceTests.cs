using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeConstructorMetadataSourceTests
{
    public const string EvaluationSource = """
        const trace: string[] = [];
        class First {
            value: any;
            constructor(value: any = "default", ...rest: any[]) {
                trace.push("body"); this.value = value + ":" + rest.join(",");
            }
        }
        class Second {}
        let current: any = First;
        const holder: any = { get C() { trace.push("callee"); return current; } };
        function argument() { trace.push("argument"); current = Second; return undefined; }
        function* spread() { trace.push("spread"); yield 2; yield 3; trace.push("done"); }
        const result: any = new holder.C(argument(), ...spread());
        console.log(result instanceof First, result.value, trace.join(","));
        """;

    public const string FailureSource = """
        const trace: string[] = [];
        class Owner { constructor(value: any) { trace.push("body"); } }
        const holder: any = {
            get bad() { trace.push("callee"); throw new Error("callee"); },
            get good() { trace.push("callee"); return Owner; }
        };
        function argument() { trace.push("argument"); throw new Error("argument"); }
        try { new holder.bad(argument()); } catch (error) { console.log(error.message); }
        console.log(trace.join(","));
        trace.length = 0;
        try { new holder.good(argument()); } catch (error) { console.log(error.message); }
        console.log(trace.join(","));
        """;

    public const string ReassignedGenericSource = """
        class First<T> { value: T; kind: string; constructor(value: T) { this.value = value; this.kind = "first"; } }
        class Second<T> { value: T; kind: string; constructor(value: T) { this.value = value; this.kind = "second"; } }
        let C: typeof First = First;
        C = Second;
        const value = new C<number>(7);
        console.log(value.value, value.kind, value instanceof Second, value instanceof First);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["callee-before-arguments-and-spread", EvaluationSource,
            "true default:2,3 callee,argument,spread,done,body\n"];
        yield return ["exceptions-before-constructor-body", FailureSource,
            "callee\ncallee\nargument\ncallee,argument\n"];
        yield return ["evaluated-generic-owner-over-annotation", ReassignedGenericSource, "7 second true false\n"];
        foreach (var row in InheritedGenericConstructorTests.Cases()) yield return row;
        yield return ["constrained-generic-closure", """
            class Item { value = 7; }
            class Child extends Item {}
            class Box<T extends Item> {
                value: T;
                constructor(value: T) { this.value = value; }
            }
            const C: any = Box;
            const direct = new Box<Child>(new Child());
            const dynamic: any = new C(new Child());
            console.log(direct.value.value, dynamic.value.value, dynamic.value instanceof Child);
            """, "7 7 true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativeMetadataPreservesEvaluatedConstructorBehavior(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = Parse(source, out var types);
        var compiler = Compiler();
        compiler.Compile(statements, types);
        Assert.True(compiler.NativeEvaluatedConstructors!.IsComplete);
        Assert.Equal(expected, Run(compiler.SaveToBytes()));
    }

    [Fact]
    public void ModuleAliasValuesCompleteAfterAllProducerBodies()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.GetPath("main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [directory.GetPath("lib.ts")] = """
                export class Base<T> { constructor(public value:T){} }
                export class Parent { constructor(public value:any=7,...rest:any[]){this.value+=":"+rest.join(",");} }
                """,
            [entry] = """
                import {Base as Generic, Parent as Imported} from "./lib";
                class Child extends Imported {}
                const C:any=Child;
                const G:any=Generic;
                console.log(new C(undefined,1,2).value, new G("generic").value);
                """
        };
        var resolver = new ModuleResolver(entry, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = Compiler(); compiler.CompileModules(modules, resolver, types);
        Assert.True(compiler.NativeEvaluatedConstructors!.IsComplete);
        Assert.Equal("7:1,2 generic\n", Run(compiler.SaveToBytes()));
    }

    private static List<Stmt> Parse(string source, out TypeMap types)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        return statements;
    }

    private static ILCompiler Compiler()
    {
        var compiler = new ILCompiler("constructor_metadata_source_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); return compiler;
    }

    private static string Run(byte[] bytes)
    {
        using var image = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(image));
        return TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20));
    }
}