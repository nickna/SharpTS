using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedBoxedPrimitiveRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedBoxedPrimitiveRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRepair(string missingHandle)
    {
        var boxed = CreateDeclarations(missingHandle);
        var property = typeof(EmittedBoxedPrimitiveRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(boxed));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(boxed.CompleteEmission).Message);
        Assert.False(boxed.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(boxed, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(boxed, property.GetValue(CreateDeclarations()));
        boxed.CompleteEmission();
        AssertFrozen(boxed);
    }

    [Fact]
    public void RequiredOwnerExistsBeforeEmissionAndCannotBeReplaced()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.NotSame(first.BoxedPrimitives, second.BoxedPrimitives);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.BoxedPrimitives))!.SetMethod);
        Assert.False(first.BoxedPrimitives.IsComplete);
        Assert.Throws<InvalidOperationException>(() => first.BoxedPrimitives.ToObject);
    }

    [Fact]
    public void ThreeSeparateForwardStagesAllowCallsBeforeBodiesAndCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("boxed_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var runtime = new EmittedRuntime();
        runtime.BigInt.BeginImplementationEmission();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        InvokeEmitter("DefineRuntimeClassPhase1", emitter, module, runtime);
        var boxed = runtime.BoxedPrimitives;
        Assert.Equal(0, boxed.ToObject.GetILGenerator().ILOffset);
        Assert.Throws<InvalidOperationException>(() => boxed.UnwrapIfBoxed);
        Assert.Throws<InvalidOperationException>(() => boxed.IsOfType);
        InvokeEmitter("DeclareUnwrapIfBoxed", emitter, runtime.RuntimeClass.Type, boxed);
        Assert.Equal(0, boxed.UnwrapIfBoxed.GetILGenerator().ILOffset);
        Assert.Throws<InvalidOperationException>(() => boxed.IsOfType);
        InvokeEmitter("DefineIsBoxedPrimitiveOfTypeShell", emitter, runtime.RuntimeClass.Type, boxed);
        Assert.Equal(0, boxed.IsOfType.GetILGenerator().ILOffset);
        foreach (var method in new[] { boxed.ToObject, boxed.UnwrapIfBoxed, boxed.IsOfType })
            Assert.Same(runtime.RuntimeClass.Type, method.DeclaringType);
        var caller = runtime.RuntimeClass.Type.DefineMethod("ForwardCaller", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object)]);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, boxed.ToObject);
        il.Emit(OpCodes.Call, boxed.UnwrapIfBoxed);
        il.Emit(OpCodes.Ret);
        Assert.True(il.ILOffset > 0);
        Assert.Throws<InvalidOperationException>(boxed.CompleteEmission);
        Assert.False(boxed.IsComplete);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("const value=1;", true)]
    [InlineData("Object(42n);", false)]
    [InlineData("new Date(0);", false)]
    [InlineData("Object(Symbol('x'));", false)]
    [InlineData("new Date(0);Object(42n);", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void WrapperMetadataRemainsRequiredAcrossFeatureAndHostingModes(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.BoxedPrimitives);
        foreach (var property in Handles)
            Assert.Same(runtime.RuntimeClass.Type, Assert.IsAssignableFrom<MethodInfo>(property.GetValue(runtime.BoxedPrimitives)).DeclaringType);
        using var bytes = Save(runtime);
        Verify(bytes);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var features = source is null ? RuntimeFeatureSet.EmitEverything() : Detect(source);
        var names = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name));
        Assert.Equal(features.UsesDate, names.Contains("$TSDate"));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var boxed = runtime.BoxedPrimitives;
        Assert.True(type.GetMethod(boxed.ToObject.Name)!.MetadataToken < type.GetMethod(boxed.UnwrapIfBoxed.Name)!.MetadataToken);
        Assert.True(type.GetMethod(boxed.UnwrapIfBoxed.Name)!.MetadataToken < type.GetMethod(boxed.IsOfType.Name)!.MetadataToken);
        var wrapper = Call(type, boxed.ToObject, 42d);
        Assert.Equal(42d, Call(type, boxed.UnwrapIfBoxed, wrapper));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrapperCreationUsesExplicitBigIntPrototypeAvailability(bool includePrototype)
    {
        var (runtime, helper, emitter) = CreateExplicitDependencyFixture(includePrototype);
        var boxed = new EmittedBoxedPrimitiveRuntime();
        var peers = CreateInputs("BoxedPrimitiveInputs",
            runtime.ObjectStorage.Type, runtime.ObjectStorage.Constructor,
            runtime.DescriptorStorage.DescriptorType, runtime.DescriptorStorage.DescriptorConstructor,
            runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!,
            runtime.DescriptorStorage.DescriptorWritable.GetSetMethod()!,
            runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!,
            runtime.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!,
            runtime.DescriptorStorage.DefineProperty, runtime.DescriptorStorage.SetPrototype,
            runtime.Booleans.PrototypeField, runtime.Booleans.PrototypePopulateMethod,
            runtime.Numbers.PrototypeField, runtime.Numbers.PrototypePopulateMethod,
            runtime.Symbols.Prototype, runtime.Symbols.PopulatePrototype);
        var prototype = includePrototype
            ? CreateInputs("BoxedBigIntPrototype", runtime.BigInt.PrototypeField, runtime.BigInt.PrototypePopulateMethod)
            : null;
        InvokeEmitter("EmitNewBoxedPrimitive", emitter, helper, boxed, runtime.Strings, peers, prototype);
        helper.CreateType();
        using var bytes = Save(runtime);
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        var runtimeType = assembly.GetType("$Runtime")!;
        var helperType = assembly.GetType(helper.Name)!;
        var wrapper = Call(helperType, boxed.New, "BigInt", new BigInteger(42));
        Assert.Equal(new BigInteger(42), Call(runtimeType, runtime.BoxedPrimitives.UnwrapIfBoxed, wrapper));
        var descriptorStoreType = assembly.GetType(runtime.DescriptorStorage.GetPrototype.DeclaringType!.FullName!)!;
        var actualPrototype = Call(descriptorStoreType, runtime.DescriptorStorage.GetPrototype, wrapper);
        if (includePrototype)
        {
            var expectedPrototype = Assert.IsType<Dictionary<string, object>>(
                runtimeType.GetField(runtime.BigInt.PrototypeField.Name)!.GetValue(null));
            Assert.Same(expectedPrototype, actualPrototype);
        }
        else
            Assert.Null(actualPrototype);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultConversionUsesExplicitDateAvailability(bool includeDate)
    {
        var (runtime, helper, emitter) = CreateExplicitDependencyFixture(includeDate);
        var boxed = new EmittedBoxedPrimitiveRuntime();
        InvokeEmitter("DeclareUnwrapIfBoxed", emitter, helper, boxed);
        var peers = CreateInputs("UnwrapPrimitiveInputs",
            runtime.ObjectStorage.Type, runtime.Symbols.ToPrimitive, runtime.ObjectRead.Index, runtime.Sentinels.UndefinedType,
            runtime.Operators.TypeOf, runtime.Invocation.Method, runtime.ObjectStorage.GetProperty,
            runtime.ObjectOwnProperties.HasOwnProperty, runtime.ObjectRead.Property, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor);
        var dateInputs = includeDate ? CreateInputs("BoxedDateInputs", runtime.Dates.RequireImplementation().Type, runtime.Dates.RequireImplementation().ToStringMethod) : null;
        InvokeEmitter("EmitUnwrapIfBoxedBody", emitter, boxed, peers, dateInputs);
        helper.CreateType();
        using var bytes = Save(runtime);
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        var runtimeType = assembly.GetType("$Runtime")!;
        var helperType = assembly.GetType(helper.Name)!;
        Assert.Equal(42d, Call(helperType, boxed.UnwrapIfBoxed, 42d));
        var date = Activator.CreateInstance(assembly.GetType(runtime.Dates.RequireImplementation().Type.Name)!, [0d]);
        var actual = Call(helperType, boxed.UnwrapIfBoxed, date);
        if (includeDate)
            Assert.Equal(Call(runtimeType, runtime.Dates.RequireImplementation().ToStringMethod, date), Assert.IsType<string>(actual));
        else
            Assert.Same(date, actual);
    }

    [Theory]
    [InlineData("Number", 7d)]
    [InlineData("Boolean", false)]
    [InlineData("String", "hello")]
    public void WrappersPreserveBrandsPrimitiveValuesAndToObjectIdentity(string tag, object value)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var boxed = runtime.BoxedPrimitives;
        var wrapper = Call(type, boxed.New, tag, value);
        Assert.Equal(true, Call(type, boxed.IsOfType, wrapper, tag));
        Assert.Equal(false, Call(type, boxed.IsOfType, wrapper, "Other"));
        Assert.Equal(false, Call(type, boxed.IsOfType, value, tag));
        Assert.Equal(value, Call(type, boxed.UnwrapIfBoxed, wrapper));
        Assert.Same(wrapper, Call(type, boxed.ToObject, wrapper));
        Assert.NotSame(wrapper, Call(type, boxed.ToObject, value));
        Assert.Same(wrapper, Call(type, boxed.NormalizeForeignEvalValue, wrapper));
    }

    [Fact]
    public void BigIntAndSymbolWrappersKeepTheirUnderlyingPrimitiveIdentity()
    {
        var runtime = EmitRuntime("Object(42n);Object(Symbol('x'));", false);
        using var bytes = Save(runtime);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$Runtime")!;
        var boxed = runtime.BoxedPrimitives;
        var bigInt = Call(type, boxed.ToObject, new BigInteger(42));
        Assert.Equal(true, Call(type, boxed.IsOfType, bigInt, "BigInt"));
        Assert.Equal(new BigInteger(42), Call(type, boxed.UnwrapIfBoxed, bigInt));
        var symbol = Activator.CreateInstance(assembly.GetType(runtime.Symbols.Type.Name)!, ["x"]);
        var symbolBox = Call(type, boxed.ToObject, symbol);
        Assert.NotSame(symbol, symbolBox);
        Assert.Equal(true, Call(type, boxed.IsOfType, symbolBox, "Symbol"));
        Assert.Same(symbol, Call(type, boxed.UnwrapIfBoxed, symbolBox));
    }

    [Fact]
    public void NullishToObjectCreatesFreshObjectsWhileObjectInputsPassThrough()
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$Runtime")!;
        var undefined = assembly.GetType(runtime.Sentinels.UndefinedType.Name)!.GetField("Instance")!.GetValue(null);
        var boxed = runtime.BoxedPrimitives;
        var first = Call(type, boxed.ToObject, (object?)null);
        var second = Call(type, boxed.ToObject, undefined);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(runtime.ObjectStorage.Type.Name, first.GetType().Name);
        var dictionary = new Dictionary<string, object>();
        var array = new List<object>();
        Assert.Same(dictionary, Call(type, boxed.ToObject, dictionary));
        Assert.Same(array, Call(type, boxed.ToObject, array));
        Assert.Same(dictionary, Call(type, boxed.NormalizeForeignEvalValue, dictionary));
        Assert.Null(Call(type, boxed.UnwrapIfBoxed, (object?)null));
        Assert.Same(undefined, Call(type, boxed.UnwrapIfBoxed, undefined));
    }

    [Theory]
    [InlineData("Number", 7d)]
    [InlineData("Boolean", false)]
    [InlineData("String", "foreign")]
    public void ForeignEvalWrappersAreRecreatedWithNativeBrands(string tag, object value)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var foreign = new SharpTSObject(new Dictionary<string, object?>
        {
            ["__primitiveType"] = tag,
            ["__primitiveValue"] = value
        });
        var boxed = runtime.BoxedPrimitives;
        var native = Call(type, boxed.NormalizeForeignEvalValue, foreign);
        Assert.NotSame(foreign, native);
        Assert.Equal(runtime.ObjectStorage.Type.Name, native!.GetType().Name);
        Assert.Equal(true, Call(type, boxed.IsOfType, native, tag));
        Assert.Equal(value, Call(type, boxed.UnwrapIfBoxed, native));
    }

    [Fact]
    public void ForeignUndefinedIsTranslatedWhileOtherForeignValuesPassThrough()
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$Runtime")!;
        var method = runtime.BoxedPrimitives.NormalizeForeignEvalValue;
        var nativeUndefined = assembly.GetType(runtime.Sentinels.UndefinedType.Name)!.GetField("Instance")!.GetValue(null);
        Assert.Same(nativeUndefined, Call(type, method, SharpTSUndefined.Instance));
        Assert.Null(Call(type, method, (object?)null));
        Assert.Equal(7d, Call(type, method, 7d));
        var other = new SharpTSObject(new Dictionary<string, object?> { ["__primitiveType"] = "Symbol" });
        Assert.Same(other, Call(type, method, other));
    }

    [Theory]
    [InlineData("text", "text")]
    [InlineData(123d, "123")]
    [InlineData(false, "false")]
    [InlineData(null, "null")]
    public void StringReceiverUnwrappingPreservesFastAndFallbackPaths(object? value, string expected)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Equal(expected, Call(type, runtime.BoxedPrimitives.UnwrapStringReceiver, value));
        var wrapper = Call(type, runtime.BoxedPrimitives.New, "String", "boxed");
        Assert.Equal("boxed", Call(type, runtime.BoxedPrimitives.UnwrapStringReceiver, wrapper));
    }

    [Fact]
    public void ReusedEmitterKeepsWrapperDeclarationsAndObjectsIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime(null, false, emitter);
        var second = EmitRuntime("const value=1;", false, emitter);
        AssertFrozen(first.BoxedPrimitives);
        AssertFrozen(second.BoxedPrimitives);
        foreach (var property in Handles)
            Assert.NotSame(property.GetValue(first.BoxedPrimitives), property.GetValue(second.BoxedPrimitives));
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        Verify(secondBytes);
        var firstType = Assembly.Load(firstBytes.ToArray()).GetType("$Runtime")!;
        var secondType = Assembly.Load(secondBytes.ToArray()).GetType("$Runtime")!;
        var firstBox = Call(firstType, first.BoxedPrimitives.ToObject, 1d);
        var secondBox = Call(secondType, second.BoxedPrimitives.ToObject, 1d);
        Assert.NotEqual(firstBox!.GetType(), secondBox!.GetType());
        Assert.Equal(1d, Call(secondType, second.BoxedPrimitives.UnwrapIfBoxed, secondBox));
    }

    private static object? Call(Type type, MethodInfo method, params object?[] arguments) =>
        type.GetMethod(method.Name)!.Invoke(null, arguments);

    private static void InvokeEmitter(string name, RuntimeEmitter emitter, params object?[] arguments) =>
        typeof(RuntimeEmitter).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(emitter, arguments);

    private static object CreateInputs(string name, params object?[] arguments) =>
        Activator.CreateInstance(typeof(RuntimeEmitter).GetNestedType(name, BindingFlags.NonPublic)!, arguments)!;

    private static (EmittedRuntime Runtime, TypeBuilder Helper, RuntimeEmitter Emitter)
        CreateExplicitDependencyFixture(bool includeDependency)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"explicit_boxed_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Detect("Object(42n);new Date(0);"));
        var helper = module.DefineType("ExplicitBoxed", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        // Exercise each scoped input with the opposite global feature selection.
        typeof(RuntimeEmitter).GetField("_features", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(emitter,
            includeDependency ? Detect("const value=1;") : RuntimeFeatureSet.EmitEverything());
        return (runtime, helper, emitter);
    }

    private static EmittedBoxedPrimitiveRuntime CreateDeclarations(string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"boxed_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("BoxedDeclarations", TypeAttributes.Public);
        var boxed = new EmittedBoxedPrimitiveRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omitted) continue;
            var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            method.GetILGenerator().Emit(OpCodes.Ret);
            property.SetValue(boxed, method);
        }
        return boxed;
    }

    private static void AssertFrozen(EmittedBoxedPrimitiveRuntime boxed)
    {
        Assert.True(boxed.IsComplete);
        Assert.Throws<InvalidOperationException>(boxed.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(boxed);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(boxed, value));
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
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"boxed_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
