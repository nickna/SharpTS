using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeStaticFieldLookupTests
{
    private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly BindingFlags[] Flags = [BindingFlags.Public | BindingFlags.Static,
        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticFieldReadsWritesDeletionAndDescriptorsKeepTheirOwners(bool native)
    {
        const string source = """
            class Parent { static inherited=3; static hidden=4; }
            class Child extends Parent { static own=7; static hidden=8; }
            const parent:any=Parent; const child:any=Child;
            console.log(child.inherited, child.hidden, Object.hasOwn(child,'inherited'));
            child.inherited=9;
            console.log(parent.inherited, child.inherited, Object.hasOwn(child,'inherited'));
            console.log(Object.getOwnPropertyDescriptor(child,'own').enumerable);
            console.log(delete child.own, Object.hasOwn(child,'own'));
            Object.defineProperty(child,'own',{value:11,writable:true,enumerable:true,configurable:true});
            child.own=12;
            console.log(child.own, child.propertyIsEnumerable('own'));
            """;
        var saved = Compile(source, native, out var compiler);
        Assert.Equal("3 8 false\n3 9 true\ntrue\ntrue false\n12 true\n",
            TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        if (native) Assert.False(compiler.NativeStaticFields!.UsesLegacy);
        else { Assert.Null(compiler.NativeStaticFields); Assert.Null(saved.GetType("$NativeStaticFieldLookup")); }
    }

    [Fact]
    public void LiteralQueriesMatchTheSavedBinderIncludingInheritanceAndNullNames()
    {
        var saved = Compile("class Parent { static same=1; static inherited=2; } class Child extends Parent { static same=3; }", true, out var compiler);
        var lookup = Helper(saved);
        foreach (var original in compiler.NativeStaticFields!.Owners)
        {
            var type = original is TypeBuilder ? saved.GetType(original.FullName!)! : original;
            foreach (var flags in Flags)
            foreach (var name in type.GetFields(flags).Select(field => field.Name).Append("<absent>"))
            {
                var expected = type.GetField(name, flags);
                var actual = lookup.Invoke(null, [type, name, flags]) as FieldInfo;
                Assert.Equal(expected, actual);
            }
        }
        var error = Assert.Throws<TargetInvocationException>(() => lookup.Invoke(null, [typeof(double), null, Flags[0]]));
        Assert.Equal("name", Assert.IsType<ArgumentNullException>(error.InnerException).ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnprovedGenericInventoriesRetainTheWholeLegacyOperation(bool generic)
    {
        var saved = Compile(generic ? "class Owner<T> { static field=7; }" : "console.log(42);", true, out var compiler);
        Assert.Equal(generic, compiler.NativeStaticFields!.UsesLegacy);
        if (generic)
            Assert.Equal(typeof(Foreign).GetField(nameof(Foreign.Field)), Helper(saved).Invoke(null, [typeof(Foreign), nameof(Foreign.Field), Flags[0]]));
        else
        {
            AssertRefused(typeof(Foreign), Flags[0]);
            AssertRefused(typeof(double), BindingFlags.Public | BindingFlags.Instance);
        }
        void AssertRefused(Type owner, BindingFlags flags)
        {
            var error = Assert.Throws<TargetInvocationException>(() => Helper(saved).Invoke(null, [owner, "Field", flags]));
            Assert.Contains(NativeStaticFieldLookup.UnsupportedOwnerMessage, error.InnerException!.Message);
        }
    }

    [Fact]
    public void CompletionRequiresTheExactModuleAndCannotBeRepeated()
    {
        _ = Compile("console.log(42);", true, out var compiler);
        var completed = compiler.NativeStaticFields!;
        var runtime = (EmittedRuntime)typeof(ILCompiler).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compiler)!;
        Assert.Throws<InvalidOperationException>(() => completed.CompleteEmission(compiler.NativeRuntimeInitializers!, compiler.NativeSourceOwners!, runtime));
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("foreign_fields_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var foreign = new NativeStaticFieldLookup(module, TypeProvider.Runtime);
        Assert.Throws<InvalidOperationException>(() => foreign.Owners);
        Assert.Throws<InvalidOperationException>(() => foreign.CompleteEmission(compiler.NativeRuntimeInitializers!, compiler.NativeSourceOwners!, runtime));
        Assert.Throws<InvalidOperationException>(() => new RuntimeEmitter(TypeProvider.Runtime, false, true, false, completed.FindField).EmitAll(module));
        var wrong = module.DefineType("WrongShape").DefineMethod("Lookup", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(Type), typeof(string), typeof(BindingFlags)]);
        Assert.Throws<InvalidOperationException>(() => new RuntimeEmitter(TypeProvider.Runtime, false, true, false, wrong).EmitAll(module));
    }

    public sealed class Foreign { public static int Field = 7; }
    private static MethodInfo Helper(Assembly saved) => saved.GetType("$NativeStaticFieldLookup")!.GetMethod("FindField", Methods)!;
    private static Assembly Compile(string source, bool native, out ILCompiler compiler)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        compiler = new ILCompiler("native_fields_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes(); using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
}
