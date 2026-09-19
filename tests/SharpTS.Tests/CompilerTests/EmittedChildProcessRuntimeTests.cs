using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedChildProcessRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedChildProcessRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var child = new EmittedChildProcessRuntime();
        FillDeclarations(child, missingHandle);
        var property = typeof(EmittedChildProcessRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(child));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(child.CompleteEmission).Message);
        Assert.False(child.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(child, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declarations = new EmittedChildProcessRuntime();
        FillDeclarations(declarations);
        property.SetValue(child, property.GetValue(declarations));
        child.CompleteEmission();
        AssertFrozen(child);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.ChildProcess);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireChildProcess).Message);
        runtime.BeginChildProcessEmission();
        var child = runtime.RequireChildProcess();
        Assert.Same(runtime.ChildProcess, child);
        Assert.Throws<InvalidOperationException>(runtime.BeginChildProcessEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ChildProcess))!.SetMethod!.IsPrivate);
        FillDeclarations(child);
        child.CompleteEmission();
        AssertFrozen(child);
        Assert.Throws<InvalidOperationException>(runtime.BeginChildProcessEmission);
    }

    [Fact]
    public void ForwardCallsCanUseDeclarationsBeforeTheirBodiesExist()
    {
        var child = new EmittedChildProcessRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("child_process_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        child.ContextRunCaptured = type.DefineMethod("Declared", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, child.ContextRunCaptured);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(child.IsComplete);
        child.ContextRunCaptured.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        child.ContextRunCaptured.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        Assert.Equal(true, Assembly.Load(bytes.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(1);", false, false)]
    [InlineData("console.log(process.env);", false, false)]
    [InlineData("Promise.resolve(1);", false, false)]
    [InlineData("Buffer.from('data');", false, false)]
    [InlineData("process.stdout.write('data');", false, false)]
    [InlineData("import * as stream from 'stream';", false, false)]
    [InlineData("import * as fs from 'fs';", false, false)]
    [InlineData("import * as http from 'http';", false, false)]
    [InlineData("import * as os from 'os';", false, false)]
    [InlineData("import * as child from 'child_process';", true, false)]
    [InlineData("import * as child from 'node:child_process';", true, false)]
    [InlineData("const child = require('child_process');", true, false)]
    [InlineData("import('child_process');", true, false)]
    [InlineData("console.log(1);", false, true)]
    [InlineData("console.log(process.env);", false, true)]
    [InlineData("import * as stream from 'stream';", false, true)]
    [InlineData("import * as child from 'child_process';", true, true)]
    [InlineData("import * as child from 'node:child_process';", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void MinimalEnabledImpliedHostedAndFullEmissionPreserveFeatureSelection(string? source, bool enabled, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.ChildProcess is not null);
        if (enabled)
        {
            var child = runtime.RequireChildProcess();
            AssertFrozen(child);
            Assert.Equal(70, Handles.Count());
            Assert.NotNull(runtime.Buffer);
            Assert.NotNull(runtime.NodeStreams);
            Assert.NotNull(runtime.Promise);
            Assert.Equal("$ChildProcessCtx", child.ContextType.Name);
            Assert.Equal("$ChildPush", child.PushType.Name);
            Assert.Same(child.ContextType, child.ContextCtor.DeclaringType);
            Assert.Same(child.ContextType, child.ContextProc.DeclaringType);
            Assert.Same(child.ContextType, child.ContextRunCaptured.DeclaringType);
            Assert.Same(child.ContextType, child.ContextRunStreamed.DeclaringType);
            Assert.Same(child.PushType, child.PushCtor.DeclaringType);
            Assert.Same(child.PushType, child.PushRun.DeclaringType);
            Assert.Same(runtime.RuntimeType, child.OwnedProcessesField.DeclaringType);
            Assert.Same(runtime.RuntimeType, child.TerminateOwned.DeclaringType);
            Assert.Same(runtime.RuntimeType, child.ExecSync.DeclaringType);
            Assert.Equal(typeof(System.Diagnostics.Process), child.ProcessStart.DeclaringType);
            Assert.Equal("Start", child.ProcessStart.Name);
            Assert.Equal("GetMethodFromHandle", child.GetMethodFromHandle.Name);
            foreach (var property in Handles.Where(property => property.PropertyType != typeof(MethodInfo)))
            {
                var member = (MemberInfo)property.GetValue(child)!;
                Assert.True(((TypeBuilder)(member is Type ? member : member.DeclaringType)!).IsCreated(), property.Name);
            }
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireChildProcess);

        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "$ChildProcessCtx", "$ChildPush" }) Assert.Equal(enabled, types.Contains(name));
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "ChildProcessExecSync", "ChildProcessSpawnSync", "ChildProcessExec", "ChildProcessSpawn", "ChildProcessExecFileSync", "ChildProcessExecFile", "ChildProcessFork", "ChildProcessTerminateOwned" })
            Assert.Equal(enabled, methods.Contains(name));
        var fields = reader.FieldDefinitions.Select(handle => reader.GetString(reader.GetFieldDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "_ownedChildProcesses", "_childProcessOwnershipStopping" }) Assert.Equal(enabled, fields.Contains(name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedRegistryInitializationAndRepeatedShutdownVerifyAndExecute(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import * as child from 'child_process';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var runtimeType = assembly.GetType("$Runtime")!;
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var registry = Assert.IsType<System.Collections.Concurrent.ConcurrentDictionary<int, System.Diagnostics.Process>>(
            runtimeType.GetField("_ownedChildProcesses", flags)!.GetValue(null));
        var stopping = runtimeType.GetField("_childProcessOwnershipStopping", flags)!;
        Assert.Empty(registry);
        Assert.Equal(0, stopping.GetValue(null));
        runtimeType.GetMethod("ChildProcessTerminateOwned")!.Invoke(null, null);
        runtimeType.GetMethod("ChildProcessTerminateOwned")!.Invoke(null, null);
        Assert.Equal(1, stopping.GetValue(null));
        Assert.Empty(registry);
        Assert.Same(registry, runtimeType.GetField("_ownedChildProcesses", flags)!.GetValue(null));
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import * as child from 'child_process';", false, emitter).RequireChildProcess();
        var minimal = EmitRuntime("console.log(1);", false, emitter);
        var second = EmitRuntime("import * as child from 'child_process';", false, emitter).RequireChildProcess();
        Assert.Null(minimal.ChildProcess);
        Assert.NotSame(first, second);
        Assert.NotSame(first.ContextType, second.ContextType);
        Assert.NotSame(first.PushCtor, second.PushCtor);
        Assert.NotSame(first.OwnedProcessesField, second.OwnedProcessesField);
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static void FillDeclarations(EmittedChildProcessRuntime child, string? missingHandle = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"child_process_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = typeof(Type).IsAssignableFrom(property.PropertyType) ? type
                : typeof(ConstructorInfo).IsAssignableFrom(property.PropertyType) ? ctor
                : typeof(FieldInfo).IsAssignableFrom(property.PropertyType) ? field : method;
            property.SetValue(child, handle);
        }
    }

    private static void AssertFrozen(EmittedChildProcessRuntime child)
    {
        Assert.True(child.IsComplete);
        Assert.Throws<InvalidOperationException>(child.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(child);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(child, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"child_process_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
