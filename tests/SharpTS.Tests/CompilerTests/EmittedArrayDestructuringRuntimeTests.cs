using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedArrayDestructuringRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesDestructuringAbiAndOutputIsolation(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedArrayOperationsRuntime>();
        foreach (string source in new[] { "const n=1;", "Buffer.from('x');new Uint8Array(2);new Set([1]);", "const a:any=[];a[Symbol.iterator]=null;", "const n=2;" })
        {
            var builder = NewAssembly();
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var features = new RuntimeFeatureDetector().Detect(statements);
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
            Assert.True(owners.Add(runtime.ArrayOperations));
            Assert.True(runtime.ArrayOperations.IsComplete);
            var handle = runtime.ArrayOperations.DestructureSource;
            Assert.Same(builder, handle.Module.Assembly);
            Assert.Same(runtime.RuntimeClass.Type, handle.DeclaringType);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!;
            var symbol = loaded.GetType(runtime.Symbols.Iterator.DeclaringType!.FullName!)!
                .GetField(runtime.Symbols.Iterator.Name, Members)!.GetValue(null)!;
            var method = type.GetMethod("ArrayDestructureSource")!;
            Assert.True(method.IsPublic && method.IsStatic); Assert.Equal(typeof(object), method.ReturnType);
            Assert.Equal(new[] { typeof(object), symbol.GetType(), typeof(Type) }, method.GetParameters().Select(p => p.ParameterType));
            object Normalize(object? value) => method.Invoke(null, [value, symbol, type])!;
            var list = new List<object> { 1d, 2d };
            var typed = new List<double> { 3d, 4d };
            if (features.UsesArrayPrototypeMutation)
            {
                Assert.NotSame(list, Normalize(list)); Assert.NotSame(typed, Normalize(typed));
                Assert.Equal(list, ((IList)Normalize(list)).Cast<object>());
                Assert.Equal(typed.Cast<object>(), ((IList)Normalize(typed)).Cast<object>());
            }
            else
            {
                Assert.Same(list, Normalize(list)); Assert.Same(typed, Normalize(typed));
            }
            Assert.Equal(new object[] { "A", "\U0001f600", "B" }, ((IList)Normalize("A\U0001f600B")).Cast<object>());
            Assert.Equal(new object[] { 0d, 1d, 2d }, ((IList)Normalize(new Queue<object>([0d, 1d, 2d]))).Cast<object>());
            Assert.Equal(new object[] { 0d, 1d, 2d }, ((IList)Normalize(
                Enumerable.Range(0, 3).Select(n => (object)(double)n).GetEnumerator())).Cast<object>());
            var active = Enumerable.Range(0, 3).Select(n => (object)(double)n).GetEnumerator();
            Assert.True(active.MoveNext());
            Assert.Equal(new object[] { 1d, 2d }, ((IList)Normalize(active)).Cast<object>());
            Assert.Empty(((IList)Normalize(active)).Cast<object>());
            var undefined = loaded.GetType(runtime.Sentinels.UndefinedType.FullName!)!
                .GetField(runtime.Sentinels.UndefinedInstance.Name, Members)!.GetValue(null);
            foreach (var invalid in new object?[] { null, undefined, 4d, false,
                new Dictionary<string, object>(),
                new Dictionary<string, object> { ["next"] = new Func<object[], object>(_ => new Dictionary<string, object> { ["done"] = true }) } })
                Assert.Contains("TypeError:", Assert.Throws<TargetInvocationException>(() => Normalize(invalid)).InnerException!.Message);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void NormalizerUsesSuppliedCollectionAndStorageHandles()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var storage = module.DefineType("Storage", TypeAttributes.Public);
        var values = storage.DefineField("Values", typeof(List<object>), FieldAttributes.Public);
        var ctor = storage.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(List<object>)]);
        var il = ctor.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, values); il.Emit(OpCodes.Ret);
        var type = module.DefineType("Probe", TypeAttributes.Public);
        var captured = type.DefineField("Captured", typeof(object[]), FieldAttributes.Public | FieldAttributes.Static);
        var result = type.DefineField("Result", typeof(List<object>), FieldAttributes.Public | FieldAttributes.Static);
        var collect = type.DefineMethod("Collect", MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>), [typeof(object), typeof(Guid), typeof(Type)]);
        il = collect.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < 3; i++)
        {
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4, i); il.Emit(OpCodes.Ldarg, (short)i);
            if (i == 1) il.Emit(OpCodes.Box, typeof(Guid));
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Stsfld, captured); il.Emit(OpCodes.Ldsfld, result); il.Emit(OpCodes.Ret);
        var getIterator = type.DefineMethod("GetIterator", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object), typeof(Guid)]);
        il = getIterator.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var dependencies = emitter.EmitAll(module);
        var inputType = typeof(RuntimeEmitter).GetNestedType("ArrayDestructureInputs", Members)!;
        var inputs = Activator.CreateInstance(inputType, [typeof(Guid), collect, ctor,
            dependencies.Errors, getIterator, dependencies.Invocation.Method,
            dependencies.IteratorWrappers.Ctor, dependencies.ObjectFields.Interface,
            dependencies.Sentinels.UndefinedType, null, null, false]);
        var arrays = new EmittedRuntime().ArrayOperations;
        typeof(RuntimeEmitter).GetMethod("EmitArrayDestructureSource", Members)!
            .Invoke(emitter, [type, arrays, inputs]);
        Assert.False(arrays.IsComplete); Assert.Same(type, arrays.DestructureSource.DeclaringType);
        storage.CreateType(); type.CreateType(); var loaded = SaveVerifyLoad(builder); var probe = loaded.GetType("Probe")!;
        var method = probe.GetMethod("ArrayDestructureSource")!; var symbol = Guid.NewGuid();
        var original = new List<double> { 2d };
        Assert.Same(original, method.Invoke(null, [original, symbol, typeof(string)]));
        Assert.Null(probe.GetField("Captured")!.GetValue(null));
        var collected = new List<object> { 7d, 8d }; probe.GetField("Result")!.SetValue(null, collected);
        var normalized = method.Invoke(null, ["source", symbol, typeof(string)])!;
        Assert.Same(collected, normalized.GetType().GetField("Values")!.GetValue(normalized));
        Assert.Equal(new object[] { "source", symbol, typeof(string) }, (object[])probe.GetField("Captured")!.GetValue(null)!);
    }

    [Fact]
    public void NormalizerBoundaryHasOnlyItsOwnerAndExactInputs()
    {
        Assert.Null(typeof(EmittedRuntime).GetProperty("ArrayDestructureSource"));
        var helper = typeof(RuntimeEmitter).GetMethod("EmitArrayDestructureSource", Members)!;
        var inputs = typeof(RuntimeEmitter).GetNestedType("ArrayDestructureInputs", Members)!;
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedArrayOperationsRuntime), inputs },
            helper.GetParameters().Select(p => p.ParameterType));
        Assert.DoesNotContain(inputs.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        Assert.Equal(new[] { "ArrayCtor", "BufferType", "Errors", "GetIteratorFunction", "HasFieldsInterface", "InvokeMethod", "IterateToList", "SymbolType", "TypedArrayType", "UndefinedType", "UsesArrayPrototypeMutation", "WrapperCtor" },
            inputs.GetProperties().Select(p => p.Name).Order());
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"array_destructuring_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
