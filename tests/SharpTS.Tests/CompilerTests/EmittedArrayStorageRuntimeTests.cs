using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedArrayStorageRuntimeTests
{
    [Fact]
    public void RequiredStorageSupportsForwardDeclarationsBeforeBodyEmission()
    {
        var runtime = new EmittedRuntime();
        var arrays = runtime.ArrayStorage;
        Assert.False(arrays.IsComplete);
        Assert.Contains("'EnsureBoxed'",
            Assert.Throws<InvalidOperationException>(() => arrays.EnsureBoxed).Message);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("array_storage_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Array");
        var ensureBoxed = type.DefineMethod("EnsureBoxed", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        arrays.EnsureBoxed = ensureBoxed;
        var caller = type.DefineMethod("Caller", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, arrays.EnsureBoxed);
        il.Emit(OpCodes.Ret);
        Assert.Same(ensureBoxed, arrays.EnsureBoxed);
        Assert.False(type.IsCreated());
        ensureBoxed.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);

        Assert.Throws<InvalidOperationException>(arrays.CompleteEmission);
        Assert.False(arrays.IsComplete);
        Assert.Throws<ArgumentNullException>(() => arrays.EnsureBoxed = null!);
        Assert.Same(ensureBoxed, arrays.EnsureBoxed);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ArrayStorage))!.SetMethod);
    }

    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedArrayStorageRuntime).GetProperties()
        .Where(property => property.Name != nameof(EmittedArrayStorageRuntime.IsComplete));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void CompletionRejectsEachMissingDeclaration(string missingHandle)
    {
        var arrays = CreateDeclarations(missingHandle);
        var property = typeof(EmittedArrayStorageRuntime).GetProperty(missingHandle)!;
        var readError = Assert.Throws<TargetInvocationException>(() => property.GetValue(arrays));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(readError.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(arrays.CompleteEmission).Message);
        Assert.False(arrays.IsComplete);
    }

    public static IEnumerable<object[]> QueueHandleNames =>
        from queue in Handles.Where(property => property.PropertyType == typeof(ArrayQueueTypeInfo))
        from handle in typeof(ArrayQueueTypeInfo).GetProperties()
        where queue.Name.StartsWith("Number", StringComparison.Ordinal)
            || handle.Name is not (nameof(ArrayQueueTypeInfo.ShiftNumber) or nameof(ArrayQueueTypeInfo.GetNumber))
        select new object[] { queue.Name, handle.Name };

    [Theory]
    [MemberData(nameof(QueueHandleNames))]
    public void CompletionRejectsMissingQueueDeclarations(string queueName, string missingHandle)
    {
        var arrays = CreateDeclarations();
        var queue = typeof(EmittedArrayStorageRuntime).GetProperty(queueName)!.GetValue(arrays)!;
        typeof(ArrayQueueTypeInfo).GetProperty(missingHandle)!.SetValue(queue, null);
        Assert.Contains($"'{queueName}.{missingHandle}'",
            Assert.Throws<InvalidOperationException>(arrays.CompleteEmission).Message);
        Assert.False(arrays.IsComplete);
    }

    [Theory]
    [InlineData("console.log(1);")]
    [InlineData("const values = [1, 2];")]
    [InlineData("import * as dns from 'dns';")]
    [InlineData("import * as crypto from 'crypto';")]
    [InlineData("new Uint8Array(2);")]
    [InlineData(null)]
    public void MinimalAndFeatureRichEmissionCompleteStorage(string? source)
    {
        var runtime = EmitRuntime(source);
        AssertComplete(runtime.ArrayStorage);
        if (source == "console.log(1);")
        {
            // Array storage is shared infrastructure even when optional families are absent.
            Assert.Null(runtime.Promise);
            Assert.Null(runtime.Dns);
            Assert.Null(runtime.Net);
        }
    }

    [Fact]
    public void HostedEmissionCompletesStorage() => AssertComplete(EmitRuntime("console.log(1);", hosted: true).ArrayStorage);

    [Fact]
    public void ArrayStorageDoesNotIntroduceGuestRuntimeDependencies()
    {
        var runtime = EmitRuntime("const values = [1, 2];");
        Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, runtime.RequiredSharpTSRuntimeRequirements);
        using var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(stream);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.AssemblyReferences,
            handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == "SharpTS");
    }

    [Fact]
    public void PackedSparseSubclassAndRestStorageVerifyAndRunStandalone()
    {
        const string source = """
            function numbers(): number[] { return [1, 2, 3]; }
            const packed = numbers();
            packed[1] = 4;
            packed.push(5);
            console.log(packed.join(','));
            const dynamic: any = packed;
            dynamic[1] = 'boxed';
            delete dynamic[2];
            dynamic[100000] = 9;
            console.log(dynamic.length, 2 in dynamic, dynamic[1], dynamic[100000]);
            dynamic.length = 2;
            console.log(dynamic.join(','), 100000 in dynamic);
            class Values extends Array<number> {}
            const derived = new Values(3);
            derived[1] = 7;
            console.log(derived.length, 0 in derived, derived[1]);
            function collect(...values: number[]): number[] { return values; }
            console.log(collect(...numbers(), 6).join(','));
            Object.freeze(packed);
            console.log(Object.isFrozen(packed), packed[0]);
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("1,4,3,5\n100001 false boxed 9\n1,boxed false\n3 false 7\n1,2,3,6\ntrue 1\n",
            TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void NumberAndBooleanQueuesVerifyAndRunStandalone()
    {
        const string source = """
            function numbers(n: number): number {
                const values: number[] = [];
                for (let i = 0; i < n; i++) values.push(i);
                values.unshift(10);
                let total = 0;
                while (values.length > 0) total += values.shift();
                return total;
            }
            function booleans(): boolean {
                const values: boolean[] = [];
                values.push(true);
                values.unshift(false);
                values.shift();
                return values.shift();
            }
            function numberHoles(): number {
                const values: number[] = [];
                values[3] = 7;
                values.unshift(1);
                values.shift();
                return values.length;
            }
            function booleanHoles(): number {
                const values: boolean[] = [];
                values[3] = true;
                values.unshift(false);
                values.shift();
                return values.length;
            }
            console.log(numbers(5), booleans(), numberHoles(), booleanHoles());
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("20 true 4 4\n", TestHarness.RunCompiledStandalone(source));
    }

    private static EmittedArrayStorageRuntime CreateDeclarations(string? missingHandle = null)
    {
        var arrays = new EmittedArrayStorageRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("array_storage_incomplete"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            var numeric = property.Name.StartsWith("Number", StringComparison.Ordinal);
            object handle = property.PropertyType == typeof(ArrayQueueTypeInfo)
                ? new ArrayQueueTypeInfo(type, ctor, numeric ? ArrayElements.Double : ArrayElements.Bool,
                    method, method, method, method, method, method, numeric ? method : null, numeric ? method : null, method)
                : property.PropertyType == typeof(Type) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldInfo) ? field : method;
            property.SetValue(arrays, handle);
        }
        return arrays;
    }

    private static void AssertComplete(EmittedArrayStorageRuntime arrays)
    {
        Assert.True(arrays.IsComplete);
        Assert.Equal("$Array", arrays.Type.Name);
        Assert.Equal("$ArrayHole", arrays.HoleType.Name);
        Assert.True(Assert.IsAssignableFrom<TypeBuilder>(arrays.Type).IsCreated());
        Assert.Same(arrays.Type, arrays.Ctor.DeclaringType);
        Assert.Same(arrays.Type, arrays.EnsureBoxed.DeclaringType);
        foreach (var property in Handles)
        {
            var value = property.GetValue(arrays);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var exception = Assert.Throws<TargetInvocationException>(() => property.SetValue(arrays, value));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            if (value is ArrayQueueTypeInfo queue)
            {
                Assert.True(queue.Type.IsCreated());
                Assert.Same(queue.Type, queue.Constructor.DeclaringType);
                Assert.Equal(queue.Elements.Kind == ArrayElementsKind.Double, queue.ShiftNumber is not null);
                Assert.Equal(queue.Elements.Kind == ArrayElementsKind.Double, queue.GetNumber is not null);
            }
        }
        Assert.Throws<InvalidOperationException>(arrays.CompleteEmission);
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"array_storage_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
