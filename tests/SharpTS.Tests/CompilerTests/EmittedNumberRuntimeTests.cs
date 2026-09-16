using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedNumberRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedNumberRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRepair(string missingHandle)
    {
        var numbers = CreateDeclarations(missingHandle);
        var property = typeof(EmittedNumberRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(numbers));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(numbers.CompleteEmission).Message);
        Assert.False(numbers.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(numbers, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(numbers, property.GetValue(CreateDeclarations()));
        numbers.CompleteEmission();
        AssertFrozen(numbers);
    }

    [Fact]
    public void RequiredOwnerExistsBeforeEmissionAndCannotBeReplaced()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.NotSame(first.Numbers, second.Numbers);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Numbers))!.SetMethod);
        Assert.False(first.Numbers.IsComplete);
        Assert.Throws<InvalidOperationException>(() => first.Numbers.Format);
    }

    [Fact]
    public void ForwardFormatAndCachedFormattingInfrastructureRemainReadableBeforeLaterBodies()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("number_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var runtime = new EmittedRuntime();
        runtime.BigInt.BeginImplementationEmission();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        InvokeEmitter("DefineRuntimeClassPhase1", emitter, module, runtime);
        var numbers = runtime.Numbers;
        Assert.Equal(0, numbers.Format.GetILGenerator().ILOffset);
        Assert.Throws<InvalidOperationException>(() => numbers.PrototypePopulateMethod);
        InvokeEmitter("DefineNumberPrototypePopulateShell", emitter, runtime.RuntimeType, numbers);
        Assert.Equal(0, numbers.PrototypePopulateMethod.GetILGenerator().ILOffset);
        Assert.Throws<InvalidOperationException>(() => numbers.FixedUInt64FormatterField);
        InvokeEmitter("DefineNumberFixedFormattingInfrastructure", emitter, runtime.RuntimeType, numbers);
        Assert.Same(runtime.RuntimeType, numbers.FixedUInt64FormatterCallback.DeclaringType);
        Assert.True(numbers.FixedUInt64FormatterCallback.GetILGenerator().ILOffset > 0);
        Assert.True(numbers.FixedUInt64FormatterField.IsInitOnly);
        Assert.True(numbers.FixedUInt64FormatterField.IsPrivate);
        Assert.Throws<InvalidOperationException>(() => numbers.ToFixedDouble);
        Assert.Throws<InvalidOperationException>(numbers.CompleteEmission);
        Assert.False(numbers.IsComplete);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("const value=1;", true)]
    [InlineData("Number.parseInt('42');", false)]
    [InlineData("new Number(1);", false)]
    [InlineData("const value=42n;", false)]
    [InlineData("Symbol('x');", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void NumberDeclarationsAndFormattingCacheRemainRequiredAcrossFeatures(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.Numbers);
        foreach (var property in Handles)
            Assert.Same(runtime.RuntimeType, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.Numbers)).DeclaringType);
        using var bytes = Save(runtime);
        Verify(bytes);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var numbers = runtime.Numbers;
        Assert.True(type.GetMethod(numbers.Format.Name)!.MetadataToken < type.GetMethod(numbers.PrototypePopulateMethod.Name)!.MetadataToken);
        Assert.True(type.GetMethod(numbers.PrototypePopulateMethod.Name)!.MetadataToken
            < type.GetMethod(numbers.FixedUInt64FormatterCallback.Name, BindingFlags.NonPublic | BindingFlags.Static)!.MetadataToken);
        var cache = type.GetField(numbers.FixedUInt64FormatterField.Name, BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(cache.IsInitOnly);
        var formatter = Assert.IsAssignableFrom<Delegate>(cache.GetValue(null));
        Assert.Equal(numbers.FixedUInt64FormatterCallback.Name, formatter.Method.Name);
        Assert.Equal("12.50", Call(type, numbers.ToFixedDouble, 12.5d, 2));
        Assert.Same(formatter, cache.GetValue(null));
    }

    [Theory]
    [InlineData("42tail", 42d)]
    [InlineData("9007199254740995", 9007199254740996d)]
    [InlineData("18446744073709551616", 18446744073709551616d)]
    [InlineData("\uFEFF+17", 17d)]
    [InlineData("\u008517", double.NaN)]
    [InlineData("x", double.NaN)]
    public void NativeDecimalParserKeepsRoundingAndWhitespaceBoundaries(string value, double expected)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var method = type.GetMethod(runtime.Numbers.ParseIntDecimalString.Name)!;
        Assert.Equal(new[] { typeof(string) }, method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.AggressiveOptimization));
        Assert.Equal(expected, Call(type, runtime.Numbers.ParseIntDecimalString, value));
        Assert.True(double.IsNegative(Assert.IsType<double>(Call(type, runtime.Numbers.ParseIntDecimalString, "-0"))));
    }

    [Theory]
    [InlineData("0xff", 16, 255d)]
    [InlineData("0X10", 0, 16d)]
    [InlineData("zz", 36, 1295d)]
    [InlineData("10101", 2, 21d)]
    [InlineData("10", 1, double.NaN)]
    public void NativeRadixParserKeepsTypedSignatureAndPrefixHandling(string value, int radix, double expected)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var method = type.GetMethod(runtime.Numbers.ParseIntString.Name)!;
        Assert.Equal(new[] { typeof(string), typeof(int) }, method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.AggressiveOptimization));
        Assert.Equal(expected, Call(type, runtime.Numbers.ParseIntString, value, radix));
    }

    [Theory]
    [InlineData(2.5d, 0, "3")]
    [InlineData(1.005d, 2, "1.00")]
    [InlineData(0.125d, 2, "0.13")]
    [InlineData(-0.0001d, 2, "-0.00")]
    [InlineData(-0d, 2, "0.00")]
    [InlineData(0.1d, 20, "0.10000000000000000555")]
    [InlineData(1e21, 2, "1e+21")]
    [InlineData(double.PositiveInfinity, 2, "Infinity")]
    public void NativeFixedFormatterPreservesExactRoundingAndFallbacks(double value, int digits, string expected)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var method = type.GetMethod(runtime.Numbers.ToFixedDouble.Name)!;
        Assert.Equal(new[] { typeof(double), typeof(int) }, method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.AggressiveOptimization));
        Assert.Equal(expected, Call(type, runtime.Numbers.ToFixedDouble, value, digits));
    }

    [Fact]
    public void NativeFixedFormatterRetainsGuestRangeErrors()
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        foreach (var digits in new[] { -1, 101 })
        {
            var error = Assert.Throws<TargetInvocationException>(() => Call(type, runtime.Numbers.ToFixedDouble, 1d, digits));
            var guest = error.InnerException!.GetType().GetProperty("Value")!.GetValue(error.InnerException);
            Assert.Equal("$RangeError", guest!.GetType().Name);
        }
    }

    [Fact]
    public void StrictNumberPredicatesAndGlobalCoercionKeepDistinctContracts()
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Equal(false, Call(type, runtime.Numbers.IsFinite, "42"));
        Assert.Equal(true, Call(type, runtime.Numbers.GlobalIsFinite, "42"));
        Assert.Equal(false, Call(type, runtime.Numbers.IsNaN, "bad"));
        Assert.Equal(true, Call(type, runtime.Numbers.GlobalIsNaN, "bad"));
        Assert.Equal(true, Call(type, runtime.Numbers.IsSafeInteger, 9007199254740991d));
        Assert.Equal(false, Call(type, runtime.Numbers.IsSafeInteger, 9007199254740992d));
    }

    [Fact]
    public void ReusedEmitterKeepsPrototypeDictionariesAndFormatterCachesIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime(null, false, emitter);
        var second = EmitRuntime("const value=1;", false, emitter);
        AssertFrozen(first.Numbers);
        AssertFrozen(second.Numbers);
        foreach (var property in Handles)
            Assert.NotSame(property.GetValue(first.Numbers), property.GetValue(second.Numbers));
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        Verify(secondBytes);
        var firstType = Assembly.Load(firstBytes.ToArray()).GetType("$Runtime")!;
        var secondType = Assembly.Load(secondBytes.ToArray()).GetType("$Runtime")!;
        Assert.NotSame(firstType.GetField(first.Numbers.PrototypeField.Name)!.GetValue(null),
            secondType.GetField(second.Numbers.PrototypeField.Name)!.GetValue(null));
        Assert.NotSame(firstType.GetField(first.Numbers.FixedUInt64FormatterField.Name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null),
            secondType.GetField(second.Numbers.FixedUInt64FormatterField.Name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
        Assert.Equal("1.25", Call(secondType, second.Numbers.ToFixedDouble, 1.25d, 2));
    }

    private static object? Call(Type type, MethodInfo method, params object?[] arguments) =>
        type.GetMethod(method.Name)!.Invoke(null, arguments);

    private static void InvokeEmitter(string name, RuntimeEmitter emitter, params object[] arguments) =>
        typeof(RuntimeEmitter).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(emitter, arguments);

    private static EmittedNumberRuntime CreateDeclarations(string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"numbers_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("BoxedDeclarations", TypeAttributes.Public);
        var numbers = new EmittedNumberRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omitted) continue;
            if (property.PropertyType == typeof(FieldBuilder))
                property.SetValue(numbers, type.DefineField(property.Name, typeof(object), FieldAttributes.Public | FieldAttributes.Static));
            else
            {
                var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                method.GetILGenerator().Emit(OpCodes.Ret);
                property.SetValue(numbers, method);
            }
        }
        return numbers;
    }

    private static void AssertFrozen(EmittedNumberRuntime numbers)
    {
        Assert.True(numbers.IsComplete);
        Assert.Throws<InvalidOperationException>(numbers.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(numbers);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(numbers, value));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"numbers_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
