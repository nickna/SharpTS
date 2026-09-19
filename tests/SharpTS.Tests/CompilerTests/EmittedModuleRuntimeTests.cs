using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedModuleRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => new[]
    {
        ("Registry", typeof(EmittedModuleRuntime)),
        ("CommonJs", typeof(EmittedCommonJsRuntime)),
        ("DynamicImport", typeof(EmittedDynamicImportRuntime))
    }.SelectMany(group => Handles(group.Item2).Select(property => new object[] { group.Item1, property.Name }));

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionWithoutFreezingOtherGroups(string group, string missingHandle)
    {
        var modules = CreateDeclarations(true, true, group, missingHandle);
        var owner = Owner(modules, group);
        var property = owner.GetType().GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(owner));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(modules.CompleteEmission).Message);
        Assert.False(modules.IsComplete);
        Assert.False(modules.RequireCommonJs().IsComplete);
        Assert.False(modules.RequireDynamicImport().IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(owner, property.GetValue(Owner(CreateDeclarations(true, true), group)));
        modules.CompleteEmission();
        AssertFrozen(modules);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RequiredRegistryAndOptionalGroupsHaveExplicitAvailability(bool commonJs, bool dynamicImport)
    {
        var runtime = new EmittedRuntime();
        Assert.NotNull(runtime.Modules);
        Assert.Null(runtime.Modules.CommonJs);
        Assert.Null(runtime.Modules.DynamicImport);
        Assert.Throws<InvalidOperationException>(runtime.Modules.RequireCommonJs);
        Assert.Throws<InvalidOperationException>(runtime.Modules.RequireDynamicImport);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Modules))!.SetMethod);

        var modules = CreateDeclarations(commonJs, dynamicImport);
        Assert.Equal(commonJs, modules.CommonJs is not null);
        Assert.Equal(dynamicImport, modules.DynamicImport is not null);
        if (commonJs) Assert.Throws<InvalidOperationException>(modules.BeginCommonJsEmission);
        else Assert.Throws<InvalidOperationException>(modules.RequireCommonJs);
        if (dynamicImport) Assert.Throws<InvalidOperationException>(modules.BeginDynamicImportEmission);
        else Assert.Throws<InvalidOperationException>(modules.RequireDynamicImport);
        modules.CompleteEmission();
        AssertFrozen(modules);
        Assert.Throws<InvalidOperationException>(modules.BeginCommonJsEmission);
        Assert.Throws<InvalidOperationException>(modules.BeginDynamicImportEmission);
    }

    [Fact]
    public void ParentCompletionAcceptsAnAlreadyCompletedOptionalGroup()
    {
        var modules = CreateDeclarations(true, true);
        modules.RequireCommonJs().CompleteEmission();
        modules.CompleteEmission();
        AssertFrozen(modules);
    }

    [Fact]
    public void CommonJsDeclarationsAreAvailableBeforeLaterRegistryEmission()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("module_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var modules = new EmittedModuleRuntime();
        modules.BeginCommonJsEmission();
        var commonJs = modules.RequireCommonJs();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        void Emit(string name, params object[] arguments) => typeof(RuntimeEmitter)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(emitter, arguments);
        Emit("EmitCjsModuleClass", module, commonJs);
        Assert.Equal("$CJSModule", commonJs.Type.Name);
        Assert.NotNull(commonJs.Ctor);
        Assert.NotNull(commonJs.ExportsFieldInfoField);
        Assert.False(commonJs.IsComplete);
        Assert.Throws<InvalidOperationException>(() => modules.Registry);
        Assert.Throws<InvalidOperationException>(modules.CompleteEmission);
        Assert.False(commonJs.IsComplete);
        var helpers = module.DefineType("Helpers", TypeAttributes.Public);
        Emit("EmitModuleRegistry", helpers, modules);
        Assert.Same(helpers, modules.Registry.DeclaringType);
        Assert.Same(helpers, modules.Initialize.DeclaringType);
        Assert.Same(helpers, modules.Register.DeclaringType);
        helpers.CreateType();
        modules.CompleteEmission();
        AssertFrozen(modules);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    [Theory]
    [InlineData("const value = 1;", false, false, false)]
    [InlineData("import { value } from './lib';", false, false, false)]
    [InlineData("Promise.resolve(1);", false, false, false)]
    [InlineData("module.exports = 42;", true, false, false)]
    [InlineData("exports.value = 42;", true, false, false)]
    [InlineData("require('os');", true, false, false)]
    [InlineData("import('os');", false, true, false)]
    [InlineData("require('os'); import('os');", true, true, false)]
    [InlineData("const value = 1;", false, false, true)]
    [InlineData("require('os'); import('os');", true, true, true)]
    [InlineData(null, true, true, false)]
    [InlineData(null, true, true, true)]
    public void MinimalEnabledHostedAndFullEmissionPreserveFeatureSelection(string? source, bool commonJs, bool dynamicImport, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var modules = runtime.Modules;
        AssertFrozen(modules);
        Assert.Equal(commonJs, modules.CommonJs is not null);
        Assert.Equal(dynamicImport, modules.DynamicImport is not null);
        Assert.Same(runtime.RuntimeClass.Type, modules.Registry.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, modules.Initialize.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, modules.Register.DeclaringType);
        if (commonJs)
        {
            var cjs = modules.RequireCommonJs();
            Assert.Equal("$CJSModule", cjs.Type.Name);
            Assert.Equal(cjs.Type, cjs.Ctor.DeclaringType);
            Assert.Equal(cjs.Type, cjs.ExportsSetter.DeclaringType);
            foreach (var property in Handles(cjs.GetType()).Where(property => property.PropertyType == typeof(FieldBuilder)))
            {
                var field = (FieldBuilder)property.GetValue(cjs)!;
                Assert.Equal(cjs.Type, field.DeclaringType);
                Assert.True(field.IsPrivate);
            }
        }
        else Assert.Throws<InvalidOperationException>(modules.RequireCommonJs);
        if (dynamicImport) Assert.Same(runtime.RuntimeClass.Type, modules.RequireDynamicImport().ImportModule.DeclaringType);
        else Assert.Throws<InvalidOperationException>(modules.RequireDynamicImport);

        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        Assert.Equal(commonJs, types.Contains("$CJSModule"));
        Assert.Equal(dynamicImport, methods.Contains("DynamicImportModule"));
        Assert.Contains("InitializeModuleRegistry", methods);
        Assert.Contains("RegisterModule", methods);
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Fact]
    public async Task SavedRuntimePreservesLazyRegistryReplacementAndRejectedImports()
    {
        using var bytes = Save(EmitRuntime("import('example');", false));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var registry = type.GetField("_moduleRegistry", BindingFlags.NonPublic | BindingFlags.Static)!;
        var initialize = type.GetMethod("InitializeModuleRegistry")!;
        var register = type.GetMethod("RegisterModule")!;
        var import = type.GetMethod("DynamicImportModule")!;
        Task<object?> Import(string path) => (Task<object?>)import.Invoke(null, [path, ""])!;
        Assert.Null(registry.GetValue(null));
        Assert.Contains("not pre-compiled", (await Assert.ThrowsAsync<Exception>(() => Import("./missing"))).Message);
        initialize.Invoke(null, null);
        var firstRegistry = registry.GetValue(null);
        Assert.NotNull(firstRegistry);
        var first = new object();
        register.Invoke(null, ["example", new Func<object?>(() => first)]);
        initialize.Invoke(null, null);
        Assert.Same(firstRegistry, registry.GetValue(null));
        Assert.Same(first, await Import("example"));
        var second = new object();
        register.Invoke(null, ["example", new Func<object?>(() => second)]);
        Assert.Same(second, await Import("example"));
        Assert.Contains("not pre-compiled", (await Assert.ThrowsAsync<Exception>(() => Import("./missing"))).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedCommonJsRuntimePreservesExportsWriteThroughAndIndependentModuleState(bool hosted)
    {
        var runtime = EmitRuntime("module.exports = 1;", hosted);
        var cells = ((ModuleBuilder)runtime.RuntimeClass.Type.Module).DefineType("ExportCells", TypeAttributes.Public);
        cells.DefineField("First", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        cells.DefineField("Second", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        cells.CreateType();
        using var bytes = Save(runtime);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$CJSModule")!;
        var firstCell = assembly.GetType("ExportCells")!.GetField("First")!;
        var secondCell = assembly.GetType("ExportCells")!.GetField("Second")!;
        var parent = new object();
        var paths = new List<object> { "search" };
        var first = Activator.CreateInstance(type, [firstCell, "first", "first.cjs", paths, parent])!;
        var second = Activator.CreateInstance(type, [secondCell, "second", "second.cjs", new List<object>(), null])!;
        var exports = type.GetProperty("exports")!;
        var value = new object();
        exports.SetValue(first, value);
        Assert.Same(value, firstCell.GetValue(null));
        Assert.Null(exports.GetValue(second));
        var replacement = new object();
        firstCell.SetValue(null, replacement);
        Assert.Same(replacement, exports.GetValue(first));
        var getMember = type.GetMethod("GetMember")!;
        Assert.Same(replacement, getMember.Invoke(first, ["exports"]));
        Assert.Equal("first", getMember.Invoke(first, ["id"]));
        Assert.Equal("first.cjs", getMember.Invoke(first, ["filename"]));
        Assert.Equal(false, getMember.Invoke(first, ["loaded"]));
        Assert.Same(paths, getMember.Invoke(first, ["paths"]));
        Assert.Same(parent, getMember.Invoke(first, ["parent"]));
        Assert.NotSame(getMember.Invoke(first, ["children"]), getMember.Invoke(second, ["children"]));
        Assert.Null(getMember.Invoke(first, ["missing"]));
        var helpers = assembly.GetType("$Runtime")!;
        foreach (var name in new[] { "SetProperty", "SetPropertyStrict" })
        {
            var setter = helpers.GetMethod(name)!;
            firstCell.SetValue(null, null);
            setter.Invoke(null, name == "SetPropertyStrict" ? [first, "exports", value, true] : [first, "exports", value]);
            Assert.Same(value, firstCell.GetValue(null));
            setter.Invoke(null, name == "SetPropertyStrict" ? [first, "id", "ignored", true] : [first, "id", "ignored"]);
            Assert.Equal("first", getMember.Invoke(first, ["id"]));
            Assert.Null(secondCell.GetValue(null));
        }
    }

    [Fact]
    public void ReusingEmitterKeepsModuleMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("require('os'); import('os');", false, emitter).Modules;
        var minimal = EmitRuntime("const value = 1;", false, emitter).Modules;
        var second = EmitRuntime("require('os'); import('os');", false, emitter).Modules;
        Assert.Null(minimal.CommonJs);
        Assert.Null(minimal.DynamicImport);
        foreach (var group in new[] { "Registry", "CommonJs", "DynamicImport" })
        {
            var left = Owner(first, group);
            var right = Owner(second, group);
            Assert.NotSame(left, right);
            foreach (var property in Handles(left.GetType())) Assert.NotSame(property.GetValue(left), property.GetValue(right));
        }
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static object Owner(EmittedModuleRuntime modules, string group) => group switch
    {
        "Registry" => modules,
        "CommonJs" => modules.RequireCommonJs(),
        "DynamicImport" => modules.RequireDynamicImport(),
        _ => throw new ArgumentOutOfRangeException(nameof(group))
    };

    private static EmittedModuleRuntime CreateDeclarations(bool commonJs, bool dynamicImport, string? missingGroup = null, string? missingHandle = null)
    {
        var modules = new EmittedModuleRuntime();
        if (commonJs) modules.BeginCommonJsEmission();
        if (dynamicImport) modules.BeginDynamicImportEmission();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"module_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        foreach (var group in new[] { "Registry", "CommonJs", "DynamicImport" })
        {
            if (group == "CommonJs" && !commonJs || group == "DynamicImport" && !dynamicImport) continue;
            var owner = Owner(modules, group);
            foreach (var property in Handles(owner.GetType()).Where(property => group != missingGroup || property.Name != missingHandle))
            {
                object handle = property.PropertyType == typeof(Type) ? type
                    : property.PropertyType == typeof(ConstructorInfo) ? ctor
                    : property.PropertyType == typeof(FieldBuilder) ? field : method;
                property.SetValue(owner, handle);
            }
        }
        return modules;
    }

    private static void AssertFrozen(EmittedModuleRuntime modules)
    {
        Assert.True(modules.IsComplete);
        Assert.Throws<InvalidOperationException>(modules.CompleteEmission);
        var owners = new List<object> { modules };
        if (modules.CommonJs is not null)
        {
            Assert.True(modules.CommonJs.IsComplete);
            Assert.Throws<InvalidOperationException>(modules.CommonJs.CompleteEmission);
            owners.Add(modules.CommonJs);
        }
        if (modules.DynamicImport is not null)
        {
            Assert.True(modules.DynamicImport.IsComplete);
            Assert.Throws<InvalidOperationException>(modules.DynamicImport.CompleteEmission);
            owners.Add(modules.DynamicImport);
        }
        foreach (var owner in owners)
        foreach (var property in Handles(owner.GetType()))
        {
            var value = property.GetValue(owner);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"module_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
