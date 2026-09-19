using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;
using static SharpTS.Tests.CompilerTests.RuntimeEmissionTestHelpers;

namespace SharpTS.Tests.CompilerTests;

public class EmittedClusterRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedClusterRuntime).GetProperties()
        .Where(property => property.PropertyType == typeof(MethodBuilder));

    [Theory]
    [InlineData(nameof(EmittedClusterRuntime.Fork))]
    [InlineData(nameof(EmittedClusterRuntime.Invoke))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var component = CreateDeclarations(missingHandle);
        var property = typeof(EmittedClusterRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(component));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(component.CompleteEmission).Message);
        Assert.False(component.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(component, property.GetValue(CreateDeclarations()));
        component.CompleteEmission();
        AssertFrozen(component);
    }

    [Fact]
    public void OptionalOwnerHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.Cluster);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireCluster).Message);
        runtime.BeginClusterEmission();
        Assert.Same(runtime.Cluster, runtime.RequireCluster());
        Assert.Throws<InvalidOperationException>(runtime.BeginClusterEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Cluster))!.SetMethod!.IsPrivate);
        var declarations = CreateDeclarations();
        foreach (var property in Handles)
            property.SetValue(runtime.RequireCluster(), property.GetValue(declarations));
        runtime.RequireCluster().CompleteEmission();
        AssertFrozen(runtime.RequireCluster());
        Assert.Throws<InvalidOperationException>(runtime.BeginClusterEmission);
    }

    [Fact]
    public void BodiesAreAvailableBeforeRuntimeTypeAndOwnerCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("cluster_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime) { EntryModulePath = "unused-emitter-path.ts" };
        var runtime = emitter.EmitAll(module, Detect("const value=1;"));
        var helpers = module.DefineType("SecondCluster", TypeAttributes.Public);
        var component = new EmittedClusterRuntime();
        emitter.EmitClusterHelpers(helpers, component, runtime.EventLoop, "explicit-entry.ts");
        foreach (var property in Handles)
        {
            var method = Assert.IsAssignableFrom<MethodBuilder>(property.GetValue(component));
            Assert.Same(helpers, method.DeclaringType);
            Assert.True(method.GetILGenerator().ILOffset > 0);
            Assert.Equal("Cluster" + property.Name, method.Name);
        }
        Assert.False(helpers.IsCreated());
        Assert.False(component.IsComplete);
        helpers.CreateType();
        component.CompleteEmission();
        AssertFrozen(component);
        using var bytes = Save(runtime);
        Verify(bytes);
        var methodInfo = Assembly.Load(bytes.ToArray()).GetType("SecondCluster")!.GetMethod("ClusterFork")!;
        Assert.Contains("explicit-entry.ts", StringOperands(methodInfo));
        Assert.DoesNotContain("unused-emitter-path.ts", StringOperands(methodInfo));
    }

    [Theory]
    [InlineData("const value=1;", false, false)]
    [InlineData("const value=1;", true, false)]
    [InlineData("import 'cluster';", false, true)]
    [InlineData("import * as cluster from 'node:cluster';", false, true)]
    [InlineData("import {fork as start} from 'cluster';", false, true)]
    [InlineData("const cluster=require('cluster');", false, true)]
    [InlineData("const cluster=require('node:cluster');", false, true)]
    [InlineData("import 'worker_threads';", false, false)]
    [InlineData("import 'child_process';", false, false)]
    [InlineData("import 'cluster';", true, true)]
    [InlineData(null, false, true)]
    [InlineData(null, true, true)]
    public void AvailabilityPreservesClusterGateAndAssemblyDependencies(string? source, bool hosted, bool enabled)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.Cluster is not null);
        if (enabled) AssertFrozen(runtime.RequireCluster());
        else Assert.Throws<InvalidOperationException>(runtime.RequireCluster);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var property in Handles)
            Assert.Equal(enabled, methods.Contains("Cluster" + property.Name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        // Emitting declarations alone does not record a call site's soft dependency.
        if (source == "import 'cluster';") Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedHelpersVerifyAndPreserveNullEntryFailureWithoutStartingWorkers(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import 'cluster';", hosted));
        Verify(bytes);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var fork = type.GetMethod("ClusterFork")!;
        var invoke = type.GetMethod("ClusterInvoke")!;
        Assert.Equal(typeof(object), fork.ReturnType);
        Assert.Equal(new[] { typeof(object) }, fork.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(object), invoke.ReturnType);
        Assert.Equal(new[] { typeof(string), typeof(object[]) }, invoke.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Contains("SharpTS.Runtime.Types.ClusterCompiledBridge, SharpTS", StringOperands(fork));
        Assert.Contains("SharpTS.Runtime.Types.ClusterCompiledBridge, SharpTS", StringOperands(invoke));
        var error = Assert.Throws<TargetInvocationException>(() => fork.Invoke(null, [null]));
        Assert.Equal("cluster.fork() cannot determine the entry script for this compiled program.", error.GetBaseException().Message);
    }

    [Fact]
    public void ReusingEmitterPreservesPerCompilationOwnersAndExactEntryConfiguration()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), "cluster-first-entry.ts");
        var secondPath = Path.Combine(Path.GetTempPath(), "cluster-second-entry.ts");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime) { EntryModulePath = firstPath };
        var first = EmitRuntime("import 'cluster';", false, emitter);
        var minimal = EmitRuntime("const value=1;", false, emitter);
        emitter.EntryModulePath = secondPath;
        var second = EmitRuntime("import 'cluster';", false, emitter);
        emitter.EntryModulePath = null;
        var noPath = EmitRuntime("import 'cluster';", false, emitter);
        Assert.Null(minimal.Cluster);
        Assert.NotSame(first.RequireCluster(), second.RequireCluster());
        Assert.NotSame(first.RequireCluster().Fork, second.RequireCluster().Fork);
        Assert.NotSame(first.RequireCluster().Invoke, second.RequireCluster().Invoke);
        foreach (var (runtime, expected, absent) in new[] { (first, firstPath, secondPath), (second, secondPath, firstPath) })
        {
            using var bytes = Save(runtime);
            var method = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!.GetMethod("ClusterFork")!;
            Assert.Contains(expected, StringOperands(method));
            Assert.DoesNotContain(absent, StringOperands(method));
            AssertFrozen(runtime.RequireCluster());
        }
        using var nullBytes = Save(noPath);
        var nullMethod = Assembly.Load(nullBytes.ToArray()).GetType("$Runtime")!.GetMethod("ClusterFork")!;
        Assert.DoesNotContain(firstPath, StringOperands(nullMethod));
        Assert.DoesNotContain(secondPath, StringOperands(nullMethod));
        AssertFrozen(noPath.RequireCluster());
    }

    private static IReadOnlyList<string> StringOperands(MethodInfo method)
    {
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => unchecked((ushort)code.Value));
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var strings = new List<string>();
        for (int offset = 0; offset < bytes.Length;)
        {
            ushort value = bytes[offset++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
            var code = codes[value];
            if (code == OpCodes.Ldstr) strings.Add(method.Module.ResolveString(BitConverter.ToInt32(bytes, offset)));
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
        }
        return strings;
    }

    private static EmittedClusterRuntime CreateDeclarations(string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"cluster_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var component = new EmittedClusterRuntime();
        foreach (var property in Handles.Where(property => property.Name != omitted))
        {
            var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            method.GetILGenerator().Emit(OpCodes.Ret);
            property.SetValue(component, method);
        }
        return component;
    }

    private static void AssertFrozen(EmittedClusterRuntime component)
    {
        Assert.True(component.IsComplete);
        Assert.Throws<InvalidOperationException>(component.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(component);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static void Verify(MemoryStream bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"cluster_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
