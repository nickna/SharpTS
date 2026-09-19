using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedTypedArrayRuntimeTests
{
    private static readonly string[] NumericElements =
        ["Int8", "Uint8", "Int16", "Uint16", "Int32", "Uint32", "Float32", "Float64"];
    private static readonly string[] ConcreteNames =
        ["Int8Array", "Uint8Array", "Uint8ClampedArray", "Int16Array", "Uint16Array", "Int32Array",
         "Uint32Array", "Float32Array", "Float64Array", "BigInt64Array", "BigUint64Array"];

    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedTypedArrayImplementation).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });
    public static IEnumerable<object[]> RegistryEntries => RegistryNames.SelectMany(registry =>
        Keys(registry).Select(key => new object[] { registry, key }));
    public static IEnumerable<object[]> RegistryCases => RegistryNames.Select(name => new object[] { name });
    public static IEnumerable<object[]> NumericKinds => ConcreteNames
        .Where(name => !name.StartsWith("Big", StringComparison.Ordinal)).Select(name => new object[] { name });

    private static readonly string[] RegistryNames =
        ["GetUnboxedByElement", "SetUnboxedByElement", "FromObjectHelpers", "FromBufferHelpers"];

    [Fact]
    public void RequiredDetectionSupportsForwardCallsAndFreezesWithoutAnImplementation()
    {
        var arrays = new EmittedRuntime().TypedArrays;
        Assert.Null(arrays.Implementation);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(arrays.RequireImplementation).Message);
        Assert.Contains("'IsTypedArray'", Assert.Throws<InvalidOperationException>(() => arrays.IsTypedArray).Message);
        Assert.Contains("'IsTypedArray'", Assert.Throws<InvalidOperationException>(arrays.CompleteEmission).Message);
        Assert.False(arrays.IsComplete);
        Assert.Throws<ArgumentNullException>(() => arrays.IsTypedArray = null!);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("typedarray_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        arrays.IsTypedArray = type.DefineMethod("IsTypedArray", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), [typeof(object)]);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Ldnull);
        caller.GetILGenerator().Emit(OpCodes.Call, arrays.IsTypedArray);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(type.IsCreated());
        arrays.IsTypedArray.GetILGenerator().Emit(OpCodes.Ldc_I4_0);
        arrays.IsTypedArray.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        Assert.Equal(false, Assembly.Load(stream.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
        arrays.CompleteEmission();
        AssertFrozen(arrays);
    }

    [Fact]
    public void ImplementationStartsOnceAndCannotCompleteWithoutDetection()
    {
        var runtime = new EmittedRuntime();
        var arrays = runtime.TypedArrays;
        arrays.BeginImplementationEmission();
        Assert.Same(arrays.Implementation, arrays.RequireImplementation());
        Assert.Throws<InvalidOperationException>(arrays.BeginImplementationEmission);
        Assert.Contains("'IsTypedArray'", Assert.Throws<InvalidOperationException>(arrays.CompleteEmission).Message);
        Assert.False(arrays.IsComplete);
        Assert.False(arrays.RequireImplementation().IsComplete);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.TypedArrays))!.SetMethod);
        Assert.False(typeof(EmittedTypedArrayRuntime).GetProperty(nameof(EmittedTypedArrayRuntime.Implementation))!.SetMethod!.IsPublic);
    }

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingHandleRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var arrays = CreateDeclarations(missingHandle: missingHandle);
        var implementation = arrays.RequireImplementation();
        var property = typeof(EmittedTypedArrayImplementation).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(implementation));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(arrays.CompleteEmission).Message);
        Assert.False(arrays.IsComplete);
        Assert.False(implementation.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(implementation, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(implementation, property.GetValue(CreateDeclarations().RequireImplementation()));
        arrays.CompleteEmission();
        AssertFrozen(arrays);
    }

    [Theory]
    [MemberData(nameof(RegistryEntries))]
    public void EveryMissingRegistryEntryRejectsCompletionThenAllowsRetry(string registry, string key)
    {
        var arrays = CreateDeclarations(missingRegistry: registry, missingKey: key);
        var implementation = arrays.RequireImplementation();
        Assert.False(Registry(implementation, registry).ContainsKey(key));
        Assert.Contains($"'{registry}.{key}'", Assert.Throws<InvalidOperationException>(arrays.CompleteEmission).Message);
        Assert.False(arrays.IsComplete);
        Assert.False(implementation.IsComplete);
        Register(implementation, registry, key, arrays.IsTypedArray);
        arrays.CompleteEmission();
        AssertFrozen(arrays);
    }

    [Theory]
    [MemberData(nameof(RegistryCases))]
    public void RegistriesRejectNullUnknownDuplicateAndExternalWrites(string registry)
    {
        var implementation = new EmittedTypedArrayImplementation();
        var method = CreateDeclarations().IsTypedArray;
        string key = Keys(registry)[0];
        Assert.Throws<ArgumentNullException>(() => Register(implementation, registry, null!, method));
        Assert.Throws<ArgumentNullException>(() => Register(implementation, registry, key, null!));
        Assert.Throws<ArgumentException>(() => Register(implementation, registry, "unknown", method));
        Assert.Throws<ArgumentException>(() => Register(implementation, registry, key.ToLowerInvariant(), method));
        if (registry.Contains("Unboxed", StringComparison.Ordinal))
        {
            Assert.Throws<ArgumentException>(() => Register(implementation, registry, "Uint8Clamped", method));
            Assert.Throws<ArgumentException>(() => Register(implementation, registry, "BigInt64", method));
        }
        Register(implementation, registry, key, method);
        Assert.Same(method, Registry(implementation, registry)[key]);
        Assert.Throws<ArgumentException>(() => Register(implementation, registry, key, method));
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, MethodBuilder>>(Registry(implementation, registry));
        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary[key] = method);
        Assert.Throws<NotSupportedException>(() => dictionary.Remove(key));
        Assert.Throws<NotSupportedException>(dictionary.Clear);
    }

    [Fact]
    public void ConstructorRegistryCanSupplyForwardCallsBeforeBodiesExist()
    {
        var implementation = new EmittedTypedArrayImplementation();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("typedarray_registry_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        var factory = type.DefineMethod("Factory", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object)]);
        implementation.RegisterFromObject("Uint8Array", factory);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Ldstr, "forward");
        caller.GetILGenerator().Emit(OpCodes.Call, implementation.FromObjectHelpers["Uint8Array"]);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(type.IsCreated());
        Assert.False(implementation.IsComplete);
        factory.GetILGenerator().Emit(OpCodes.Ldarg_0);
        factory.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        Assert.Equal("forward", Assembly.Load(stream.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("console.log(1);", true)]
    [InlineData("Buffer.from('data');", false)]
    [InlineData("import * as http from 'http';", false)]
    [InlineData("import * as worker from 'worker_threads';", false)]
    [InlineData("new Response('body');", false)]
    [InlineData("console.log(Array.from([1, 2]));", false)]
    public void DisabledFeatureCompletesDetectionWithoutImplementationMetadata(string source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Null(runtime.TypedArrays.Implementation);
        AssertFrozen(runtime.TypedArrays);
        using var stream = Save(runtime);
        using (var pe = new PEReader(stream, PEStreamOptions.LeaveOpen))
        {
            var reader = pe.GetMetadataReader();
            Assert.DoesNotContain(reader.TypeDefinitions,
                handle => reader.GetString(reader.GetTypeDefinition(handle).Name) is "$TypedArray" or "$BoundTypedArrayMethod" or "$Uint8Array");
            Assert.DoesNotContain(reader.MethodDefinitions,
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name) is "GetTypedArrayMember" or "CreateInt8ArrayFromObject");
        }
        var loaded = Assembly.Load(stream.ToArray());
        var detection = loaded.GetType(runtime.RuntimeClass.Type.Name)!.GetMethod(runtime.TypedArrays.IsTypedArray.Name)!;
        Assert.Equal(false, detection.Invoke(null, [null]));
        Assert.Equal(false, detection.Invoke(null, [new byte[1]]));
    }

    [Theory]
    [InlineData("function read(a: Uint8Array): number { return a[0]; } console.log(read([2] as any));", "2\n")]
    [InlineData("function write(a: Uint8Array): number { a[0] = 7; return a[0]; } console.log(write([2] as any));", "7\n")]
    [InlineData("function increment(a: Uint8Array): number { a[0]++; return a[0]; } console.log(increment([2] as any));", "3\n")]
    [InlineData("function sum(a: Uint8Array): number { let total = 0; for (let i = 0; i < 3; i++) total += a[i]; return total; } console.log(sum([1, 2, 3] as any));", "6\n")]
    [InlineData("function* items() { yield* [1, 2]; } console.log([...items()].join(','));", "1,2\n")]
    [InlineData("function* items() { yield* Buffer.from([1, 2]); } console.log([...items()].join(','));", "1,2\n")]
    public void AnnotationOnlyAndGeneratorPathsRetainFallbacksWithoutImplementation(string source, string expected)
    {
        Assert.Null(EmitRuntime(source).TypedArrays.Implementation);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal(expected, TestHarness.RunCompiledStandalone(source));
    }

    [Theory]
    [InlineData("new Uint8Array(8);", false)]
    [InlineData("new Uint8Array(8);", true)]
    [InlineData("new BigInt64Array(1);", false)]
    [InlineData("new ArrayBuffer(8);", false)]
    [InlineData("new SharedArrayBuffer(8);", false)]
    [InlineData("new DataView(new ArrayBuffer(8));", false)]
    [InlineData("ArrayBuffer.isView(null);", false)]
    [InlineData("import * as crypto from 'crypto';", false)]
    [InlineData("Atomics;", false)]
    [InlineData(null, false)]
    public void EnabledImpliedHostedAndFullEmissionCompletesEveryDeclaration(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var implementation = runtime.TypedArrays.RequireImplementation();
        AssertFrozen(runtime.TypedArrays);
        Assert.True(runtime.RequireArrayBuffer().IsComplete);
        Assert.True(runtime.RequireSharedArrayBuffer().IsComplete);
        Assert.True(runtime.RequireDataView().IsComplete);
        Assert.Equal("$TypedArray", implementation.BaseType.Name);
        Assert.Equal("$BoundTypedArrayMethod", implementation.BoundMethodType.Name);
        Assert.Same(implementation.BaseType, implementation.BufferField.DeclaringType);
        Assert.Same(implementation.BoundMethodType, implementation.BoundMethodInvoke.DeclaringType);
        foreach (var property in Handles)
        {
            var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(implementation));
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
        }
        foreach (string registry in RegistryNames)
        {
            Assert.Equal(Keys(registry).Order(), Registry(implementation, registry).Keys.Order());
            foreach (var method in Registry(implementation, registry).Values)
                Assert.True(Assert.IsAssignableFrom<TypeBuilder>(method.DeclaringType).IsCreated());
        }
        Assert.Same(implementation.Uint8ClampedArrayType, implementation.GetNumericType("Uint8Clamped"));
        Assert.Null(implementation.GetNumericType("BigInt64"));
        Assert.Null(implementation.GetNumericType("BigUint64"));
        Assert.Null(implementation.GetNumericType("unknown"));
    }

    [Theory]
    [MemberData(nameof(NumericKinds))]
    public void NumericKindsRetainConstructionViewsCopiesAndBoundMethodsInStandaloneOutput(string name)
    {
        string source = $$"""
            const values = new {{name}}([1, 2, 3]);
            const buffer = new SharedArrayBuffer(64);
            const view = new {{name}}(buffer, 8, 3);
            const set: any = view.set;
            set(values, 0);
            const copy = new {{name}}(view);
            const sub = view.subarray(1);
            sub[0] = 9;
            const sliced = view.slice(0, 2);
            view[1] = 7;
            console.log(ArrayBuffer.isView(view), view.buffer === buffer, view.byteOffset, view.length);
            console.log(copy[1].toString(), sliced[1].toString(), view[1].toString());
            const join: any = view.join;
            console.log(join('-'));
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("true true 8 3\n2 9 7\n1-7-3\n", TestHarness.RunCompiledStandalone(source));
    }

    [Theory]
    [InlineData("BigInt64")]
    [InlineData("BigUint64")]
    public void BigIntKindsRetainViewsByteCopiesAndBoundMethodsInStandaloneOutput(string kind)
    {
        // The existing boxed BigInt setter cannot accept BigInteger values. Populate the
        // backing storage through DataView to test valid BigInt views and same-kind byte copies.
        string source = $$"""
            const buffer = new SharedArrayBuffer(64);
            const data = new DataView(buffer);
            const littleEndian = {{(BitConverter.IsLittleEndian ? "true" : "false")}};
            data.set{{kind}}(8, 1n, littleEndian);
            data.set{{kind}}(16, 2n, littleEndian);
            data.set{{kind}}(24, 3n, littleEndian);
            const view = new {{kind}}Array(buffer, 8, 3);
            const copy = new {{kind}}Array(3);
            const set: any = copy.set;
            set(view, 0);
            const sub = view.subarray(1);
            const subData = new DataView(sub.buffer);
            subData.set{{kind}}(sub.byteOffset, 9n, littleEndian);
            const sliced = view.slice(0, 2);
            data.set{{kind}}(16, 7n, littleEndian);
            console.log(ArrayBuffer.isView(view), view.buffer === buffer, view.byteOffset, view.length);
            console.log(copy[1].toString(), sliced[1].toString(), view[1].toString());
            const join: any = view.join;
            console.log(join('-'));
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("true true 8 3\n2 9 7\n1-7-3\n", TestHarness.RunCompiledStandalone(source));
    }

    [Theory]
    [InlineData("Int8", -12d)]
    [InlineData("Uint8", 250d)]
    [InlineData("Int16", -1234d)]
    [InlineData("Uint16", 60000d)]
    [InlineData("Int32", -123456d)]
    [InlineData("Uint32", 4000000000d)]
    [InlineData("Float32", 1.25d)]
    [InlineData("Float64", -3.5d)]
    public void UnboxedRegistriesRetainStorageAndInlining(string element, double value)
    {
        var runtime = EmitRuntime($"new {element}Array(2);");
        var implementation = runtime.TypedArrays.RequireImplementation();
        using var stream = Save(runtime);
        var loaded = Assembly.Load(stream.ToArray());
        var helpers = loaded.GetType(runtime.RuntimeClass.Type.Name)!;
        var buffer = helpers.GetMethod(runtime.RequireArrayBuffer().Create.Name)!.Invoke(null, [64d]);
        var array = helpers.GetMethod(implementation.FromBufferHelpers[element + "Array"].Name)!.Invoke(null, [buffer, 8d, 2d]);
        var type = array!.GetType();
        var get = type.GetMethod(implementation.GetUnboxedByElement[element].Name)!;
        var set = type.GetMethod(implementation.SetUnboxedByElement[element].Name)!;
        Assert.True(get.MethodImplementationFlags.HasFlag(MethodImplAttributes.AggressiveInlining));
        Assert.True(set.MethodImplementationFlags.HasFlag(MethodImplAttributes.AggressiveInlining));
        set.Invoke(array, [1, value]);
        Assert.Equal(value, get.Invoke(array, [1]));
        Assert.Same(buffer, type.GetProperty("Buffer")!.GetValue(array));
        Assert.Equal(8, type.GetProperty("ByteOffset")!.GetValue(array));
        Assert.Equal(2, type.GetProperty("Length")!.GetValue(array));
        Assert.Equal(true, helpers.GetMethod(runtime.TypedArrays.IsTypedArray.Name)!.Invoke(null, [array]));
        Assert.Equal(false, helpers.GetMethod(runtime.TypedArrays.IsTypedArray.Name)!.Invoke(null, [buffer]));
        foreach (var field in new[] { implementation.BufferField, implementation.ByteOffsetField, implementation.LengthField, implementation.ArrayBufferField })
        {
            Assert.True(field.IsFamily);
            Assert.Same(implementation.BaseType, field.DeclaringType);
        }
    }

    [Fact]
    public void BulkMethodsClampingAndStructuredClonePassStandaloneAndILChecks()
    {
        const string source = """
            const a = new Uint8ClampedArray([300, -1, 2.5, 3.5]);
            console.log(a.join(','));
            const v: any = new Int16Array([1, 2, 3, 4]);
            v.copyWithin(1, 0, 2); v.reverse(); v.fill(7, 1, 3);
            console.log(v.join(','), v.indexOf(7), v.lastIndexOf(7), v.includes(7), v.toString());
            const clone: any = structuredClone(v);
            clone[0] = 9; console.log(v[0], clone[0]);
            const shared = new Int32Array(new SharedArrayBuffer(16));
            shared[0] = 5;
            const sharedClone: any = structuredClone(shared);
            sharedClone[0] = 8; console.log(shared[0]);
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("255,0,2,3\n4,7,7,1 1 2 true 4,7,7,1\n4 9\n8\n", TestHarness.RunCompiledStandalone(source));
    }

    private static string[] Keys(string registry) => registry.Contains("Unboxed", StringComparison.Ordinal) ? NumericElements : ConcreteNames;

    private static IReadOnlyDictionary<string, MethodBuilder> Registry(EmittedTypedArrayImplementation implementation, string name) =>
        (IReadOnlyDictionary<string, MethodBuilder>)typeof(EmittedTypedArrayImplementation).GetProperty(name)!.GetValue(implementation)!;

    private static void Register(EmittedTypedArrayImplementation implementation, string registry, string key, MethodBuilder method)
    {
        switch (registry)
        {
            case "GetUnboxedByElement": implementation.RegisterGetUnboxed(key, method); break;
            case "SetUnboxedByElement": implementation.RegisterSetUnboxed(key, method); break;
            case "FromObjectHelpers": implementation.RegisterFromObject(key, method); break;
            case "FromBufferHelpers": implementation.RegisterFromBuffer(key, method); break;
            default: throw new ArgumentException(registry);
        }
    }

    private static EmittedTypedArrayRuntime CreateDeclarations(string? missingHandle = null, string? missingRegistry = null, string? missingKey = null)
    {
        var arrays = new EmittedTypedArrayRuntime();
        arrays.BeginImplementationEmission();
        var implementation = arrays.RequireImplementation();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"typedarray_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        arrays.IsTypedArray = method;
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(implementation, handle);
        }
        foreach (string registry in RegistryNames)
            foreach (string key in Keys(registry))
                if (registry != missingRegistry || key != missingKey)
                    Register(implementation, registry, key, method);
        return arrays;
    }

    private static void AssertFrozen(EmittedTypedArrayRuntime arrays)
    {
        Assert.True(arrays.IsComplete);
        Assert.Throws<InvalidOperationException>(arrays.CompleteEmission);
        Assert.Throws<InvalidOperationException>(arrays.BeginImplementationEmission);
        Assert.Throws<InvalidOperationException>(() => arrays.IsTypedArray = arrays.IsTypedArray);
        if (arrays.Implementation is not { } implementation) return;
        Assert.True(implementation.IsComplete);
        Assert.Throws<InvalidOperationException>(implementation.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(implementation);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(implementation, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        foreach (string registry in RegistryNames)
        {
            string key = Keys(registry)[0];
            Assert.Throws<InvalidOperationException>(() => Register(implementation, registry, key, arrays.IsTypedArray));
        }
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(stream);
        stream.Position = 0;
        return stream;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"typedarray_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
