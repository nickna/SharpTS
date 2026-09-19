using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;
using static SharpTS.Tests.CompilerTests.RuntimeEmissionTestHelpers;

namespace SharpTS.Tests.CompilerTests;

public class EmittedAbortRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedAbortRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));
    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var abort = CreateDeclarations(missingHandle);
        var property = typeof(EmittedAbortRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(abort));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(abort.CompleteEmission).Message);
        Assert.False(abort.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(abort, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(abort, property.GetValue(CreateDeclarations()));
        abort.CompleteEmission();
        AssertFrozen(abort);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.Abort);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireAbort).Message);
        runtime.BeginAbortEmission();
        var abort = runtime.RequireAbort();
        Assert.Same(runtime.Abort, abort);
        Assert.Throws<InvalidOperationException>(runtime.BeginAbortEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Abort))!.SetMethod!.IsPrivate);
        var declarations = CreateDeclarations();
        foreach (var property in Handles) property.SetValue(abort, property.GetValue(declarations));
        abort.CompleteEmission();
        AssertFrozen(abort);
        Assert.Throws<InvalidOperationException>(runtime.BeginAbortEmission);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamespaceFieldEventAndHelpersPrecedeLateNamespacePopulation(bool usesAny)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("abort_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var type = module.DefineType("Runtime", TypeAttributes.Public);
        var function = module.DefineType("Function", TypeAttributes.Public);
        var boundFunction = module.DefineType("BoundFunction", TypeAttributes.Public);
        static MethodBuilder InvokeStub(TypeBuilder owner)
        {
            var method = owner.DefineMethod("Invoke", MethodAttributes.Public, typeof(object), [typeof(object[])]);
            method.GetILGenerator().Emit(OpCodes.Ldnull);
            method.GetILGenerator().Emit(OpCodes.Ret);
            return method;
        }
        var functionInvoke = InvokeStub(function);
        var boundInvoke = InvokeStub(boundFunction);
        function.CreateType();
        boundFunction.CreateType();
        var getOrCreate = type.DefineMethod("GetOrCreate", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(MethodInfo), typeof(string), typeof(int)]);
        getOrCreate.GetILGenerator().Emit(OpCodes.Ldnull);
        getOrCreate.GetILGenerator().Emit(OpCodes.Ret);
        var runtime = new EmittedRuntime();
        runtime.BeginAbortEmission();
        var abort = runtime.RequireAbort();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var source = usesAny ? "AbortSignal.any([]);" : "new AbortController();";
        typeof(RuntimeEmitter).GetField("_features", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(emitter, Detect(source));
        void Emit(string name, params object[] args) => typeof(RuntimeEmitter)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(emitter, args);

        Emit("DefineNamespaceSingletonFields", type, abort, null!);
        Assert.Equal("_AbortSignalNamespace", abort.NamespaceField.Name);
        Assert.Throws<InvalidOperationException>(() => abort.FireEvent);
        Assert.Throws<InvalidOperationException>(() => abort.NamespacePopulate);
        Emit("EmitFireAbortEvent", type, abort, function, functionInvoke, boundFunction, boundInvoke);
        Assert.True(abort.FireEvent.GetILGenerator().ILOffset > 0);
        Assert.Throws<InvalidOperationException>(() => abort.CreateController);
        var requirements = new List<string>();
        Action<string> requireRuntime = reason =>
        {
            Assert.Equal("AbortSignal.any", reason);
            Assert.NotNull(abort.SignalTimeout);
            Assert.Throws<InvalidOperationException>(() => abort.SignalAny);
            Assert.False(abort.IsComplete);
            runtime.Deployment.Require(reason);
            requirements.Add(reason);
        };
        Emit("EmitAbortControllerMethods", type, abort, requireRuntime, usesAny);
        Assert.Equal(usesAny ? 1 : 0, requirements.Count);
        Assert.Equal(usesAny, runtime.Deployment.Reasons.Contains("AbortSignal.any"));
        Assert.Throws<InvalidOperationException>(abort.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => abort.NamespacePopulate);
        Assert.False(abort.IsComplete);
        Emit("EmitNamespaceSingletons", type, abort, null!, getOrCreate);
        Assert.True(abort.NamespacePopulate.GetILGenerator().ILOffset > 0);
        Assert.False(type.IsCreated());
        type.CreateType();
        abort.CompleteEmission();
        AssertFrozen(abort);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    [Theory]
    [InlineData("const value=1;", false, false, false)]
    [InlineData("Buffer.from('only');", false, false, false)]
    [InlineData("import * as stream from 'stream';", false, false, false)]
    [InlineData("new AbortController();", true, false, false)]
    [InlineData("AbortSignal.abort('stop');", true, false, false)]
    [InlineData("AbortSignal.timeout(10);", true, false, false)]
    [InlineData("AbortSignal.any([]);", true, true, false)]
    [InlineData("const A=AbortSignal; A.abort('stop');", true, false, false)]
    [InlineData("const A=AbortSignal; A.any([]);", true, false, false)]
    [InlineData("new ReadableStream();", true, false, false)]
    [InlineData("fetch('unused');", true, false, false)]
    [InlineData("import * as http from 'http';", true, false, false)]
    [InlineData("const value=1;", false, false, true)]
    [InlineData("new AbortController();", true, false, true)]
    [InlineData(null, true, true, false)]
    [InlineData(null, true, true, true)]
    public void FeatureGatesPreserveImpliedAvailabilityAndPreciseAnyRequirement(string? source, bool enabled, bool requiresRuntime, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.Abort is not null);
        Assert.Equal(requiresRuntime, runtime.Deployment.Reasons.Contains("AbortSignal.any"));
        if (enabled)
        {
            AssertFrozen(runtime.RequireAbort());
            Assert.Equal(19, Handles.Count());
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireAbort);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var fields = reader.FieldDefinitions.Select(handle => reader.GetString(reader.GetFieldDefinition(handle).Name));
        Assert.Equal(enabled, fields.Contains("_AbortSignalNamespace"));
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name));
        Assert.Equal(enabled ? 18 : 0, methods.Count(name => name == "FireAbortEvent" || name == "CreateAbortController" ||
            name.StartsWith("AbortController", StringComparison.Ordinal) || name.StartsWith("AbortSignal", StringComparison.Ordinal) ||
            name == "_AbortSignalNamespacePopulate"));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedHelpersVerifyAndPreserveStateWrappersNamespaceAndCancellation(bool hosted)
    {
        using var bytes = Save(EmitRuntime("new AbortController();", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        object? Call(string name, params object?[] args) => type.GetMethod(name)!.Invoke(null, args);
        var controller = Assert.IsType<Dictionary<string, object?>>(Call("CreateAbortController"));
        using var controllerCts = Assert.IsType<CancellationTokenSource>(controller["_cts"]);
        var signal = Assert.IsType<Dictionary<string, object?>>(Call("AbortControllerGetSignal", controller));
        Assert.Same(signal, Call("AbortControllerGetSignal", controller));
        Assert.Equal(false, Call("AbortSignalGetAborted", signal));
        Assert.Null(Call("AbortSignalGetReason", signal));
        Assert.Null(Call("AbortSignalGetOnAbort", signal));
        var marker = new object();
        Call("AbortSignalSetOnAbort", signal, marker);
        Assert.Same(marker, Call("AbortSignalGetOnAbort", signal));
        Call("AbortSignalSetOnAbort", signal, null);
        var listeners = Assert.IsType<List<object?>>(signal["_listeners"]);
        Call("AbortSignalAddEventListenerThis", signal, "other", marker);
        Assert.Empty(listeners);
        Call("AbortSignalAddEventListenerThis", signal, "abort", marker);
        Assert.Same(marker, Assert.Single(listeners));
        Call("AbortSignalRemoveEventListenerThis", signal, "abort", marker);
        Assert.Empty(listeners);
        Assert.Null(Call("AbortSignalThrowIfAbortedThis", signal));
        Call("AbortControllerAbort", controller, "stop");
        Call("AbortControllerAbort", controller, "ignored");
        Assert.True(controllerCts.IsCancellationRequested);
        Assert.Equal(true, Call("AbortSignalGetAborted", signal));
        Assert.Equal("stop", Call("AbortSignalGetReason", signal));
        var error = Assert.Throws<TargetInvocationException>(() => Call("AbortSignalThrowIfAbortedThis", signal));
        Assert.Contains("stop", error.GetBaseException().Message);
        var already = Assert.IsType<Dictionary<string, object?>>(Call("AbortSignalAbort", "already"));
        using var alreadyCts = Assert.IsType<CancellationTokenSource>(already["_cts"]);
        Assert.Equal(true, Call("AbortSignalGetAborted", already));
        Assert.Equal("already", Call("AbortSignalGetReason", already));
        var combined = Assert.IsType<Dictionary<string, object?>>(Call("AbortSignalAny", new List<object?> { signal }));
        using var combinedCts = Assert.IsType<CancellationTokenSource>(combined["_cts"]);
        Assert.Equal(true, Call("AbortSignalGetAborted", combined));
        Assert.Equal("stop", Call("AbortSignalGetReason", combined));
        var timeout = Assert.IsType<Dictionary<string, object?>>(Call("AbortSignalTimeout", 1d));
        using var timeoutCts = Assert.IsType<CancellationTokenSource>(timeout["_cts"]);
        Assert.True(timeoutCts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal(true, Call("AbortSignalGetAborted", timeout));
        Assert.Contains("TimeoutError", Assert.IsType<string>(Call("AbortSignalGetReason", timeout)));
        Call("_AbortSignalNamespacePopulate");
        var ns = Assert.IsType<Dictionary<string, object?>>(type.GetField("_AbortSignalNamespace")!.GetValue(null));
        Assert.Equal(new[] { "abort", "any", "timeout" }, ns.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.All(ns.Values, Assert.NotNull);
        Call("_AbortSignalNamespacePopulate");
        Assert.Same(ns, type.GetField("_AbortSignalNamespace")!.GetValue(null));
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("new AbortController();", false, emitter).RequireAbort();
        var minimal = EmitRuntime("const value=1;", false, emitter);
        var second = EmitRuntime("AbortSignal.abort();", false, emitter).RequireAbort();
        Assert.Null(minimal.Abort);
        Assert.NotSame(first, second);
        foreach (var property in Handles) Assert.NotSame(property.GetValue(first), property.GetValue(second));
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static EmittedAbortRuntime CreateDeclarations(string? missingHandle = null)
    {
        var abort = new EmittedAbortRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"abort_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
            property.SetValue(abort, property.PropertyType == typeof(FieldBuilder) ? field : method);
        return abort;
    }

    private static void AssertFrozen(EmittedAbortRuntime abort)
    {
        Assert.True(abort.IsComplete);
        Assert.Throws<InvalidOperationException>(abort.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(abort);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(abort, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"abort_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
