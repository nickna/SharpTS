using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedBuiltInModuleRegistryTests
{
    public static IEnumerable<object[]> ExportKeys
    {
        get
        {
            var (registry, _) = Create(RuntimeFeatureSet.EmitEverything());
            return registry.SelectedExports.Select(key => new object[] { key.Module, key.Method }).ToArray();
        }
    }

    [Theory]
    [MemberData(nameof(ExportKeys))]
    public void EverySelectedExportRequiresOneLocalDeclarationAndFreezesOnCompletion(string module, string name)
    {
        var (registry, type) = Create(RuntimeFeatureSet.EmitEverything());
        var declaration = Define(type);
        foreach (var key in registry.SelectedExports.Where(key => key != (module, name)))
            registry.Register(key.Module, key.Method, declaration);
        Assert.Contains($"'{module}.{name}'", Assert.Throws<InvalidOperationException>(registry.CompleteEmission).Message);
        Assert.False(registry.IsComplete);
        Assert.Throws<InvalidOperationException>(() => registry.Require(module, name));
        Assert.Throws<InvalidOperationException>(() => registry.GetOptional(module, name));
        Assert.Throws<ArgumentNullException>(() => registry.Register(module, name, null!));
        var (_, foreignType) = Create(Minimal());
        Assert.Throws<ArgumentException>(() => registry.Register(module, name, Define(foreignType)));
        registry.Register(module, name, declaration);
        Assert.Same(declaration, registry.GetOptional(module, name));
        Assert.Throws<InvalidOperationException>(() => registry.Register(module, name, Define(type, "Replacement")));
        Assert.Same(declaration, registry.Require(module, name));
        registry.CompleteEmission();
        Assert.True(registry.IsComplete);
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => registry.Register(module, name, declaration));
        Assert.Same(declaration, registry.Require(module, name));
    }

    [Fact]
    public void SelectionIsExplicitImmutableAndIndependentOfLaterFeatureChanges()
    {
        var features = Minimal();
        var (registry, type) = Create(features);
        Assert.Equal(6, registry.SelectedExports.Count);
        Assert.All(registry.SelectedExports, key => Assert.Equal("timers", key.Module));
        Assert.False(registry.IsSelected("fs", "lstatSync"));
        Assert.Null(registry.GetOptional("fs", "lstatSync"));
        Assert.Null(registry.GetOptional("timers", "unknown"));
        Assert.Throws<InvalidOperationException>(() => registry.Require("fs", "lstatSync"));
        Assert.Throws<InvalidOperationException>(() => registry.Register("fs", "lstatSync", Define(type)));
        features.UsesFs = true;
        Assert.False(registry.IsSelected("fs", "lstatSync"));
        Assert.Throws<InvalidOperationException>(() => registry.BeginEmission((ModuleBuilder)type.Module, features));
        var keys = Assert.IsAssignableFrom<ICollection<(string, string)>>(registry.SelectedExports);
        Assert.True(keys.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => keys.Add(("fs", "lstatSync")));
        Assert.Throws<NotSupportedException>(keys.Clear);
        Assert.Equal(181, Create(RuntimeFeatureSet.EmitEverything()).Registry.SelectedExports.Count);
    }

    [Fact]
    public void RegistryRequiresInitializationAndNullInitializationCanBeRetried()
    {
        var registry = new EmittedBuiltInModuleRegistry();
        var (_, type) = Create(Minimal());
        var module = (ModuleBuilder)type.Module;
        Assert.Throws<InvalidOperationException>(() => registry.GetOptional("timers", "setTimeout"));
        Assert.Throws<InvalidOperationException>(() => registry.IsSelected("timers", "setTimeout"));
        Assert.Throws<InvalidOperationException>(() => registry.SelectedExports);
        Assert.Throws<InvalidOperationException>(() => registry.Register("timers", "setTimeout", Define(type)));
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        Assert.Throws<ArgumentNullException>(() => registry.BeginEmission(null!, Minimal()));
        Assert.Throws<ArgumentNullException>(() => registry.BeginEmission(module, null!));
        registry.BeginEmission(module, Minimal());
        Assert.True(registry.IsSelected("timers", "setTimeout"));
    }

    [Theory]
    [InlineData(null, "setTimeout")]
    [InlineData("", "setTimeout")]
    [InlineData("timers", null)]
    [InlineData("timers", "")]
    public void InvalidKeysCannotEnterTheRegistry(string? module, string? name)
    {
        var (registry, type) = Create(Minimal());
        Assert.ThrowsAny<ArgumentException>(() => registry.Register(module!, name!, Define(type)));
        Assert.ThrowsAny<ArgumentException>(() => registry.GetOptional(module!, name!));
        Assert.ThrowsAny<ArgumentException>(() => registry.IsSelected(module!, name!));
        Assert.Equal(6, registry.SelectedExports.Count);
    }

    [Fact]
    public void ForwardReferenceAndIntentionalAliasesSurviveCompletion()
    {
        var (registry, type) = Create(RuntimeFeatureSet.EmitEverything());
        var target = type.DefineMethod("Target", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        foreach (var key in registry.SelectedExports)
            registry.Register(key.Module, key.Method, target);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Call, registry.Require("dns", "getDefaultResultOrder"));
        il.Emit(OpCodes.Ret);
        Assert.Same(registry.Require("dns", "getDefaultResultOrder"), registry.Require("dns/promises", "getDefaultResultOrder"));
        Assert.Same(registry.Require("tls", "Server"), registry.Require("tls", "createServer"));
        registry.CompleteEmission();
        target.GetILGenerator().Emit(OpCodes.Ldc_I4, 27);
        target.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)type.Assembly).Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        Assert.Equal(27, Assembly.Load(bytes.ToArray()).GetType("Runtime")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FeatureSelectionAliasesAndPriorExportsSurviveEmitterReuse(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var registries = new HashSet<EmittedBuiltInModuleRegistry>();
        var saved = new List<MethodInfo>();
        foreach (string? source in new[] { "import * as dns from 'dns';", "console.log(1);", null, "import * as tls from 'tls';" })
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName($"module_registry_{Guid.NewGuid():N}"), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("main");
            var features = source is null ? RuntimeFeatureSet.EmitEverything() : Detect(source);
            bool timerPromisesSelected = hosted || features.UsesPromise;
            var runtime = emitter.EmitAll(module, features);
            var registry = runtime.BuiltInModules;
            Assert.True(registries.Add(registry));
            Assert.True(registry.IsComplete);
            Assert.Equal(timerPromisesSelected, registry.IsSelected("timers/promises", "setTimeout"));
            Assert.Equal(runtime.FileSystem is not null, registry.IsSelected("fs", "lstatSync"));
            Assert.Equal(runtime.Crypto is not null, registry.IsSelected("crypto", "randomBytes"));
            Assert.All(registry.SelectedExports, key => Assert.Same(module, registry.Require(key.Module, key.Method).Module));
            if (runtime.Tls is not null)
                Assert.Same(registry.Require("tls", "Server"), registry.Require("tls", "createServer"));
            using var bytes = new MemoryStream();
            assembly.Save(bytes); bytes.Position = 0;
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(bytes));
            var loaded = Assembly.Load(bytes.ToArray());
            if (runtime.Dns is not null)
            {
                var method = registry.Require("dns", "getDefaultResultOrder");
                Assert.Same(method, registry.Require("dns/promises", "getDefaultResultOrder"));
                saved.Add((MethodInfo)loaded.ManifestModule.ResolveMethod(method.MetadataToken)!);
            }
        }
        Assert.All(saved, method => Assert.Equal("verbatim", method.Invoke(null, null)));
    }

    private static RuntimeFeatureSet Detect(string source) =>
        new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static RuntimeFeatureSet Minimal() => Detect("console.log(1);");

    private static (EmittedBuiltInModuleRegistry Registry, TypeBuilder Type) Create(RuntimeFeatureSet features)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"registry_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var registry = new EmittedBuiltInModuleRegistry();
        registry.BeginEmission(module, features);
        return (registry, module.DefineType("Runtime", TypeAttributes.Public));
    }

    private static MethodBuilder Define(TypeBuilder type, string name = "Declared") =>
        type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
}
