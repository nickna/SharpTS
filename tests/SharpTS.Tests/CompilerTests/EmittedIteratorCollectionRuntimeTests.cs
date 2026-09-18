using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedIteratorCollectionRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Slots => typeof(EmittedIteratorCollectionRuntime).GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();

    [Theory]
    [InlineData("ToList")]
    [InlineData("IntoList")]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedRuntime().IteratorCollection;
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        var handle = type.DefineMethod("Method", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        var property = Slots.Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Slots.Where(p => p.Name != missing)) other.SetValue(owner, handle);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        property.SetValue(owner, handle); Assert.Same(handle, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, handle));
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        foreach (var slot in Slots) Expect<InvalidOperationException>(() => slot.SetValue(owner, handle));
    }

    [Fact]
    public void RequiredOwnerIsFreshForEachCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.IteratorCollection, second.IteratorCollection);
        Assert.False(first.IteratorCollection.IsComplete); Assert.False(second.IteratorCollection.IsComplete);
    }

    [Fact]
    public void ForwardDeclarationsCanBeReferencedBeforeBodyEmission()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main"); var type = module.DefineType("Forward", TypeAttributes.Public);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var owner = new EmittedRuntime().IteratorCollection;
        typeof(RuntimeEmitter).GetMethod("DeclareIterateToList", Members)!.Invoke(emitter, [type, owner, typeof(object)]);
        Assert.False(owner.IsComplete);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static, typeof(List<object>), Type.EmptyTypes);
        var il = caller.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, owner.ToList); il.Emit(OpCodes.Ret);
        foreach (var method in new[] { owner.ToList, owner.IntoList })
        {
            il = method.GetILGenerator(); il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!); il.Emit(OpCodes.Ret);
        }
        owner.CompleteEmission(); type.CreateType(); var loaded = SaveVerifyLoad(builder);
        Assert.Empty((List<object>)loaded.GetType("Forward")!.GetMethod("Call")!.Invoke(null, null)!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesCollectionAbiAndOutputIsolation(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<EmittedIteratorCollectionRuntime>();
        foreach (string source in new[] { "const n=1;", "Buffer.from('x');new Uint8Array(2);new Map();", "const a=[1];a[Symbol.iterator]=function*(){yield 2;};", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(source));
            Assert.True(owners.Add(runtime.IteratorCollection)); Assert.True(runtime.IteratorCollection.IsComplete);
            foreach (var slot in Slots) Assert.Same(builder, ((MemberInfo)slot.GetValue(runtime.IteratorCollection)!).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!; var symbol = Symbol(loaded, runtime);
            var toList = type.GetMethod("IterateToList")!; var intoList = type.GetMethod("IterateIntoList")!;
            Assert.True(toList.IsPublic && toList.IsStatic && intoList.IsPublic && intoList.IsStatic);
            Assert.Equal(typeof(List<object>), toList.ReturnType); Assert.Equal(typeof(List<object>), intoList.ReturnType);
            Assert.Equal(new[] { typeof(object), symbol.GetType(), typeof(Type) }, toList.GetParameters().Select(p => p.ParameterType));
            Assert.Equal(new[] { typeof(object), symbol.GetType(), typeof(Type), typeof(List<object>) }, intoList.GetParameters().Select(p => p.ParameterType));
            var original = new List<object> { 1d, 2d }; Assert.Equal(original, (List<object>)toList.Invoke(null, [original, symbol, type])!);
            var destination = new List<object> { 0d }; Assert.Same(destination, intoList.Invoke(null, [original, symbol, type, destination]));
            Assert.Equal(new object[] { 0d, 1d, 2d }, destination); Assert.Equal(new object[] { 1d, 2d }, original);
            Assert.Equal(new object[] { "A", "\U0001f600", "B" }, (List<object>)toList.Invoke(null, ["A\U0001f600B", symbol, type])!);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SuppliedMutationAndBufferSelectionOverrideGlobalFlags(bool mutation, bool bufferSelected)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main"); var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var features = Detect("Buffer.from('x');new Uint8Array(2);new Map();"); var runtime = emitter.EmitAll(module, features);
        features.UsesArrayPrototypeMutation = !mutation; features.UsesBuffer = !bufferSelected;
        var type = module.DefineType("CollectionProbe", TypeAttributes.Public); var owner = new EmittedRuntime().IteratorCollection;
        typeof(RuntimeEmitter).GetMethod("DeclareIterateToList", Members)!.Invoke(emitter, [type, owner, runtime.Symbols.Type]);
        var helper = typeof(RuntimeEmitter).GetMethod("EmitIteratorMethodsAdvanced", Members)!;
        var inputs = Activator.CreateInstance(helper.GetParameters()[2].ParameterType,
            runtime.ArrayStorage, runtime.CollectionKeys, runtime.Errors, runtime.Invocation,
            runtime.IteratorProtocol, runtime.IteratorRecords, runtime.ObjectRead,
            runtime.Sentinels.UndefinedType, runtime.Sentinels.UndefinedInstance, runtime.TypedArrays.Implementation,
            bufferSelected ? runtime.RequireBuffer() : null, runtime.Map is not null, mutation)!;
        helper.Invoke(emitter, [type, owner, inputs]); Assert.False(owner.IsComplete); owner.CompleteEmission(); type.CreateType();
        var loaded = SaveVerifyLoad(builder); var runtimeType = loaded.GetType("$Runtime")!; var symbol = Symbol(loaded, runtime); var probe = loaded.GetType("CollectionProbe")!;
        var list = new List<object> { 1d, 2d };
        var symbols = (Dictionary<object, object>)runtimeType.GetMethod("GetSymbolDict", Members)!.Invoke(null, [list])!;
        symbols[symbol] = new Func<object[], object>(_ =>
        {
            int index = 0;
            return new Dictionary<string, object> { ["next"] = new Func<object[], object>(_ => new Dictionary<string, object> { ["value"] = 9d, ["done"] = index++ > 0 }) };
        });
        var toList = probe.GetMethod("IterateToList")!; var intoList = probe.GetMethod("IterateIntoList")!;
        Assert.Equal(mutation ? new object[] { 9d } : new object[] { 1d, 2d }, (List<object>)toList.Invoke(null, [list, symbol, runtimeType])!);
        var destination = new List<object> { 0d }; Assert.Same(destination, intoList.Invoke(null, [list, symbol, runtimeType, destination]));
        Assert.Equal(mutation ? new object[] { 0d, 9d } : new object[] { 0d, 1d, 2d }, destination);
        var buffer = Activator.CreateInstance(loaded.GetType(runtime.RequireBuffer().Type.FullName!)!, new byte[] { 4, 5 })!;
        if (bufferSelected) Assert.Equal(new object[] { 4d, 5d }, (List<object>)toList.Invoke(null, [buffer, symbol, runtimeType])!);
        else Assert.Contains("Value is not iterable", Assert.Throws<TargetInvocationException>(() => toList.Invoke(null, [buffer, symbol, runtimeType])).InnerException!.Message);
    }

    [Fact]
    public void ScopedCollectionAndArrayHelpersDoNotRetainTheWholeHolder()
    {
        Assert.Equal(2, Slots.Length);
        foreach (string old in new[] { "IterateToList", "IterateIntoList" }) Assert.Null(typeof(EmittedRuntime).GetProperty(old));
        foreach (string name in new[] { "DeclareIterateToList", "EmitIteratorMethodsAdvanced", "EmitIterateToList", "EmitIterateToListBody", "EmitAppendDenseIteratorSource", "EmitArrayIteratorType" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var input in method.GetParameters().Where(p => p.Name == "inputs"))
                Assert.DoesNotContain(input.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
    }

    private static object Symbol(Assembly loaded, EmittedRuntime runtime) => loaded.GetType(runtime.Symbols.Iterator.DeclaringType!.FullName!)!.GetField(runtime.Symbols.Iterator.Name, Members)!.GetValue(null)!;
    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"iterator_collection_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors)); return Assembly.Load(bytes.ToArray());
    }
}
