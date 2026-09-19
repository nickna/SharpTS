using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedAtomicsRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedAtomicsRuntime).GetProperties()
        .Where(property => property.PropertyType == typeof(MethodBuilder));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var component = CreateDeclarations(missingHandle);
        var property = typeof(EmittedAtomicsRuntime).GetProperty(missingHandle)!;
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
        Assert.Null(runtime.Atomics);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireAtomics).Message);
        runtime.BeginAtomicsEmission();
        Assert.Same(runtime.Atomics, runtime.RequireAtomics());
        Assert.Throws<InvalidOperationException>(runtime.BeginAtomicsEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Atomics))!.SetMethod!.IsPrivate);
        var complete = CreateDeclarations();
        foreach (var property in Handles)
            property.SetValue(runtime.RequireAtomics(), property.GetValue(complete));
        runtime.RequireAtomics().CompleteEmission();
        AssertFrozen(runtime.RequireAtomics());
        Assert.Throws<InvalidOperationException>(runtime.BeginAtomicsEmission);
    }

    [Fact]
    public void StagedBodiesUseScopedHelpersBeforeRuntimeTypeCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("atomics_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, Detect("new Int32Array(new SharedArrayBuffer(4));"));
        var helpers = module.DefineType("SecondAtomics", TypeAttributes.Public);
        var component = new EmittedAtomicsRuntime();
        typeof(RuntimeEmitter).GetMethod("EmitAtomicsHelpersPure", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [helpers, component, runtime.TypedArrays.RequireImplementation(),
                runtime.Sentinels.UndefinedType, runtime.Sentinels.UndefinedInstance, runtime.Errors.TypeErrorConstructor, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor]);
        foreach (var property in Handles)
        {
            var method = Assert.IsAssignableFrom<MethodBuilder>(property.GetValue(component));
            Assert.Same(helpers, method.DeclaringType);
            Assert.True(method.GetILGenerator().ILOffset > 0);
            Assert.Equal("Atomics" + property.Name, method.Name);
        }
        Assert.False(helpers.IsCreated());
        Assert.False(component.IsComplete);
        helpers.CreateType();
        component.CompleteEmission();
        AssertFrozen(component);
        using var bytes = Save(runtime);
        Verify(bytes);
        var loaded = Assembly.Load(bytes.ToArray());
        var view = NewView(loaded, "$Int32Array", 1);
        Assert.Equal(0d, loaded.GetType("SecondAtomics")!.GetMethod("AtomicsAddInt32")!.Invoke(null, [view, 0, 3d, false]));
        Assert.Equal(3d, Call(loaded, "Load", view, 0d));
    }

    [Theory]
    [InlineData("const value=1;", false, false)]
    [InlineData("const value=1;", true, false)]
    [InlineData("Atomics.pause();", false, true)]
    [InlineData("Atomics.pause();", true, true)]
    [InlineData("new Int32Array(1);", false, true)]
    [InlineData("new Uint8Array(1);", false, true)]
    [InlineData("new ArrayBuffer(4);", false, true)]
    [InlineData("new SharedArrayBuffer(4);", false, true)]
    [InlineData("new DataView(new ArrayBuffer(4));", false, true)]
    [InlineData("import 'worker_threads';", false, false)]
    [InlineData("const workers=require('worker_threads');", false, false)]
    [InlineData("const pause=Atomics.pause;", false, true)]
    [InlineData(null, false, true)]
    [InlineData(null, true, true)]
    public void AvailabilityPreservesTheExistingTypedArrayGate(string? source, bool hosted, bool enabled)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.Atomics is not null);
        Assert.Equal(enabled, runtime.TypedArrays.Implementation is not null);
        if (enabled) AssertFrozen(runtime.RequireAtomics());
        else Assert.Throws<InvalidOperationException>(runtime.RequireAtomics);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var property in Handles)
            Assert.Equal(enabled, methods.Contains("Atomics" + property.Name));
        foreach (var suffix in new[] { "LoadLocked", "StoreLocked", "UpdateLocked", "UpdateInt32", "ConvertInt32Operand" })
            Assert.Equal(enabled, methods.Contains("Atomics" + suffix));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedOperationsPreservePrivateHelpersInliningErrorsAndCurrentWaitSemantics(bool hosted)
    {
        var runtime = EmitRuntime("Atomics.pause();new Int32Array(1);new Uint32Array(1);", hosted);
        using var bytes = Save(runtime);
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$Runtime")!;
        foreach (var suffix in new[] { "LoadLocked", "StoreLocked", "UpdateLocked", "UpdateInt32", "ConvertInt32Operand" })
            Assert.True(type.GetMethod("Atomics" + suffix, BindingFlags.NonPublic | BindingFlags.Static)!.IsPrivate);
        foreach (var suffix in new[] { "AddInt32", "IncrementInt32Discarded", "UpdateInt32", "ConvertInt32Operand" })
            Assert.True(type.GetMethod("Atomics" + suffix, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
                .GetMethodImplementationFlags().HasFlag(MethodImplAttributes.AggressiveInlining));
        var signed = NewView(assembly, "$Int32Array", 1);
        Assert.Equal(0d, Call(assembly, "AddInt32", signed, 0, 5000000000d, false));
        Assert.Equal(705032704d, Call(assembly, "Load", signed, 0d));
        Call(assembly, "IncrementInt32Discarded", signed, 0);
        Assert.Equal(705032705d, Call(assembly, "Exchange", signed, 0d, double.NaN));
        Assert.Equal(0d, Call(assembly, "CompareExchange", signed, 0d, double.PositiveInfinity, 7d));
        Assert.Equal("ok", Call(assembly, "Wait", signed, 0d, 7d, 0d));
        Assert.Equal("not-equal", Call(assembly, "Wait", signed, 0d, 8d, 0d));
        Assert.Equal(0d, Call(assembly, "Notify", signed, 0d, 1d));
        Assert.Equal(false, Call(assembly, "IsLockFree", 3d));
        Assert.Equal(true, Call(assembly, "IsLockFree", 4d));
        AssertGuestError(assembly, "$RangeError", () => Call(assembly, "AddInt32", signed, 1, 1d, false));
        AssertGuestError(assembly, "$TypeError", () => Call(assembly, "Pause", 1.5d));
        var undefined = assembly.GetType(runtime.Sentinels.UndefinedInstance.DeclaringType!.FullName!)!
            .GetField(runtime.Sentinels.UndefinedInstance.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        Assert.Same(undefined, Call(assembly, "Pause", undefined));
        Assert.Same(undefined, Call(assembly, "Pause", -1d));
        var unsigned = NewView(assembly, "$Uint32Array", 1);
        Assert.Equal(0d, Call(assembly, "AddInt32", unsigned, 0, -1d, true));
        Assert.Equal(4294967295d, Call(assembly, "Load", unsigned, 0d));
    }

    [Theory]
    [InlineData("$Int16Array", false)]
    [InlineData("$Int32Array", true)]
    public async Task SharedBackingStorageRemainsAtomicAcrossCompiledAssemblies(string kind, bool optimized)
    {
        using var bytes = Save(EmitRuntime("new Int16Array(1);new Int32Array(1);new SharedArrayBuffer(4);", false));
        Verify(bytes);
        var first = Assembly.Load(bytes.ToArray());
        var second = Assembly.Load(bytes.ToArray());
        var shared = first.GetType("$SharedArrayBuffer")!.GetConstructor([typeof(int)])!.Invoke([4]);
        object View(Assembly assembly) => assembly.GetType(kind)!.GetConstructor([typeof(object), typeof(int), typeof(int?)])!.Invoke([shared, 0, 1]);
        var firstView = View(first);
        var secondView = View(second);
        Assert.NotEqual(firstView.GetType(), secondView.GetType());
        Assert.Same(firstView.GetType().GetMethod("GetBuffer")!.Invoke(firstView, null),
            secondView.GetType().GetMethod("GetBuffer")!.Invoke(secondView, null));
        var tasks = new[] { (first, firstView), (second, secondView) }.Select(pair => Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                if (optimized) Call(pair.Item1, "AddInt32", pair.Item2, 0, 1d, false);
                else Call(pair.Item1, "Add", pair.Item2, 0d, 1d);
            }
        }));
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(400d, Call(first, "Load", firstView, 0d));
        Assert.Equal(400d, Call(second, "Load", secondView, 0d));
    }

    [Fact]
    public void ExplicitAndRootGuestErrorHelpersEmitIdenticalThrowBodies()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("atomics_error_helpers"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Detect("Atomics.pause();"));
        var helpers = module.DefineType("ErrorHelpers", TypeAttributes.Public);
        ILGenerator Body(string name) => helpers.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), Type.EmptyTypes).GetILGenerator();
        GuestErrorEmitter.ThrowTypeError(Body("RootType"), runtime, "invalid");
        GuestErrorEmitter.ThrowError(Body("ExplicitType"), runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor, "invalid");
        var root = Body("RootRange");
        root.Emit(OpCodes.Ldstr, "range");
        GuestErrorEmitter.ThrowErrorFromStack(root, runtime, runtime.Errors.RangeErrorConstructor);
        var explicitBody = Body("ExplicitRange");
        explicitBody.Emit(OpCodes.Ldstr, "range");
        GuestErrorEmitter.ThrowErrorFromStack(explicitBody, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor);
        helpers.CreateType();
        using var bytes = Save(runtime);
        Verify(bytes);
        var loaded = Assembly.Load(bytes.ToArray()).GetType("ErrorHelpers")!;
        foreach (var suffix in new[] { "Type", "Range" })
            Assert.Equal(loaded.GetMethod("Root" + suffix)!.GetMethodBody()!.GetILAsByteArray(),
                loaded.GetMethod("Explicit" + suffix)!.GetMethodBody()!.GetILAsByteArray());
    }

    [Fact]
    public void ReusingEmitterDoesNotLeakPrivateHelpersOrBackingState()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("new Int32Array(1);", false, emitter);
        var omitted = EmitRuntime("const value=1;", false, emitter);
        var second = EmitRuntime("new Int32Array(1);", false, emitter);
        Assert.Null(omitted.Atomics);
        Assert.NotSame(first.RequireAtomics(), second.RequireAtomics());
        foreach (var property in Handles)
            Assert.NotSame(property.GetValue(first.RequireAtomics()), property.GetValue(second.RequireAtomics()));
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        Verify(firstBytes);
        Verify(secondBytes);
        var firstAssembly = Assembly.Load(firstBytes.ToArray());
        var secondAssembly = Assembly.Load(secondBytes.ToArray());
        var firstView = NewView(firstAssembly, "$Int32Array", 1);
        var secondView = NewView(secondAssembly, "$Int32Array", 1);
        Call(firstAssembly, "AddInt32", firstView, 0, 7d, false);
        Assert.Equal(7d, Call(firstAssembly, "Load", firstView, 0d));
        Assert.Equal(0d, Call(secondAssembly, "Load", secondView, 0d));
        AssertFrozen(first.RequireAtomics());
        AssertFrozen(second.RequireAtomics());
    }

    private static object NewView(Assembly assembly, string kind, int length) =>
        assembly.GetType(kind)!.GetConstructor([typeof(int)])!.Invoke([length]);

    private static object? Call(Assembly assembly, string operation, params object?[] arguments) =>
        assembly.GetType("$Runtime")!.GetMethod("Atomics" + operation)!.Invoke(null, arguments);

    private static void AssertGuestError(Assembly assembly, string expectedType, Action action)
    {
        var error = Assert.Throws<TargetInvocationException>(action).InnerException!;
        var value = assembly.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [error]);
        Assert.Equal(expectedType, value!.GetType().Name);
    }

    private static EmittedAtomicsRuntime CreateDeclarations(string? missing = null)
    {
        var component = new EmittedAtomicsRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"atomics_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        foreach (var property in Handles.Where(property => property.Name != missing))
            property.SetValue(component, type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes));
        return component;
    }

    private static void AssertFrozen(EmittedAtomicsRuntime component)
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
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        bytes.Position = 0;
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector().Detect(
        new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"atomics_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
