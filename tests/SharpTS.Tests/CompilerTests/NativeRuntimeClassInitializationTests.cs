using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeRuntimeClassInitializationTests
{
    private const BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void LiteralInventoryDefersInitializationAndRunsAnOwnedInitializerOnce()
    {
        var saved = Compile("class State { static count = 0; } class Owner { static item = ++State.count; }", true, out var compiler);
        Assert.False(compiler.NativeRuntimeInitializers!.UsesLegacy);
        var initialize = Initializer(saved);
        var count = NativeSourceTypeLookup.Find(saved, "State")!.GetField("count")!;
        Assert.Equal(0d, count.GetValue(null));
        initialize.Invoke(null, [NativeSourceTypeLookup.Find(saved, "Owner")]);
        Assert.Equal(1d, count.GetValue(null));
        initialize.Invoke(null, [NativeSourceTypeLookup.Find(saved, "Owner")]);
        Assert.Equal(1d, count.GetValue(null));
        Assert.Contains(Instructions(initialize), instruction => instruction.Member?.Name == "RunClassConstructor");
        Assert.DoesNotContain(Instructions(initialize), instruction => instruction.Member?.Name == "get_TypeHandle");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntrinsicAndSourceSymbolLookupsPreserveUndefinedAndInitializationOrder(bool native)
    {
        const string source = """
            const events: string[] = [];
            function note(value: string): number { events.push(value); return 1; }
            class Base { static item = note('base'); static [Symbol.iterator]() { return 7; } }
            class Child extends Base { static item2 = note('child'); }
            const key = Symbol('missing');
            const constructors: any[] = [Object, Number, Boolean, String, Array, Map, Set, WeakMap, WeakSet,
                WeakRef, FinalizationRegistry, Promise, Function, Symbol, BigInt, Date, RegExp,
                Error, TypeError, RangeError, ReferenceError, SyntaxError, URIError, EvalError, AggregateError,
                MessageChannel, ArrayBuffer, SharedArrayBuffer, DataView, Uint8Array, TextEncoder, TextDecoder];
            console.log(constructors.every((owner: any) => owner[key] === undefined));
            console.log((Child as any)[Symbol.iterator](), events.join(','));
            """;
        var saved = Compile(source, native, out var compiler);
        Assert.Equal("true\n7 base,child\n", Run(saved));
        if (!native) { Assert.Null(compiler.NativeRuntimeInitializers); Assert.Null(saved.GetType("$NativeRuntimeClassInitialization")); }
        else
        {
            Assert.False(compiler.NativeRuntimeInitializers!.UsesLegacy);
            foreach (string name in new[] { "GetClassPrototype", "FindSymbolGetterFor", "FindOwnSymbolGetterFor", "FindSymbolSetterFor", "FindSymbolMethodFor", "FindOwnSymbolMethodFor" })
            {
                var method = saved.GetType("$Runtime")!.GetMethod(name)!;
                Assert.Contains(Instructions(method), instruction => instruction.Member?.DeclaringType?.Name == "$NativeRuntimeClassInitialization");
                Assert.DoesNotContain(Instructions(method), instruction => instruction.Member?.Name == "RunClassConstructor");
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnlyUnprovedGenericProducersRetainTheFullLegacyInitializationOperation(bool generic, bool unproved)
    {
        string source = generic ? "class Owner<T> { static item = 7; } console.log((Owner as any).item);" : "console.log(42);";
        source += unproved ? " const C:any=Owner; new C();" : "";
        var saved = Compile(source, true, out var compiler);
        Assert.Equal(generic ? "7\n" : "42\n", Run(saved));
        Assert.Equal(unproved, compiler.NativeRuntimeInitializers!.UsesLegacy);
        var initialize = Initializer(saved);
        Assert.Equal(unproved, Instructions(initialize).Any(instruction => instruction.Member?.Name == "get_TypeHandle"));
        if (unproved) initialize.Invoke(null, [typeof(ForeignOwner)]);
        else
        {
            var exception = Assert.Throws<TargetInvocationException>(() => initialize.Invoke(null, [typeof(ForeignOwner)]));
            Assert.Contains(NativeRuntimeClassInitialization.ForeignOwnerMessage, exception.InnerException!.Message);
        }
    }

    [Fact]
    public void ImportedOwnerCompletesAfterAllModuleDeclarations()
    {
        string directory = Path.Combine(Path.GetTempPath(), "native_initializer_" + Guid.NewGuid().ToString("N"));
        string entry = Path.Combine(directory, "main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(directory, "owner.ts")] = "export class Owner { static [Symbol.iterator]() { return 8; } }",
            [entry] = "import {Owner} from './owner'; console.log((Owner as any)[Symbol.iterator]());"
        };
        var resolver = new ModuleResolver(entry, files); var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = Compiler(true); compiler.CompileModules(modules, resolver, types);
        var saved = Verify(compiler.SaveToBytes());
        Assert.True(compiler.NativeRuntimeInitializers!.IsComplete); Assert.False(compiler.NativeRuntimeInitializers.UsesLegacy);
        Assert.Equal("8\n", Run(saved));
    }

    public sealed class ForeignOwner { }
    private static MethodInfo Initializer(Assembly saved) => saved.GetType("$NativeRuntimeClassInitialization")!.GetMethod("Initialize", StaticMethods)!;
    private static ILCompiler Compiler(bool native)
    {
        var compiler = new ILCompiler("native_initializer_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        return compiler;
    }
    private static Assembly Compile(string source, bool native, out ILCompiler compiler)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        compiler = Compiler(native); compiler.Compile(statements, types); return Verify(compiler.SaveToBytes());
    }
    private static Assembly Verify(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
    private static string Run(Assembly saved) => TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20));
}
