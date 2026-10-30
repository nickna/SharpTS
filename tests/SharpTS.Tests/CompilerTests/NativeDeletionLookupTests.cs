using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeDeletionLookupTests
{
    private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags DynamicFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const BindingFlags Helpers = BindingFlags.Static | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletionKeepsDynamicStoresDescriptorsAndOwnIdentity(bool native)
    {
        const string source = """
            class Parent { inherited=1; }
            class Owner extends Parent { own=2; static field=3; }
            const object:any=new Owner(); const owner:any=Owner;
            object.extra=4;
            console.log(delete object.extra, Object.hasOwn(object,'extra'), Object.keys(object).join(','));
            Object.defineProperty(object,'configured',{get:()=>5,configurable:true,enumerable:true});
            console.log(delete object.configured, Object.hasOwn(object,'configured'));
            console.log(delete owner.field, Object.hasOwn(owner,'field'));
            owner.field=7;
            console.log(owner.field, Object.keys(owner).join(','));
            function strictDelete(){'use strict'; return delete object.missing;}
            console.log(strictDelete());
            """;
        var saved = Compile(source, native, out var compiler);
        Assert.Equal("true false inherited,own\ntrue false\ntrue false\n7 field\ntrue\n",
            TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        if (native) Assert.False(compiler.NativeDeletion!.UsesLegacy);
        else { Assert.Null(compiler.NativeDeletion); Assert.Null(saved.GetType("$NativeDeletionLookup")); }
    }

    [Fact]
    public void ExactSavedPropertyAndDynamicStoreQueriesMatchTheRealBinder()
    {
        var saved = Compile("class Owner { value=1; }", true, out var compiler);
        var metadata = compiler.NativeDeletion!;
        var property = Helper(saved, "FindStaticProperty");
        int present = 0;
        foreach (var original in metadata.StaticOwners)
        {
            var type = SavedType(saved, original);
            foreach (var name in type.GetProperties(StaticFlags).Select(member => member.Name).Distinct().Append("<missing>"))
            {
                var expected = type.GetProperty(name, StaticFlags);
                Assert.Equal(expected, property.Invoke(null, [type, name, StaticFlags]));
                if (expected is not null) present++;
            }
        }
        Assert.True(present > 0);
        var dynamicFields = Helper(saved, "FindDynamicFields");
        foreach (var original in metadata.DynamicOwners)
        {
            var type = SavedType(saved, original);
            Assert.Equal(type.GetMethod("$EnsureFields", DynamicFlags),
                dynamicFields.Invoke(null, [type, "$EnsureFields", DynamicFlags]));
        }
        Assert.NotNull(dynamicFields.Invoke(null, [NativeSourceTypeLookup.Find(saved, "Owner"), "$EnsureFields", DynamicFlags]));
        var error = Assert.Throws<TargetInvocationException>(() => property.Invoke(null, [typeof(double), null, StaticFlags]));
        Assert.Equal("name", Assert.IsType<ArgumentNullException>(error.InnerException).ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnprovedGenericInventoriesKeepTheCompleteOriginalOperations(bool generic)
    {
        var saved = Compile(generic ? "class Owner<T> { field:T; }" : "class Owner { field=1; }", true, out var compiler);
        Assert.Equal(generic, compiler.NativeDeletion!.UsesLegacy);
        var property = Helper(saved, "FindStaticProperty"); var dynamicFields = Helper(saved, "FindDynamicFields");
        if (generic)
        {
            Assert.Equal(typeof(Foreign).GetProperty(nameof(Foreign.Value), StaticFlags), property.Invoke(null, [typeof(Foreign), nameof(Foreign.Value), StaticFlags]));
            Assert.Equal(typeof(Foreign).GetMethod("Other", DynamicFlags), dynamicFields.Invoke(null, [typeof(Foreign), "Other", DynamicFlags]));
        }
        else
        {
            Refused(() => property.Invoke(null, [typeof(Foreign), nameof(Foreign.Value), StaticFlags]));
            Refused(() => property.Invoke(null, [typeof(double), "x", BindingFlags.Public | BindingFlags.Instance]));
            Refused(() => dynamicFields.Invoke(null, [typeof(Foreign), "$EnsureFields", DynamicFlags]));
            Refused(() => dynamicFields.Invoke(null, [NativeSourceTypeLookup.Find(saved, "Owner"), "Other", DynamicFlags]));
        }
    }

    [Fact]
    public void DeclarationAndCompletionValidateModeModuleShapeAndLifecycle()
    {
        _ = Compile("console.log(42);", true, out var compiler);
        var complete = compiler.NativeDeletion!;
        var runtime = (EmittedRuntime)typeof(ILCompiler).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compiler)!;
        Assert.Throws<InvalidOperationException>(() => complete.CompleteEmission(compiler.NativeRuntimeInitializers!, compiler.NativeProperties!, compiler.NativeSourceOwners!, runtime));
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("foreign_delete_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var helper = new NativeDeletionLookup(module, TypeProvider.Runtime);
        Assert.Throws<InvalidOperationException>(() => helper.StaticOwners);
        Assert.Throws<InvalidOperationException>(() => helper.ValidateForEmitter(module, false));
        Assert.Throws<InvalidOperationException>(() => helper.CompleteEmission(compiler.NativeRuntimeInitializers!, compiler.NativeProperties!, compiler.NativeSourceOwners!, runtime));
        helper.FindStaticProperty.SetReturnType(typeof(object));
        Assert.Throws<InvalidOperationException>(() => helper.ValidateForEmitter(module, true));
    }

    public sealed class Foreign
    {
        public static int Value => 7;
        internal int Other() => 8;
    }

    private static void Refused(Func<object?> action)
    {
        var error = Assert.Throws<TargetInvocationException>(action);
        Assert.Contains(NativeDeletionLookup.UnsupportedMessage, error.InnerException!.Message);
    }

    private static Type SavedType(Assembly saved, Type original) => original is TypeBuilder ? saved.GetType(original.FullName!)! : original;
    private static MethodInfo Helper(Assembly saved, string name) => saved.GetType("$NativeDeletionLookup")!.GetMethod(name, Helpers)!;
    private static Assembly Compile(string source, bool native, out ILCompiler compiler)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        compiler = new ILCompiler("native_delete_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes(); using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
}
