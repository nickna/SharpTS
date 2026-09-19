using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedVmRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedVmRuntime).GetProperties()
        .Where(property => property.PropertyType == typeof(MethodBuilder));

    private static readonly (string Export, string Handle)[] RegisteredExports =
    [
        ("runInNewContext", "RunInNewContext"), ("runInThisContext", "RunInThisContext"),
        ("runInContext", "RunInContext"), ("createContext", "CreateContext"),
        ("isContext", "IsContext"), ("compileFunction", "CompileFunction"),
        ("measureMemory", "MeasureMemory"), ("Script", "GetScriptConstructor")
    ];

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var vm = CreateDeclarations(missingHandle);
        var property = typeof(EmittedVmRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(vm));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(vm.CompleteEmission).Message);
        Assert.False(vm.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(vm, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(vm, property.GetValue(CreateDeclarations()));
        vm.CompleteEmission();
        AssertFrozen(vm);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.Vm);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireVm).Message);
        runtime.BeginVmEmission();
        var vm = runtime.RequireVm();
        Assert.Same(runtime.Vm, vm);
        Assert.Throws<InvalidOperationException>(runtime.BeginVmEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Vm))!.SetMethod!.IsPrivate);
        var declarations = CreateDeclarations();
        foreach (var property in Handles) property.SetValue(vm, property.GetValue(declarations));
        vm.CompleteEmission();
        AssertFrozen(vm);
        Assert.Throws<InvalidOperationException>(runtime.BeginVmEmission);
    }

    [Fact]
    public void DeclarationsRegisterInOrderBeforeBodiesAndFamilyCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("vm_staged"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Runtime", TypeAttributes.Public);
        var resolve = type.DefineMethod("Resolve", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object)]);
        resolve.GetILGenerator().Emit(OpCodes.Ldarg_0);
        resolve.GetILGenerator().Emit(OpCodes.Ret);
        var promise = new EmittedPromiseRuntime { TypeResolve = resolve };
        var vm = new EmittedVmRuntime();
        var runtime = new EmittedRuntime();
        var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow());
        features.UsesVm = true;
        runtime.BuiltInModules.BeginEmission((ModuleBuilder)type.Module, features);
        var registrations = new List<string>();
        Action<string, MethodBuilder> register = (name, method) =>
        {
            var expected = RegisteredExports[registrations.Count];
            Assert.Equal(expected.Export, name);
            Assert.Same(method, typeof(EmittedVmRuntime).GetProperty(expected.Handle)!.GetValue(vm));
            Assert.Equal(0, method.GetILGenerator().ILOffset);
            Assert.False(vm.IsComplete);
            Assert.False(type.IsCreated());
            runtime.BuiltInModules.Register("vm", name, method);
            Assert.Same(method, runtime.BuiltInModules.GetOptional("vm", name));
            registrations.Add(name);
        };
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetMethod("EmitVmMethods", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [type, vm, promise, register]);
        Assert.Equal(RegisteredExports.Select(entry => entry.Export), registrations);
        foreach (var property in Handles)
            Assert.True(((MethodBuilder)property.GetValue(vm)!).GetILGenerator().ILOffset > 0);
        Assert.False(vm.IsComplete);
        type.CreateType();
        vm.CompleteEmission();
        AssertFrozen(vm);
        Assert.False(promise.IsComplete);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    [Theory]
    [InlineData("const value = 1;", false, false)]
    [InlineData("import * as vm from 'vm';", true, false)]
    [InlineData("import { Script } from 'node:vm';", true, false)]
    [InlineData("const vm = require('vm');", true, false)]
    [InlineData("const vm = require('node:vm');", true, false)]
    [InlineData("import('vm');", true, false)]
    [InlineData("import * as execution from 'sharpts:execution';", false, false)]
    [InlineData("const value = 1;", false, true)]
    [InlineData("import * as vm from 'vm';", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void FeatureGatesPreserveOptionalMetadataRegistryAndRuntimeRequirement(string? source, bool enabled, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.Vm is not null);
        Assert.Equal(enabled, runtime.Deployment.Reasons.Contains("vm module"));
        if (enabled)
        {
            var vm = runtime.RequireVm();
            AssertFrozen(vm);
            Assert.Equal(12, Handles.Count());
            Assert.True(runtime.RequirePromise().IsComplete);
            foreach (var (export, handle) in RegisteredExports)
                Assert.Same(typeof(EmittedVmRuntime).GetProperty(handle)!.GetValue(vm), runtime.BuiltInModules.GetOptional("vm", export));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(runtime.RequireVm);
            foreach (var (export, _) in RegisteredExports) Assert.Null(runtime.BuiltInModules.GetOptional("vm", export));
        }
        foreach (var name in new[] { "constants", "SourceTextModule", "SyntheticModule", "unknown" })
            Assert.Null(runtime.BuiltInModules.GetOptional("vm", name));
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var property in Handles) Assert.Equal(enabled, methods.Contains("Vm" + property.Name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedHelpersVerifyAndLoadTheInterpreterByReflection(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import * as vm from 'vm';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Equal(3d, type.GetMethod("VmRunInNewContext")!.Invoke(null, ["1+2", null, null]));
        var context = type.GetMethod("VmCreateContext")!.Invoke(null, [null, null]);
        Assert.Equal(true, type.GetMethod("VmIsContext")!.Invoke(null, [context]));
        Assert.Equal(42d, type.GetMethod("VmRunInContext")!.Invoke(null, ["6*7", context, null]));
        Assert.NotNull(type.GetMethod("VmGetScriptConstructor")!.Invoke(null, null));
        Assert.NotNull(type.GetMethod("VmGetConstants")!.Invoke(null, null));
        Assert.NotNull(type.GetMethod("VmCompileFunction")!.Invoke(null, ["return 42;", null, null]));
        Assert.NotNull(type.GetMethod("VmNewScript")!.Invoke(null, ["40+2", null]));
        Assert.NotNull(type.GetMethod("VmNewSourceTextModule")!.Invoke(null, ["export const value=1;", null]));
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import * as vm from 'vm';", false, emitter).RequireVm();
        var minimal = EmitRuntime("const value=1;", false, emitter);
        var second = EmitRuntime("import * as vm from 'vm';", false, emitter).RequireVm();
        Assert.Null(minimal.Vm);
        Assert.NotSame(first, second);
        foreach (var property in Handles) Assert.NotSame(property.GetValue(first), property.GetValue(second));
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static EmittedVmRuntime CreateDeclarations(string? missingHandle = null)
    {
        var vm = new EmittedVmRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"vm_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        foreach (var property in Handles.Where(property => property.Name != missingHandle)) property.SetValue(vm, method);
        return vm;
    }

    private static void AssertFrozen(EmittedVmRuntime vm)
    {
        Assert.True(vm.IsComplete);
        Assert.Throws<InvalidOperationException>(vm.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(vm);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(vm, value));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"vm_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
