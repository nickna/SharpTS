using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedHostPrimitiveRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(bool performance)
    {
        object component = performance ? new EmittedPerformanceRuntime() : new EmittedTtyRuntime();
        var property = Handle(component);
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(component));
        Assert.Contains($"'{property.Name}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{property.Name}'", Assert.Throws<InvalidOperationException>(() => Complete(component)).Message);
        Assert.False(IsComplete(component));
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var method = Placeholder();
        property.SetValue(component, method);
        Assert.Same(method, property.GetValue(component));
        Assert.False(((TypeBuilder)method.DeclaringType!).IsCreated());
        Complete(component);
        AssertFrozen(component);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalOwnersHaveIndependentAvailabilityAndCannotBeReplaced(bool performance)
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.Performance);
        Assert.Null(runtime.Tty);
        Action begin = performance ? runtime.BeginPerformanceEmission : runtime.BeginTtyEmission;
        Func<object> require = performance ? () => runtime.RequirePerformance() : () => runtime.RequireTty();
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(require).Message);
        begin();
        var component = require();
        Assert.Throws<InvalidOperationException>(begin);
        Assert.True(typeof(EmittedRuntime).GetProperty(performance ? "Performance" : "Tty")!.SetMethod!.IsPrivate);
        if (performance) Assert.Null(runtime.Tty);
        else Assert.Null(runtime.Performance);
        Handle(component).SetValue(component, Placeholder());
        Complete(component);
        AssertFrozen(component);
        Assert.Throws<InvalidOperationException>(begin);
    }

    [Fact]
    public void PrimitiveBodiesAreAvailableBeforeRuntimeTypeAndOwnerCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("host_primitives_staged"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Runtime", TypeAttributes.Public);
        var toNumber = type.DefineMethod("ToNumber", MethodAttributes.Public | MethodAttributes.Static, typeof(double), [typeof(object)]);
        toNumber.GetILGenerator().Emit(OpCodes.Ldc_R8, 0d);
        toNumber.GetILGenerator().Emit(OpCodes.Ret);
        var performance = new EmittedPerformanceRuntime();
        var tty = new EmittedTtyRuntime();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetMethod("EmitPerfPrimitiveMethods", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [type, performance]);
        Assert.Equal("PerfPrimitiveNow", performance.Now.Name);
        Assert.True(performance.Now.GetILGenerator().ILOffset > 0);
        Assert.Throws<InvalidOperationException>(() => tty.Isatty);
        emitter.EmitTtyPrimitiveMethods(type, tty, toNumber);
        Assert.Equal("Tty_isatty", tty.Isatty.Name);
        Assert.True(tty.Isatty.GetILGenerator().ILOffset > 0);
        Assert.False(type.IsCreated());
        Assert.False(performance.IsComplete);
        Assert.False(tty.IsComplete);
        type.CreateType();
        performance.CompleteEmission();
        tty.CompleteEmission();
        AssertFrozen(performance);
        AssertFrozen(tty);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    [Theory]
    [InlineData("const value=1;", false, false, false)]
    [InlineData("import {performance as clock} from 'perf_hooks';", true, false, false)]
    [InlineData("import * as perf from 'node:perf_hooks';", true, false, false)]
    [InlineData("performance.now();", true, false, false)]
    [InlineData("const perf=require('perf_hooks');", true, false, false)]
    [InlineData("import {isatty as check} from 'tty';", false, true, false)]
    [InlineData("import * as tty from 'node:tty';", false, true, false)]
    [InlineData("const tty=require('tty');", false, true, false)]
    [InlineData("const stream:any={};stream.isTTY;", false, true, false)]
    [InlineData("import 'perf_hooks';import 'tty';", true, true, false)]
    [InlineData("import {now} from 'primitive:perf';", false, false, false)]
    [InlineData("import {isatty} from 'primitive:tty';", false, false, false)]
    [InlineData("Math.abs(-1);", false, false, false)]
    [InlineData("const value=1;", false, false, true)]
    [InlineData("import 'perf_hooks';import 'tty';", true, true, true)]
    [InlineData(null, true, true, false)]
    [InlineData(null, true, true, true)]
    public void FeatureGatesPreserveIndependentDeclarationsAndStandaloneDependencies(string? source, bool performance, bool tty, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(performance, runtime.Performance is not null);
        Assert.Equal(tty, runtime.Tty is not null);
        if (performance) AssertFrozen(runtime.RequirePerformance());
        else Assert.Throws<InvalidOperationException>(runtime.RequirePerformance);
        if (tty) AssertFrozen(runtime.RequireTty());
        else Assert.Throws<InvalidOperationException>(runtime.RequireTty);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        var fields = reader.FieldDefinitions.Select(handle => reader.GetString(reader.GetFieldDefinition(handle).Name)).ToArray();
        Assert.Equal(performance, methods.Contains("PerfPrimitiveNow"));
        Assert.Equal(tty, methods.Contains("Tty_isatty"));
        foreach (var name in new[] { "_perfPrimitiveStartTicks", "_perfPrimitiveTicksPerMs", "_perfPrimitiveInitialized" })
            Assert.Equal(performance, fields.Contains(name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        if (source == "import 'perf_hooks';import 'tty';") Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedPrimitivesVerifyAndPreserveLazyClockAndDescriptorCoercion(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import 'perf_hooks';import 'tty';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$Runtime")!;
        var now = type.GetMethod("PerfPrimitiveNow")!;
        var initialized = type.GetField("_perfPrimitiveInitialized", BindingFlags.NonPublic | BindingFlags.Static)!;
        var start = type.GetField("_perfPrimitiveStartTicks", BindingFlags.NonPublic | BindingFlags.Static)!;
        var scale = type.GetField("_perfPrimitiveTicksPerMs", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(false, initialized.GetValue(null));
        Assert.Equal(0L, start.GetValue(null));
        Assert.Equal(0d, scale.GetValue(null));
        var first = Assert.IsType<double>(now.Invoke(null, null));
        var anchor = start.GetValue(null);
        var second = Assert.IsType<double>(now.Invoke(null, null));
        Assert.True(double.IsFinite(first) && first >= 0);
        Assert.True(double.IsFinite(second) && second >= first);
        Assert.Equal(true, initialized.GetValue(null));
        Assert.Equal(anchor, start.GetValue(null));
        Assert.Equal(Stopwatch.Frequency / 1000d, scale.GetValue(null));

        var isatty = type.GetMethod("Tty_isatty")!;
        (object? Value, bool Expected)[] cases =
        [
            (0d, !Console.IsInputRedirected), (1d, !Console.IsOutputRedirected), (2d, !Console.IsErrorRedirected),
            ("1", !Console.IsOutputRedirected), (null, !Console.IsInputRedirected),
            (false, !Console.IsInputRedirected), (true, !Console.IsOutputRedirected),
            (0.5d, !Console.IsInputRedirected), (1.5d, !Console.IsErrorRedirected),
            (999d, false), (-1d, false), (double.NaN, false), (double.PositiveInfinity, false),
            (double.NegativeInfinity, false), ("invalid", false)
        ];
        foreach (var (value, expected) in cases)
            Assert.Equal(expected, Assert.IsType<bool>(isatty.Invoke(null, [value])));
    }

    [Fact]
    public void ReusingEmitterKeepsBothOwnersAndClockStorageIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import 'perf_hooks';import 'tty';", false, emitter);
        var minimal = EmitRuntime("const value=1;", false, emitter);
        var second = EmitRuntime("import 'perf_hooks';import 'tty';", false, emitter);
        Assert.Null(minimal.Performance);
        Assert.Null(minimal.Tty);
        Assert.NotSame(first.RequirePerformance(), second.RequirePerformance());
        Assert.NotSame(first.RequirePerformance().Now, second.RequirePerformance().Now);
        Assert.NotSame(first.RequireTty(), second.RequireTty());
        Assert.NotSame(first.RequireTty().Isatty, second.RequireTty().Isatty);
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        var firstType = Assembly.Load(firstBytes.ToArray()).GetType("$Runtime")!;
        var secondType = Assembly.Load(secondBytes.ToArray()).GetType("$Runtime")!;
        firstType.GetMethod("PerfPrimitiveNow")!.Invoke(null, null);
        Assert.Equal(false, secondType.GetField("_perfPrimitiveInitialized", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
        AssertFrozen(first.RequirePerformance());
        AssertFrozen(second.RequirePerformance());
        AssertFrozen(first.RequireTty());
        AssertFrozen(second.RequireTty());
    }

    private static PropertyInfo Handle(object component) => component.GetType().GetProperties()
        .Single(property => property.PropertyType == typeof(MethodBuilder));

    private static bool IsComplete(object component) => component is EmittedPerformanceRuntime performance
        ? performance.IsComplete : Assert.IsType<EmittedTtyRuntime>(component).IsComplete;

    private static void Complete(object component)
    {
        if (component is EmittedPerformanceRuntime performance) performance.CompleteEmission();
        else Assert.IsType<EmittedTtyRuntime>(component).CompleteEmission();
    }

    private static void AssertFrozen(object component)
    {
        Assert.True(IsComplete(component));
        Assert.Throws<InvalidOperationException>(() => Complete(component));
        var property = Handle(component);
        var value = property.GetValue(component);
        Assert.NotNull(value);
        Assert.False(property.SetMethod!.IsPublic);
        var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, value));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    private static MethodBuilder Placeholder()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"primitive_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return method;
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"host_primitives_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
