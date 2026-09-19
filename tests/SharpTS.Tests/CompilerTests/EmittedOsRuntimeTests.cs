using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedOsRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedOsRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var os = new EmittedOsRuntime();
        FillDeclarations(os, missingHandle);
        var property = typeof(EmittedOsRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(os));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(os.CompleteEmission).Message);
        Assert.False(os.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(os, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declarations = new EmittedOsRuntime();
        FillDeclarations(declarations);
        property.SetValue(os, property.GetValue(declarations));
        os.CompleteEmission();
        AssertFrozen(os);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.Os);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireOs).Message);
        runtime.BeginOsEmission();
        var os = runtime.RequireOs();
        Assert.Same(runtime.Os, os);
        Assert.Throws<InvalidOperationException>(runtime.BeginOsEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Os))!.SetMethod!.IsPrivate);
        FillDeclarations(os);
        os.CompleteEmission();
        AssertFrozen(os);
        Assert.Throws<InvalidOperationException>(runtime.BeginOsEmission);
    }

    [Fact]
    public void ForwardCallsCanUseDeclarationsBeforeTheirBodiesExist()
    {
        var os = new EmittedOsRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("os_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        os.Freemem = type.DefineMethod("Declared", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, os.Freemem);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(os.IsComplete);
        os.Freemem.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        os.Freemem.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        Assert.Equal(true, Assembly.Load(bytes.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(1);", false, false)]
    [InlineData("console.log(process.env);", false, false)]
    [InlineData("Buffer.from('data');", false, false)]
    [InlineData("import * as fs from 'fs';", false, false)]
    [InlineData("import * as child from 'child_process';", false, false)]
    [InlineData("import * as os from 'os';", true, false)]
    [InlineData("import * as os from 'node:os';", true, false)]
    [InlineData("const os = require('os');", true, false)]
    [InlineData("import('os');", true, false)]
    [InlineData("os.freemem();", true, false)]
    [InlineData("console.log(1);", false, true)]
    [InlineData("import * as os from 'os';", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void MinimalEnabledImpliedHostedAndFullEmissionPreserveFeatureSelection(string? source, bool enabled, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.Os is not null);
        if (enabled)
        {
            var os = runtime.RequireOs();
            AssertFrozen(os);
            Assert.Equal(3, Handles.Count());
            foreach (var property in Handles)
            {
                var member = Assert.IsAssignableFrom<MethodBuilder>(property.GetValue(os));
                Assert.Same(runtime.RuntimeClass.Type, member.DeclaringType);
                Assert.True(member.IsPublic && member.IsStatic);
                Assert.True(runtime.RuntimeClass.Type.IsCreated());
            }
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireOs);

        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "OsFreemem", "OsLoadavg", "OsNetworkInterfaces" }) Assert.Equal(enabled, methods.Contains(name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedHelpersVerifyAndPreserveCurrentCompiledBehavior(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import * as os from 'os';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var runtime = assembly.GetType("$Runtime")!;
        var memory = Assert.IsType<double>(runtime.GetMethod("OsFreemem")!.Invoke(null, null));
        Assert.True(double.IsFinite(memory) && memory > 0);
        var load = Assert.IsType<List<object>>(runtime.GetMethod("OsLoadavg")!.Invoke(null, null));
        Assert.Equal(new object[] { 0d, 0d, 0d }, load);
        Assert.NotSame(load, runtime.GetMethod("OsLoadavg")!.Invoke(null, null));
        var interfaces = Assert.IsType<Dictionary<string, object>>(runtime.GetMethod("OsNetworkInterfaces")!.Invoke(null, null));
        Assert.Empty(interfaces);
        Assert.NotSame(interfaces, runtime.GetMethod("OsNetworkInterfaces")!.Invoke(null, null));
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import * as os from 'os';", false, emitter).RequireOs();
        var minimal = EmitRuntime("console.log(1);", false, emitter);
        var second = EmitRuntime("import * as os from 'os';", false, emitter).RequireOs();
        Assert.Null(minimal.Os);
        Assert.NotSame(first, second);
        Assert.NotSame(first.Freemem, second.Freemem);
        Assert.NotSame(first.Loadavg, second.Loadavg);
        Assert.NotSame(first.NetworkInterfaces, second.NetworkInterfaces);
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static void FillDeclarations(EmittedOsRuntime os, string? missingHandle = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"os_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            property.SetValue(os, method);
        }
    }

    private static void AssertFrozen(EmittedOsRuntime os)
    {
        Assert.True(os.IsComplete);
        Assert.Throws<InvalidOperationException>(os.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(os);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(os, value));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"os_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
