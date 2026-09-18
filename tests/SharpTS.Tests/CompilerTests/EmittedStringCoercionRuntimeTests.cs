using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedStringCoercionRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedStringCoercionRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRepair(string missingHandle)
    {
        var coercion = CreateDeclarations(missingHandle);
        var property = typeof(EmittedStringCoercionRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(coercion));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(coercion.CompleteEmission).Message);
        Assert.False(coercion.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(coercion, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(coercion, property.GetValue(CreateDeclarations()));
        coercion.CompleteEmission();
        AssertFrozen(coercion);
    }

    [Fact]
    public void RequiredOwnerExistsBeforeEmissionAndCannotBeReplaced()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.NotSame(first.StringCoercion, second.StringCoercion);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.StringCoercion))!.SetMethod);
        Assert.False(first.StringCoercion.IsComplete);
        Assert.Throws<InvalidOperationException>(() => first.StringCoercion.Stringify);
    }

    [Fact]
    public void ThreeForwardDeclarationsRemainReadableBeforeBodiesAndLaterHelpers()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("coercion_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var runtime = new EmittedRuntime();
        runtime.BigInt.BeginImplementationEmission();
        typeof(RuntimeEmitter).GetMethod("DefineRuntimeClassPhase1", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [module, runtime]);
        var coercion = runtime.StringCoercion;
        foreach (var method in new[] { coercion.Stringify, coercion.ToJsString, coercion.StringifyCoerce })
        {
            Assert.Same(runtime.RuntimeType, method.DeclaringType);
            Assert.Equal(0, method.GetILGenerator().ILOffset);
        }
        Assert.NotSame(coercion.Stringify, coercion.ToJsString);
        Assert.NotSame(coercion.ToJsString, coercion.StringifyCoerce);
        var caller = runtime.RuntimeType.DefineMethod("ForwardCaller", MethodAttributes.Public | MethodAttributes.Static,
            typeof(string), [typeof(object)]);
        caller.GetILGenerator().Emit(OpCodes.Ldarg_0);
        caller.GetILGenerator().Emit(OpCodes.Call, coercion.ToJsString);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.True(caller.GetILGenerator().ILOffset > 0);
        Assert.Throws<InvalidOperationException>(() => coercion.FromValue);
        Assert.Throws<InvalidOperationException>(() => coercion.ConcatInt64);
        Assert.Throws<InvalidOperationException>(coercion.CompleteEmission);
        Assert.False(coercion.IsComplete);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("const value=1;", true)]
    [InlineData("Symbol('value');", false)]
    [InlineData("new RegExp('value');", false)]
    [InlineData("class Value{toString(){return 'value';}}new Value();", false)]
    [InlineData("const values:number[]=[1,2];String(values);", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void ConversionMetadataRemainsRequiredAcrossFeatureAndHostingModes(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.StringCoercion);
        foreach (var property in Handles)
            Assert.Same(runtime.RuntimeType, Assert.IsAssignableFrom<MethodInfo>(property.GetValue(runtime.StringCoercion)).DeclaringType);
        using var bytes = Save(runtime);
        Verify(bytes);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var features = source is null ? RuntimeFeatureSet.EmitEverything() : Detect(source);
        var names = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name));
        Assert.Equal(features.UsesRegExp, names.Contains("$RegExp"));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        // Persisted builders receive their final tokens only when the assembly is saved.
        Assert.True(type.GetMethod(runtime.StringCoercion.Stringify.Name)!.MetadataToken
            < type.GetMethod(runtime.StringCoercion.ToJsString.Name)!.MetadataToken);
        Assert.True(type.GetMethod(runtime.StringCoercion.ToJsString.Name)!.MetadataToken
            < type.GetMethod(runtime.StringCoercion.StringifyCoerce.Name)!.MetadataToken);
        Assert.Equal("42", Convert(type, runtime.StringCoercion.ToJsString, 42d));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LanguageConversionUsesExplicitRegExpAvailability(bool includeRegExp)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"explicit_coercion_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var features = Detect(includeRegExp ? "new RegExp('value');" : "const value=1;");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, features);
        var helper = module.DefineType("ExplicitCoercion", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var coercion = new EmittedStringCoercionRuntime
        {
            Stringify = runtime.StringCoercion.Stringify,
            ToJsString = helper.DefineMethod("ToJsString", MethodAttributes.Public | MethodAttributes.Static,
                typeof(string), [typeof(object)])
        };
        var customToString = helper.DefineMethod("CustomToString", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), Type.EmptyTypes);
        customToString.GetILGenerator().Emit(OpCodes.Ldstr, "custom regex");
        customToString.GetILGenerator().Emit(OpCodes.Ret);
        // This fixture's helper lives outside $Runtime, whose symbol reader is private.
        // No symbol hooks are needed here; supply an accessible empty dictionary reader.
        var getSymbols = helper.DefineMethod("GetSymbols", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Dictionary<object, object>), [typeof(object)]);
        getSymbols.GetILGenerator().Emit(OpCodes.Newobj, typeof(Dictionary<object, object>).GetConstructor(Type.EmptyTypes)!);
        getSymbols.GetILGenerator().Emit(OpCodes.Ret);

        const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        // A scoped helper must follow the supplied metadata even when the emitter's
        // unrelated compilation selection differs in either direction.
        typeof(RuntimeEmitter).GetField("_features", privateInstance)!.SetValue(emitter,
            includeRegExp ? Detect("const value=1;") : RuntimeFeatureSet.EmitEverything());
        var inputType = typeof(RuntimeEmitter).GetNestedType("StringCoercionInputs", BindingFlags.NonPublic)!;
        var peers = Activator.CreateInstance(inputType,
        [
            runtime.Sentinels.UndefinedType, runtime.Symbols.Type, runtime.GlobalThisSingletonField, runtime.GlobalThisGetProperty,
            runtime.Operators.TypeOf, runtime.Invocation.Method, runtime.Arguments.Type, runtime.ObjectRead.Property, runtime.ObjectStorage.Type,
            runtime.FunctionValues.Type, runtime.FunctionBindings.AnyType, runtime.ObjectOwnProperties.HasOwnProperty, runtime.ObjectFields.Interface,
            getSymbols, runtime.Symbols.ToPrimitive, runtime.DescriptorStorage.DescriptorType,
            runtime.DescriptorStorage.DescriptorGetter.GetGetMethod()!, runtime.DescriptorStorage.DescriptorSetter.GetGetMethod()!,
            runtime.DescriptorStorage.DescriptorValue.GetGetMethod()!, runtime.DescriptorStorage.HasPrototypeEntry, runtime.DescriptorStorage.GetPrototype,
            runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor
        ]);
        typeof(RuntimeEmitter).GetMethod("EmitToJsString", privateInstance)!.Invoke(emitter,
            [helper, coercion, runtime.ArrayStorage, runtime.ArrayOperations, peers, includeRegExp ? runtime.RegExps.RequireImplementation().Type : null]);
        helper.CreateType();
        using var bytes = Save(runtime);
        Verify(bytes);
        var loaded = Assembly.Load(bytes.ToArray());
        var helperType = loaded.GetType("ExplicitCoercion")!;
        var convert = helperType.GetMethod("ToJsString")!;
        Assert.Equal("42", convert.Invoke(null, [42d]));
        if (includeRegExp)
        {
            var regex = Activator.CreateInstance(loaded.GetType(runtime.RegExps.RequireImplementation().Type.Name)!, ["value", ""]);
            var function = Activator.CreateInstance(loaded.GetType(runtime.FunctionValues.Type.Name)!,
                [null, helperType.GetMethod("CustomToString")!, "toString", 0]);
            loaded.GetType("$Runtime")!.GetMethod("SetProperty")!.Invoke(null, [regex, "toString", function]);
            Assert.Equal("custom regex", convert.Invoke(null, [regex]));
        }
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData(false, "false")]
    [InlineData(true, "true")]
    [InlineData(-0d, "0")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(1e-7, "1e-7")]
    public void PrimitiveValuesKeepDisplayAndLanguageConversionResults(object? value, string expected)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        foreach (var method in new[] { runtime.StringCoercion.Stringify, runtime.StringCoercion.ToJsString,
            runtime.StringCoercion.FromValue, runtime.StringCoercion.StringifyCoerce })
            Assert.Equal(expected, Convert(type, method, value));
    }

    [Fact]
    public void BigIntAndArrayDisplayRemainDistinctFromLanguageConversion()
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var coercion = runtime.StringCoercion;
        Assert.Equal("42n", Convert(type, coercion.Stringify, new BigInteger(42)));
        foreach (var method in new[] { coercion.ToJsString, coercion.FromValue, coercion.StringifyCoerce })
            Assert.Equal("42", Convert(type, method, new BigInteger(42)));
        var values = new List<object> { 1d, 2d };
        Assert.Equal("[1, 2]", Convert(type, coercion.Stringify, values));
        Assert.Equal("1,2", Convert(type, coercion.ToJsString, values));
        Assert.Equal("1,2", Convert(type, coercion.StringifyCoerce, values));
    }

    [Fact]
    public void OnlyStringCallFormPermitsSymbolConversion()
    {
        var runtime = EmitRuntime("Symbol('value');", false);
        using var bytes = Save(runtime);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$Runtime")!;
        var symbol = Activator.CreateInstance(assembly.GetType(runtime.Symbols.Type.Name)!, ["value"]);
        Assert.Equal("Symbol(value)", Convert(type, runtime.StringCoercion.FromValue, symbol));
        foreach (var method in new[] { runtime.StringCoercion.ToJsString, runtime.StringCoercion.StringifyCoerce })
        {
            var error = Assert.Throws<TargetInvocationException>(() => Convert(type, method, symbol));
            var guest = error.InnerException!.GetType().GetProperty("Value")!.GetValue(error.InnerException);
            Assert.Equal("$TypeError", guest!.GetType().Name);
            Assert.Contains("Cannot convert a Symbol value to a string", error.InnerException.Message);
        }
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(-42L)]
    [InlineData(0L)]
    [InlineData(42L)]
    public void IntegerConcatenationPreservesExtremaOrderAndTypedOptimization(long value)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var method = type.GetMethod(runtime.StringCoercion.ConcatInt64.Name)!;
        Assert.Equal(new[] { typeof(string), typeof(long), typeof(bool) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(string), method.ReturnType);
        Assert.True(method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.AggressiveOptimization));
        var expected = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("prefix" + expected, method.Invoke(null, ["prefix", value, false]));
        Assert.Equal(expected + "suffix", method.Invoke(null, ["suffix", value, true]));
        var buffer = type.GetField("_concatInt64Buffer", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(typeof(char[]), buffer.FieldType);
        Assert.NotNull(buffer.GetCustomAttribute<ThreadStaticAttribute>());
        Assert.Equal(20, Assert.IsType<char[]>(buffer.GetValue(null)).Length);
    }

    [Fact]
    public void ReusedEmitterKeepsConversionHandlesAndIntegerBuffersIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime(null, false, emitter);
        var second = EmitRuntime("const value=1;", false, emitter);
        AssertFrozen(first.StringCoercion);
        AssertFrozen(second.StringCoercion);
        foreach (var property in Handles)
            Assert.NotSame(property.GetValue(first.StringCoercion), property.GetValue(second.StringCoercion));
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        Verify(secondBytes);
        var firstType = Assembly.Load(firstBytes.ToArray()).GetType("$Runtime")!;
        var secondType = Assembly.Load(secondBytes.ToArray()).GetType("$Runtime")!;
        foreach (var type in new[] { firstType, secondType })
            type.GetMethod("ConcatStringInt64")!.Invoke(null, ["value", 1L, false]);
        Assert.NotSame(firstType.GetField("_concatInt64Buffer", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null),
            secondType.GetField("_concatInt64Buffer", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
    }

    private static object? Convert(Type type, MethodInfo method, object? value) => type.GetMethod(method.Name)!.Invoke(null, [value]);

    private static EmittedStringCoercionRuntime CreateDeclarations(string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"coercion_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("CoercionDeclarations", TypeAttributes.Public);
        var coercion = new EmittedStringCoercionRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omitted) continue;
            var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            method.GetILGenerator().Emit(OpCodes.Ret);
            property.SetValue(coercion, method);
        }
        return coercion;
    }

    private static void AssertFrozen(EmittedStringCoercionRuntime coercion)
    {
        Assert.True(coercion.IsComplete);
        Assert.Throws<InvalidOperationException>(coercion.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(coercion);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(coercion, value));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"string_coercion_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
