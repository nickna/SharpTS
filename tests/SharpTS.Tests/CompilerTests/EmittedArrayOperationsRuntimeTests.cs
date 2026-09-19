using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedArrayOperationsRuntimeTests
{
    [Fact]
    public void RequiredOperationsSupportForwardDeclarationsBeforeBodyEmission()
    {
        var runtime = new EmittedRuntime();
        var arrays = runtime.ArrayOperations;
        Assert.False(arrays.IsComplete);
        Assert.Contains("'BoundMethodInvoke'",
            Assert.Throws<InvalidOperationException>(() => arrays.BoundMethodInvoke).Message);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("array_operations_forward"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        emitter.EmitBoundArrayMethodTypeDefinition(module, arrays);
        var type = module.DefineType("Caller");
        emitter.DeclareArrayLikeMaterialize(type, arrays);
        emitter.DeclareArrayLikeMaterializeForIteration(type, arrays);
        emitter.DeclareLoadArrayLikeElement(type, arrays);
        emitter.DeclareHasArrayLikeProperty(type, arrays);
        var invoke = arrays.BoundMethodInvoke;
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [arrays.BoundMethodType, typeof(object[])]);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, arrays.BoundMethodInvoke);
        il.Emit(OpCodes.Ret);
        Assert.Same(invoke, arrays.BoundMethodInvoke);
        Assert.False(arrays.BoundMethodType.IsCreated());
        foreach (var method in new[] { invoke, arrays.Materialize, arrays.MaterializeForIteration,
                     arrays.LoadArrayLikeElement })
        {
            method.GetILGenerator().Emit(OpCodes.Ldnull);
            method.GetILGenerator().Emit(OpCodes.Ret);
        }
        arrays.HasArrayLikeProperty.GetILGenerator().Emit(OpCodes.Ldc_I4_0);
        arrays.HasArrayLikeProperty.GetILGenerator().Emit(OpCodes.Ret);
        arrays.BoundMethodType.CreateType();
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);

        Assert.Throws<InvalidOperationException>(arrays.CompleteEmission);
        Assert.False(arrays.IsComplete);
        Assert.Throws<ArgumentNullException>(() => arrays.BoundMethodInvoke = null!);
        Assert.Same(invoke, arrays.BoundMethodInvoke);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ArrayOperations))!.SetMethod);
    }

    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedArrayOperationsRuntime).GetProperties()
        .Where(property => property.Name != nameof(EmittedArrayOperationsRuntime.IsComplete));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void CompletionRejectsEachMissingDeclarationAndCanBeRetried(string missingHandle)
    {
        var arrays = CreateDeclarations(missingHandle);
        var property = typeof(EmittedArrayOperationsRuntime).GetProperty(missingHandle)!;
        var readError = Assert.Throws<TargetInvocationException>(() => property.GetValue(arrays));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(readError.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(arrays.CompleteEmission).Message);
        Assert.False(arrays.IsComplete);

        property.SetValue(arrays, property.GetValue(CreateDeclarations()));
        arrays.CompleteEmission();
        AssertFrozen(arrays);
    }

    [Theory]
    [InlineData("console.log(1);")]
    [InlineData("const values = [1, 2];")]
    [InlineData("import * as dns from 'dns';")]
    [InlineData("import * as crypto from 'crypto';")]
    [InlineData("new Uint8Array(2);")]
    [InlineData(null)]
    public void MinimalAndFeatureRichEmissionCompleteOperations(string? source)
    {
        var runtime = EmitRuntime(source);
        AssertComplete(runtime);
        if (source == "console.log(1);")
        {
            // Array operations are required even when optional feature families are absent.
            Assert.Null(runtime.Promise);
            Assert.Null(runtime.Dns);
            Assert.Null(runtime.Net);
            Assert.Null(runtime.Tls);
        }
    }

    [Fact]
    public void HostedEmissionCompletesOperations() => AssertComplete(EmitRuntime("console.log(1);", hosted: true));

    [Fact]
    public void ArrayOperationsDoNotIntroduceGuestRuntimeDependencies()
    {
        var runtime = EmitRuntime("const values = [1, 2];");
        Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, runtime.RequiredSharpTSRuntimeRequirements);
        using var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(stream);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.AssemblyReferences,
            handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == "SharpTS");
    }

    [Fact]
    public void StaticDynamicAndIteratorOperationsVerifyAndRunStandalone()
    {
        const string source = """
            const values = Array.from({ 0: 3, 1: 1, 2: 2, length: 3 });
            console.log(Array.isArray(values), values.map(x => x * 2).join(','));
            const factory: any = Array.from;
            console.log(factory([8, 9]).join(','));
            const dynamic: any = values;
            const push = dynamic.push;
            push.call(dynamic, 4);
            console.log(dynamic.toSorted((a: number, b: number) => a - b).join(','));
            const map: any = Array.prototype.map;
            console.log(map.call(values, (x: number) => x + 1).join(','));
            const iterator = values.values();
            console.log(iterator.next().value);
            values[1] = 7;
            console.log(iterator.next().value, values.reduce((a, b) => a + b, 0));
            console.log(values.filter(x => x > 2).join(','), values.slice(1).join(','));
            console.log(values.toSpliced(1, 1, 5).join(','), values.toReversed().join(','));
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("true 6,2,4\n8,9\n1,2,3,4\n4,2,3,5\n3\n7 16\n3,7,4 7,2,4\n3,5,2,4 4,2,7,3\n",
            TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void LazyPrototypeReceiversAndCallbackContextVerifyAndRunStandalone()
    {
        const string source = """
            const receiver: any = { length: 3, 0: 2, 2: 4 };
            let reads = 0;
            Object.defineProperty(receiver, '1', {
                get: function() { reads++; return 3; }, configurable: true
            });
            const context = { scale: 10 };
            const result = Array.prototype.map.call(receiver,
                function(value: number, index: number, original: any) {
                    console.log(index, original === receiver, this.scale);
                    return value * this.scale;
                }, context);
            console.log(result.join(','), reads > 0);
            delete receiver[1];
            const sparse = Array.prototype.map.call(receiver, (x: number) => x + 1);
            console.log(sparse.length, sparse.join(','));
            console.log([1, 2].map(x => x + 5).join(','));
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("0 true 10\n1 true 10\n2 true 10\n20,30,40 true\n3 3,,5\n6,7\n",
            TestHarness.RunCompiledStandalone(source));
    }

    private static EmittedArrayOperationsRuntime CreateDeclarations(string? missingHandle = null)
    {
        var arrays = new EmittedArrayOperationsRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("array_operations_incomplete"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(arrays, handle);
        }
        return arrays;
    }

    private static void AssertFrozen(EmittedArrayOperationsRuntime arrays)
    {
        Assert.True(arrays.IsComplete);
        foreach (var property in Handles)
        {
            var value = property.GetValue(arrays);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var exception = Assert.Throws<TargetInvocationException>(() => property.SetValue(arrays, value));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }
        Assert.Throws<InvalidOperationException>(arrays.CompleteEmission);
    }

    private static void AssertComplete(EmittedRuntime runtime)
    {
        var arrays = runtime.ArrayOperations;
        AssertFrozen(arrays);
        Assert.Equal("$BoundArrayMethod", arrays.BoundMethodType.Name);
        Assert.True(arrays.BoundMethodType.IsCreated());
        Assert.Same(arrays.BoundMethodType, arrays.BoundMethodCtor.DeclaringType);
        Assert.Same(arrays.BoundMethodType, arrays.BoundMethodInvoke.DeclaringType);
        Assert.True(Assert.IsAssignableFrom<TypeBuilder>(arrays.IteratorCtor.DeclaringType).IsCreated());
        Assert.Same(runtime.RuntimeClass.Type, arrays.PrototypePopulateMethod.DeclaringType);
        Assert.Same(runtime.RuntimeClass.Type, arrays.CurrentReceiverField.DeclaringType);
        Assert.Equal("_currentArrayLikeReceiver", arrays.CurrentReceiverField.Name);
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"array_operations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
