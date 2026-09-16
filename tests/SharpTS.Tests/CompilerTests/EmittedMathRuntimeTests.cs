using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedMathRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedMathRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRepair(string missingHandle)
    {
        var math = CreateDeclarations(missingHandle);
        var property = typeof(EmittedMathRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(math));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(math.CompleteEmission).Message);
        Assert.False(math.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(math, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(math, property.GetValue(CreateDeclarations()));
        math.CompleteEmission();
        AssertFrozen(math);
    }

    [Fact]
    public void RequiredOwnerExistsBeforeEmissionAndCannotBeReplaced()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.NotSame(first.Math, second.Math);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Math))!.SetMethod);
        Assert.False(first.Math.IsComplete);
        Assert.Throws<InvalidOperationException>(() => first.Math.SingletonField);
    }

    [Fact]
    public void SingletonAdaptersAndRandomAreReadableBeforeLateExactSum()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("math_staged"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("MathStaged");
        var math = new EmittedRuntime().Math;
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        math.SingletonField = type.DefineField("math", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        InvokeEmitter("DefineMathSingletonPopulateShell", emitter, type, math);
        Assert.Equal(0, math.SingletonPopulateMethod.GetILGenerator().ILOffset);
        Assert.Throws<InvalidOperationException>(() => math.FloorAdapter);
        var number = type.DefineMethod("number", MethodAttributes.Public | MethodAttributes.Static, typeof(double), [typeof(object)]);
        number.GetILGenerator().Emit(OpCodes.Ldc_R8, 0d);
        number.GetILGenerator().Emit(OpCodes.Ret);
        var integer = type.DefineMethod("integer", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(object)]);
        integer.GetILGenerator().Emit(OpCodes.Ldc_I4_0);
        integer.GetILGenerator().Emit(OpCodes.Ret);
        InvokeEmitter("EmitMathAdapters", emitter, type, math, number, integer);
        Assert.True(math.FloorAdapter.GetILGenerator().ILOffset > 0);
        Assert.True(math.HypotAdapter.GetILGenerator().ILOffset > 0);
        Assert.Throws<InvalidOperationException>(() => math.Random);
        var random = type.DefineField("random", typeof(Random), FieldAttributes.Private | FieldAttributes.Static);
        InvokeEmitter("EmitRandom", emitter, type, math, random);
        Assert.Same(type, math.Random.DeclaringType);
        Assert.Throws<InvalidOperationException>(() => math.SumPrecise);
        Assert.Throws<InvalidOperationException>(math.CompleteEmission);
        Assert.False(math.IsComplete);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("const value=1;", true)]
    [InlineData("Math.floor(2.5);", false)]
    [InlineData("Reflect.get({x:1},'x'); JSON.stringify({x:1});", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void AllMathDeclarationsRemainRequiredAcrossFeatures(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.Math);
        foreach (var property in Handles)
            Assert.Same(runtime.RuntimeType, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.Math)).DeclaringType);
        using var bytes = Save(runtime);
        Verify(bytes);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var math = runtime.Math;
        Assert.True(type.GetMethod(math.SingletonPopulateMethod.Name)!.MetadataToken < type.GetMethod(math.FloorAdapter.Name)!.MetadataToken);
        Assert.True(type.GetMethod(math.FloorAdapter.Name)!.MetadataToken < type.GetMethod(math.Random.Name)!.MetadataToken);
        Assert.True(type.GetMethod(math.Random.Name)!.MetadataToken < type.GetMethod(math.SumPrecise.Name)!.MetadataToken);
        var singleton = Assert.IsType<Dictionary<string, object>>(type.GetField(math.SingletonField.Name)!.GetValue(null));
        var floor = singleton["floor"];
        Call(type, math.SingletonPopulateMethod);
        Assert.Same(singleton, type.GetField(math.SingletonField.Name)!.GetValue(null));
        Assert.Same(floor, singleton["floor"]);
        Assert.NotNull(singleton["sumPrecise"]);
        Assert.Equal(2d, Call(type, math.FloorAdapter, "2.9"));
    }

    [Theory]
    [InlineData(-0.5d, -0.0d)]
    [InlineData(-0.25d, -0.0d)]
    [InlineData(0.5d, 1d)]
    [InlineData(-1.5d, -1d)]
    [InlineData(4503599627370497d, 4503599627370497d)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity)]
    public void RoundAdapterPreservesSignedZeroTiesAndLargeIntegers(double value, double expected)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var actual = Assert.IsType<double>(Call(type, runtime.Math.RoundAdapter, value));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
    }

    [Theory]
    [InlineData(1e30d, 0.1d, -1e30d, 0.1d)]
    [InlineData(1d, 2d, 3d, 6d)]
    [InlineData(double.MaxValue, double.MaxValue, 0d, double.PositiveInfinity)]
    [InlineData(double.Epsilon, double.Epsilon, 0d, 2 * double.Epsilon)]
    public void ExactSumKeepsCancellationSubnormalsAndOverflow(double a, double b, double c, double expected)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Equal(expected, Call(type, runtime.Math.SumPrecise, new List<object> { a, b, c }));
        Assert.True(double.IsNegative(Assert.IsType<double>(Call(type, runtime.Math.SumPrecise, new List<object>()))));
        Assert.True(double.IsNaN(Assert.IsType<double>(Call(type, runtime.Math.SumPrecise,
            new List<object> { double.PositiveInfinity, double.NegativeInfinity }))));
    }

    [Fact]
    public void NativeSignaturesAndRandomInitializationRemainStable()
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var math = runtime.Math;
        Assert.Equal(typeof(double), type.GetMethod(math.Random.Name)!.ReturnType);
        Assert.Empty(type.GetMethod(math.Random.Name)!.GetParameters());
        Assert.Equal(new[] { typeof(object) }, type.GetMethod(math.FloorAdapter.Name)!.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { typeof(object), typeof(object) }, type.GetMethod(math.PowAdapter.Name)!.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { typeof(object[]) }, type.GetMethod(math.MaxAdapter.Name)!.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { typeof(object) }, type.GetMethod(math.SumPrecise.Name)!.GetParameters().Select(p => p.ParameterType));
        var random = type.GetField("_random", BindingFlags.NonPublic | BindingFlags.Static)!;
        var generator = Assert.IsType<Random>(random.GetValue(null));
        for (var i = 0; i < 64; i++)
        {
            var value = Assert.IsType<double>(Call(type, math.Random));
            Assert.True(value >= 0 && value < 1);
        }
        Assert.Same(generator, random.GetValue(null));
        Assert.Equal(-5d, Call(type, math.ImulAdapter, 4294967295d, 5d));
        Assert.Equal(32d, Call(type, math.Clz32Adapter, double.PositiveInfinity));
        Assert.Equal(1.5d, Call(type, math.F16RoundAdapter, 1.5d));
    }

    [Fact]
    public void ReusedEmitterKeepsSingletonsAndRandomStateIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime(null, false, emitter);
        var second = EmitRuntime("const value=1;", false, emitter);
        AssertFrozen(first.Math);
        AssertFrozen(second.Math);
        foreach (var property in Handles)
            Assert.NotSame(property.GetValue(first.Math), property.GetValue(second.Math));
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        Verify(secondBytes);
        var firstType = Assembly.Load(firstBytes.ToArray()).GetType("$Runtime")!;
        var secondType = Assembly.Load(secondBytes.ToArray()).GetType("$Runtime")!;
        Assert.NotSame(firstType.GetField(first.Math.SingletonField.Name)!.GetValue(null),
            secondType.GetField(second.Math.SingletonField.Name)!.GetValue(null));
        Assert.NotSame(firstType.GetField("_random", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null),
            secondType.GetField("_random", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
        Assert.Equal(3d, Call(secondType, second.Math.SqrtAdapter, 9d));
    }

    private static object? Call(Type type, MethodInfo method, params object?[] arguments) =>
        type.GetMethod(method.Name)!.Invoke(null, arguments);

    private static void InvokeEmitter(string name, RuntimeEmitter emitter, params object[] arguments) =>
        typeof(RuntimeEmitter).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(emitter, arguments);

    private static EmittedMathRuntime CreateDeclarations(string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"math_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("MathDeclarations", TypeAttributes.Public);
        var math = new EmittedMathRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omitted) continue;
            if (property.PropertyType == typeof(FieldBuilder))
                property.SetValue(math, type.DefineField(property.Name, typeof(object), FieldAttributes.Public | FieldAttributes.Static));
            else
            {
                var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                method.GetILGenerator().Emit(OpCodes.Ret);
                property.SetValue(math, method);
            }
        }
        return math;
    }

    private static void AssertFrozen(EmittedMathRuntime math)
    {
        Assert.True(math.IsComplete);
        Assert.Throws<InvalidOperationException>(math.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(math);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(math, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static void Verify(MemoryStream bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        bytes.Position = 0;
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"math_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
