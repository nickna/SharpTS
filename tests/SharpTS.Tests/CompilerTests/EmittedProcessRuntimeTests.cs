using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedProcessRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => new[]
    {
        ("required", typeof(EmittedProcessRuntime)),
        ("streams", typeof(EmittedProcessStreamRuntime)),
        ("hosted", typeof(EmittedHostedProcessRuntime))
    }.SelectMany(group => Handles(group.Item2).Select(property => new object[] { group.Item1, property.Name }));

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationsRejectReadsAndCompletionWithoutFreezingEitherOptionalGroup(string group, string missingHandle)
    {
        var process = new EmittedProcessRuntime();
        process.BeginStreamsEmission();
        process.BeginHostedEmission();
        object missingOwner = group switch
        {
            "streams" => process.RequireStreams(),
            "hosted" => process.RequireHosted(),
            _ => process
        };
        foreach (var component in new object[] { process, process.RequireStreams(), process.RequireHosted() })
            FillDeclarations(component, ReferenceEquals(component, missingOwner) ? missingHandle : null);

        var property = missingOwner.GetType().GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(missingOwner));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(process.CompleteEmission).Message);
        Assert.False(process.IsComplete);
        Assert.False(process.RequireStreams().IsComplete);
        Assert.False(process.RequireHosted().IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(missingOwner, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);

        // A failed completion must leave every enabled group writable for retry.
        FillDeclarations(process);
        FillDeclarations(process.RequireStreams());
        FillDeclarations(process.RequireHosted());
        process.CompleteEmission();
        AssertAllFrozen(process);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RequiredOwnerAndOptionalGroupsHaveExplicitAvailabilityAndCannotBeReplaced(bool streams, bool hosted)
    {
        var runtime = new EmittedRuntime();
        var process = runtime.Process;
        Assert.NotNull(process);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Process))!.SetMethod);
        Assert.Null(process.Streams);
        Assert.Null(process.Hosted);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(process.RequireStreams).Message);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(process.RequireHosted).Message);
        if (streams)
        {
            process.BeginStreamsEmission();
            Assert.Same(process.Streams, process.RequireStreams());
            Assert.Throws<InvalidOperationException>(process.BeginStreamsEmission);
            FillDeclarations(process.RequireStreams());
        }
        if (hosted)
        {
            process.BeginHostedEmission();
            Assert.Same(process.Hosted, process.RequireHosted());
            Assert.Throws<InvalidOperationException>(process.BeginHostedEmission);
            FillDeclarations(process.RequireHosted());
        }
        Assert.True(typeof(EmittedProcessRuntime).GetProperty(nameof(EmittedProcessRuntime.Streams))!.SetMethod!.IsPrivate);
        Assert.True(typeof(EmittedProcessRuntime).GetProperty(nameof(EmittedProcessRuntime.Hosted))!.SetMethod!.IsPrivate);
        FillDeclarations(process);
        process.CompleteEmission();
        AssertAllFrozen(process);
        Assert.Throws<InvalidOperationException>(process.BeginStreamsEmission);
        Assert.Throws<InvalidOperationException>(process.BeginHostedEmission);
    }

    [Fact]
    public void ForwardCallsCanUseProcessDeclarationsBeforeTheirBodiesExist()
    {
        var process = new EmittedProcessRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("process_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        process.GetObject = type.DefineMethod("Declared", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, process.GetObject);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(process.IsComplete);
        process.GetObject.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        process.GetObject.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        Assert.Equal(true, Assembly.Load(bytes.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(1);", false, false)]
    [InlineData("console.log(1);", false, true)]
    [InlineData("console.log(process.env);", false, false)]
    [InlineData("console.log(process.env);", false, true)]
    [InlineData("import process from 'process';", false, false)]
    [InlineData("import process from 'process';", false, true)]
    [InlineData("process.stdout.write('data');", true, false)]
    [InlineData("process.stdout.write('data');", true, true)]
    [InlineData("Promise.resolve(1);", false, false)]
    [InlineData("Promise.resolve(1);", false, true)]
    [InlineData("Buffer.from('data');", false, false)]
    [InlineData("Buffer.from('data');", false, true)]
    [InlineData("new ReadableStream();", false, false)]
    [InlineData("new ReadableStream();", false, true)]
    [InlineData("import * as fs from 'fs';", true, false)]
    [InlineData("import * as fs from 'fs';", true, true)]
    [InlineData("import * as stream from 'stream';", true, false)]
    [InlineData("import * as stream from 'stream';", true, true)]
    [InlineData("import * as http from 'http';", true, false)]
    [InlineData("import * as http from 'http';", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void MinimalEnabledImpliedHostedAndFullEmissionKeepProcessRequiredAndOptionalGroupsGated(string? source, bool streams, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var process = runtime.Process;
        Assert.Equal(streams, process.Streams is not null);
        Assert.Equal(hosted, process.Hosted is not null);
        AssertAllFrozen(process);
        Assert.Equal(70, Handles(typeof(EmittedProcessRuntime)).Count());
        Assert.Equal(6, Handles(typeof(EmittedProcessStreamRuntime)).Count());
        Assert.Equal(2, Handles(typeof(EmittedHostedProcessRuntime)).Count());
        Assert.Same(runtime.RuntimeClass.Type, process.GetObject.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, process.UptimeBaselineField.DeclaringType);
        Assert.Equal("$Process", process.GetInstance.DeclaringType!.Name);
        Assert.Same(process.GetInstance.DeclaringType, process.FieldsField.DeclaringType);
        Assert.Equal("$ProcessEmitClosure", process.EmitClosureCtor.DeclaringType!.Name);
        Assert.Same(process.EmitClosureCtor.DeclaringType, process.EmitClosureInvoke.DeclaringType);
        foreach (var component in Components(process))
        foreach (var property in Handles(component.GetType()))
        {
            var member = (MemberInfo)property.GetValue(component)!;
            Assert.True(((TypeBuilder)member.DeclaringType!).IsCreated(), property.Name);
        }
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        Assert.Contains("$Process", types);
        Assert.Contains("$ProcessEmitClosure", types);
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "GetStdout", "GetStderr", "GetStdin" }) Assert.Equal(streams, methods.Contains(name));
        foreach (var name in new[] { "ProcessEmitHostedBeforeExit", "ProcessEmitHostedExit" }) Assert.Equal(hosted, methods.Contains(name));
        var fields = reader.FieldDefinitions.Select(handle => reader.GetString(reader.GetFieldDefinition(handle).Name)).ToArray();
        foreach (var name in new[] { "_stdoutInstance", "_stderrInstance", "_stdinInstance" }) Assert.Equal(streams, fields.Contains(name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ProcessSingletonStreamsNullFallbackAndHostedLifecycleVerifyAndExecute(bool streams, bool hosted)
    {
        var runtime = EmitRuntime(streams ? "process.stdout.write('data');" : "console.log(1);", hosted);
        using var bytes = Save(runtime);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var runtimeType = assembly.GetType("$Runtime")!;
        object? Call(string name, params object?[] args) => runtimeType.GetMethod(name)!.Invoke(null, args);
        var process = Call("GetProcessObject")!;
        Assert.Same(process, Call("GetProcessObject"));
        Assert.Same(process, Call("GetProcessEventEmitter"));
        var type = assembly.GetType("$Process")!;
        Assert.Same(assembly.GetType("$EventEmitter"), type.BaseType);
        var firstUptime = Assert.IsType<double>(Call("ProcessUptime"));
        Assert.True(firstUptime >= 0);
        Assert.True(Assert.IsType<double>(Call("ProcessUptime")) >= firstUptime);
        foreach (var stream in new[] { ("Stdout", "$Writable"), ("Stderr", "$Writable"), ("Stdin", "$Readable") })
        {
            var value = type.GetProperty(stream.Item1)!.GetValue(process);
            if (streams)
            {
                Assert.Equal(stream.Item2, value!.GetType().Name);
                Assert.Same(value, Call("Get" + stream.Item1));
                Assert.Same(value, type.GetProperty(stream.Item1)!.GetValue(process));
                var field = runtimeType.GetField("_" + stream.Item1.ToLowerInvariant() + "Instance", BindingFlags.NonPublic | BindingFlags.Static)!;
                Assert.Same(value, field.GetValue(null));
            }
            else Assert.Null(value);
        }
        Assert.Same(Call("ProcessGetHrtimeFn"), Call("ProcessGetHrtimeFn"));
        Assert.Same(Call("ProcessGetMemoryUsageFn"), Call("ProcessGetMemoryUsageFn"));
        if (hosted)
        {
            var collector = new LifecycleCollector();
            var callback = Activator.CreateInstance(assembly.GetType("$TSFunction")!,
                [collector, typeof(LifecycleCollector).GetMethod(nameof(LifecycleCollector.Record))]);
            type.GetMethod("On")!.Invoke(process, ["beforeExit", callback]);
            type.GetMethod("On")!.Invoke(process, ["exit", callback]);
            Call("ProcessEmitHostedBeforeExit", 7);
            Call("ProcessEmitHostedExit", 9);
            Assert.Equal(new object[] { 7.0, 9.0 }, collector.Codes);
        }
    }

    [Fact]
    public void ReusingEmitterKeepsProcessAndOptionalMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("process.stdout.write('data');", false, emitter).Process;
        var minimal = EmitRuntime("console.log(1);", false, emitter).Process;
        var second = EmitRuntime("process.stdout.write('data');", false, emitter).Process;
        Assert.Null(minimal.Streams);
        Assert.NotSame(first, second);
        Assert.NotSame(first.GetObject, second.GetObject);
        Assert.NotSame(first.RequireStreams(), second.RequireStreams());
        Assert.NotSame(first.RequireStreams().StdoutInstance, second.RequireStreams().StdoutInstance);
        AssertAllFrozen(first);
        AssertAllFrozen(minimal);
        AssertAllFrozen(second);
    }

    public sealed class LifecycleCollector
    {
        public List<object?> Codes { get; } = [];
        public object? Record(object? code)
        {
            Codes.Add(code);
            return null;
        }
    }

    private static IEnumerable<object> Components(EmittedProcessRuntime process)
    {
        yield return process;
        if (process.Streams is not null) yield return process.Streams;
        if (process.Hosted is not null) yield return process.Hosted;
    }

    private static void FillDeclarations(object component, string? missingHandle = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"process_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles(component.GetType()).Where(property => property.Name != missingHandle))
        {
            object handle = typeof(ConstructorInfo).IsAssignableFrom(property.PropertyType) ? ctor
                : typeof(FieldInfo).IsAssignableFrom(property.PropertyType) ? field : method;
            property.SetValue(component, handle);
        }
    }

    private static void AssertAllFrozen(EmittedProcessRuntime process)
    {
        Assert.True(process.IsComplete);
        Assert.Throws<InvalidOperationException>(process.CompleteEmission);
        foreach (var component in Components(process))
        {
            Assert.Equal(true, component.GetType().GetProperty("IsComplete")!.GetValue(component));
            foreach (var property in Handles(component.GetType()))
            {
                var value = property.GetValue(component);
                Assert.NotNull(value);
                Assert.False(property.SetMethod!.IsPublic);
                var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, value));
                Assert.IsType<InvalidOperationException>(error.InnerException);
            }
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"process_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
