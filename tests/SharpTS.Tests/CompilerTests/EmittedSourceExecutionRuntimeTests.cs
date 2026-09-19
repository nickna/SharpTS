using System.Reflection;
using System.Text.Json;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[Collection("CompilationService")]
public class EmittedSourceExecutionRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedSourceExecutionRuntime).GetProperties()
        .Where(property => property.PropertyType == typeof(MethodBuilder));

    private static readonly (string Export, string Handle)[] RegisteredExports =
    [
        ("runSourceJson", "RunJson"), ("configureUntrustedProcess", "ConfigureUntrustedProcess")
    ];

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var sourceExecution = CreateDeclarations(missingHandle);
        var property = typeof(EmittedSourceExecutionRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(sourceExecution));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(sourceExecution.CompleteEmission).Message);
        Assert.False(sourceExecution.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(sourceExecution, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(sourceExecution, property.GetValue(CreateDeclarations()));
        sourceExecution.CompleteEmission();
        AssertFrozen(sourceExecution);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.SourceExecution);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireSourceExecution).Message);
        runtime.BeginSourceExecutionEmission();
        var sourceExecution = runtime.RequireSourceExecution();
        Assert.Same(runtime.SourceExecution, sourceExecution);
        Assert.Throws<InvalidOperationException>(runtime.BeginSourceExecutionEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.SourceExecution))!.SetMethod!.IsPrivate);
        var declarations = CreateDeclarations();
        foreach (var property in Handles) property.SetValue(sourceExecution, property.GetValue(declarations));
        sourceExecution.CompleteEmission();
        AssertFrozen(sourceExecution);
        Assert.Throws<InvalidOperationException>(runtime.BeginSourceExecutionEmission);
    }

    [Fact]
    public void DeclarationsRegisterInOrderBeforeBodiesAndFamilyCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("source_execution_staged"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Runtime", TypeAttributes.Public);
        var sourceExecution = new EmittedSourceExecutionRuntime();
        var runtime = new EmittedRuntime();
        var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow());
        features.UsesSourceExecution = true;
        runtime.BuiltInModules.BeginEmission((ModuleBuilder)type.Module, features);
        var registrations = new List<string>();
        Action<string, MethodBuilder> register = (name, method) =>
        {
            var expected = RegisteredExports[registrations.Count];
            Assert.Equal(expected.Export, name);
            Assert.Same(method, typeof(EmittedSourceExecutionRuntime).GetProperty(expected.Handle)!.GetValue(sourceExecution));
            Assert.Equal(0, method.GetILGenerator().ILOffset);
            Assert.False(sourceExecution.IsComplete);
            Assert.False(type.IsCreated());
            runtime.BuiltInModules.Register("sharpts:execution", name, method);
            Assert.Same(method, runtime.BuiltInModules.GetOptional("sharpts:execution", name));
            registrations.Add(name);
        };
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetMethod("EmitSourceExecutionMethods", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [type, sourceExecution, register]);
        Assert.Equal(RegisteredExports.Select(entry => entry.Export), registrations);
        foreach (var property in Handles)
            Assert.True(((MethodBuilder)property.GetValue(sourceExecution)!).GetILGenerator().ILOffset > 0);
        Assert.False(sourceExecution.IsComplete);
        type.CreateType();
        sourceExecution.CompleteEmission();
        AssertFrozen(sourceExecution);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    [Theory]
    [InlineData("const value = 1;", false, false)]
    [InlineData("import * as execution from 'sharpts:execution';", true, false)]
    [InlineData("import { runSourceJson } from 'sharpts:execution';", true, false)]
    [InlineData("const execution = require('sharpts:execution');", true, false)]
    [InlineData("import('sharpts:execution');", true, false)]
    [InlineData("import * as vm from 'vm';", false, false)]
    [InlineData("const value = 1;", false, true)]
    [InlineData("import * as execution from 'sharpts:execution';", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void FeatureGatesPreserveOptionalMetadataRegistryAndRuntimeRequirement(string? source, bool enabled, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.SourceExecution is not null);
        Assert.Equal(enabled, runtime.Deployment.Reasons.Contains("sharpts:execution module"));
        if (enabled)
        {
            var sourceExecution = runtime.RequireSourceExecution();
            AssertFrozen(sourceExecution);
            Assert.Equal(2, Handles.Count());
            Assert.True(runtime.RequirePromise().IsComplete);
            Assert.True(runtime.Deployment.Requirements.HasFlag(SharpTSRuntimeRequirements.RuntimeAssembly));
            Assert.True(runtime.Deployment.Requirements.HasFlag(SharpTSRuntimeRequirements.FullDependencyClosure));
            Assert.True(runtime.Deployment.Requirements.HasFlag(SharpTSRuntimeRequirements.ManagedCompilerHost));
            foreach (var (export, handle) in RegisteredExports)
                Assert.Same(typeof(EmittedSourceExecutionRuntime).GetProperty(handle)!.GetValue(sourceExecution), runtime.BuiltInModules.GetOptional("sharpts:execution", export));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(runtime.RequireSourceExecution);
            foreach (var (export, _) in RegisteredExports) Assert.Null(runtime.BuiltInModules.GetOptional("sharpts:execution", export));
        }
        foreach (var name in new[] { "RunJson", "ConfigureUntrustedProcess", "unknown" })
            Assert.Null(runtime.BuiltInModules.GetOptional("sharpts:execution", name));
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var property in Handles) Assert.Equal(enabled, methods.Contains("SourceExecution" + property.Name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedHelpersVerifyAndPreserveLateBoundExecutionAndArgumentValidation(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import * as execution from 'sharpts:execution';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var json = Assert.IsType<string>(type.GetMethod("SourceExecutionRunJson")!
            .Invoke(null, ["console.log(42);", "interpret", 1024d]));
        using var result = JsonDocument.Parse(json);
        Assert.True(result.RootElement.GetProperty("Success").GetBoolean());
        Assert.Equal("42\n", result.RootElement.GetProperty("Output").GetString()!.Replace("\r\n", "\n"));
        Assert.Equal(0, result.RootElement.GetProperty("Errors").GetArrayLength());
        // Invalid input is checked before this API changes process-wide controls or proxies.
        // Valid configuration is exercised only by isolated CLI fixtures below.
        var error = Assert.Throws<TargetInvocationException>(() =>
            type.GetMethod("SourceExecutionConfigureUntrustedProcess")!.Invoke(null, [null]));
        Assert.IsType<ArgumentException>(error.GetBaseException());
        Assert.Contains("blocked proxy URI", error.GetBaseException().Message);
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import * as execution from 'sharpts:execution';", false, emitter).RequireSourceExecution();
        var minimal = EmitRuntime("const value=1;", false, emitter);
        var second = EmitRuntime("import * as execution from 'sharpts:execution';", false, emitter).RequireSourceExecution();
        Assert.Null(minimal.SourceExecution);
        Assert.NotSame(first, second);
        foreach (var property in Handles) Assert.NotSame(property.GetValue(first), property.GetValue(second));
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static EmittedSourceExecutionRuntime CreateDeclarations(string? missingHandle = null)
    {
        var sourceExecution = new EmittedSourceExecutionRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"source_execution_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        foreach (var property in Handles.Where(property => property.Name != missingHandle)) property.SetValue(sourceExecution, method);
        return sourceExecution;
    }

    private static void AssertFrozen(EmittedSourceExecutionRuntime sourceExecution)
    {
        Assert.True(sourceExecution.IsComplete);
        Assert.Throws<InvalidOperationException>(sourceExecution.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(sourceExecution);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(sourceExecution, value));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"source_execution_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
