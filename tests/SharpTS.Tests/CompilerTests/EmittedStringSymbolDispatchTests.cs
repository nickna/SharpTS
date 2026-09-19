using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedStringSymbolDispatchTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void ExistingStringOwnerRejectsDuplicateProtocolDeclaration()
    {
        var strings = new EmittedRuntime().Strings;
        Assert.Null(typeof(EmittedRuntime).GetProperty("StringTryInvokeSymbolMethod"));
        var property = typeof(EmittedStringRuntime).GetProperty("TryInvokeSymbolMethod")!;
        Assert.Throws<InvalidOperationException>(() => strings.TryInvokeSymbolMethod);
        var method = NewAssembly().DefineDynamicModule("main").DefineType("Probe")
            .DefineMethod("Hook", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        property.SetValue(strings, method);
        Assert.Same(method, strings.TryInvokeSymbolMethod);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(strings, method)).InnerException);
        Assert.False(strings.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedHelperUsesSuppliedMetadataAndOptionalRegExpTypeWithoutOtherStringHandles(bool nativeType)
    {
        var helper = typeof(RuntimeEmitter).GetMethod("EmitStringTryInvokeSymbolMethod", PrivateInstance)!;
        var inputsType = typeof(RuntimeEmitter).GetNestedType("StringSymbolDispatchInputs", BindingFlags.NonPublic)!;
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedStringRuntime), inputsType }, helper.GetParameters().Select(p => p.ParameterType));
        var constructor = Assert.Single(inputsType.GetConstructors());
        Assert.Equal(new[] { typeof(Type), typeof(Type), typeof(MethodInfo), typeof(Type), typeof(MethodInfo), typeof(MethodInfo), typeof(MethodInfo) }, constructor.GetParameters().Select(p => p.ParameterType));
        var assembly = NewAssembly(); var type = assembly.DefineDynamicModule("main").DefineType("Probe", TypeAttributes.Public);
        FieldBuilder Field(string name, Type valueType) => type.DefineField(name, valueType, FieldAttributes.Public | FieldAttributes.Static);
        var classification = Field("Classification", typeof(string)); var classified = Field("Classified", typeof(object));
        var storage = Field("Storage", typeof(Dictionary<object, object>)); var storageReceiver = Field("StorageReceiver", typeof(object));
        var choice = Field("Choice", typeof(object)); var indexReceiver = Field("IndexReceiver", typeof(object)); var indexSymbol = Field("IndexSymbol", typeof(object));
        var result = Field("Result", typeof(object)); var receiver = Field("Receiver", typeof(object)); var selectedMethod = Field("SelectedMethod", typeof(object)); var arguments = Field("Arguments", typeof(object[]));
        MethodBuilder Forward(string name, FieldBuilder returned, params FieldBuilder[] captured)
        {
            var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returned.FieldType, captured.Select(f => f.FieldType).ToArray());
            var il = method.GetILGenerator();
            for (int i = 0; i < captured.Length; i++) { il.Emit(OpCodes.Ldarg, (short)i); il.Emit(OpCodes.Stsfld, captured[i]); }
            il.Emit(OpCodes.Ldsfld, returned); il.Emit(OpCodes.Ret); return method;
        }
        var typeOf = Forward("Classify", classification, classified);
        var getStorage = Forward("Symbols", storage, storageReceiver);
        var getIndex = Forward("Index", choice, indexReceiver, indexSymbol);
        var invoke = Forward("Invoke", result, receiver, selectedMethod, arguments);
        var inputs = constructor.Invoke([typeof(object), typeof(Uri), typeOf, nativeType ? typeof(string) : null, getStorage, getIndex, invoke]);
        var strings = new EmittedRuntime().Strings;
        helper.Invoke(new RuntimeEmitter(TypeProvider.Runtime), [type, strings, inputs]);
        Assert.Same(type, strings.TryInvokeSymbolMethod.DeclaringType);
        Assert.Throws<InvalidOperationException>(() => strings.CharAt);
        Assert.False(strings.IsComplete); type.CreateType();
        var loaded = SaveVerifyLoad(assembly).GetType("Probe")!;
        void Set(string name, object? value) => loaded.GetField(name)!.SetValue(null, value);
        object? Get(string name) => loaded.GetField(name)!.GetValue(null);
        var symbol = new object(); var methodMarker = new object(); var output = new object(); object[] values = ["abc", "X"];
        var symbols = new Dictionary<object, object> { [symbol] = methodMarker };
        Set("Storage", symbols); Set("Classification", "object"); Set("Choice", methodMarker); Set("Result", output);
        (object? Value, bool Invoked, bool Own) Dispatch(object? candidate)
        {
            object?[] args = [candidate, symbol, values, true, true];
            var value = loaded.GetMethod("StringTryInvokeSymbolMethod")!.Invoke(null, args);
            return (value, (bool)args[3]!, (bool)args[4]!);
        }
        foreach (object? value in new object?[] { null, new Uri("https://example.test") })
        {
            var row = Dispatch(value); Assert.Null(row.Value); Assert.False(row.Invoked); Assert.False(row.Own);
        }
        Assert.Null(Get("Classified")); Assert.Null(Get("IndexReceiver"));
        Set("Classification", "number"); var primitive = Dispatch(42);
        Assert.Equal(42, Get("Classified")); Assert.Null(primitive.Value); Assert.False(primitive.Invoked); Assert.False(primitive.Own); Assert.Null(Get("IndexReceiver"));
        Set("Classification", "object"); object candidate = "selected native type";
        var called = Dispatch(candidate); Assert.Same(output, called.Value); Assert.True(called.Invoked); Assert.Equal(nativeType, called.Own);
        Assert.Same(candidate, Get("IndexReceiver")); Assert.Same(symbol, Get("IndexSymbol"));
        Assert.Same(candidate, Get("Receiver")); Assert.Same(methodMarker, Get("SelectedMethod")); Assert.Same(values, Get("Arguments"));
        Assert.Equal(nativeType, Get("StorageReceiver") is not null);
        foreach (object? value in new object?[] { null, new Uri("https://example.test") })
        {
            Set("Choice", value); Set("Receiver", null); var row = Dispatch(candidate);
            Assert.Null(row.Value); Assert.False(row.Invoked); Assert.Equal(nativeType, row.Own); Assert.Null(Get("Receiver"));
        }
        Set("Choice", methodMarker); Set("Classification", "function"); Set("StorageReceiver", null);
        var functionCandidate = new object(); var function = Dispatch(functionCandidate);
        Assert.Same(output, function.Value); Assert.True(function.Invoked); Assert.False(function.Own); Assert.Same(functionCandidate, Get("Receiver")); Assert.Null(Get("StorageReceiver"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsRequiredProtocolHandleFreshAcrossOptionalRegExp(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedStringRuntime>(); var handles = new HashSet<MethodBuilder>();
        foreach (string source in new[] { "const n=1;", "new RegExp('b');", "const n=2;" })
        {
            var assembly = NewAssembly(); var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = emitter.EmitAll(assembly.DefineDynamicModule("main"), features);
            Assert.True(owners.Add(runtime.Strings)); Assert.True(runtime.Strings.IsComplete); Assert.True(handles.Add(runtime.Strings.TryInvokeSymbolMethod));
            Assert.Same(assembly, runtime.Strings.TryInvokeSymbolMethod.Module.Assembly); Assert.Same(runtime.RuntimeClass.Type, runtime.Strings.TryInvokeSymbolMethod.DeclaringType);
            var loaded = SaveVerifyLoad(assembly); var type = loaded.GetType("$Runtime")!; var method = type.GetMethod("StringTryInvokeSymbolMethod")!;
            Assert.True(method.IsPublic && method.IsStatic); Assert.Equal(typeof(object), method.ReturnType);
            Assert.True(method.MetadataToken > type.GetMethod("StringLocaleCompare")!.MetadataToken);
            Assert.Equal(features.UsesRegExp, loaded.GetType("$RegExp") is not null);
            var field = runtime.Symbols.Match; var symbol = loaded.GetType(field.DeclaringType!.FullName!)!.GetField(field.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Assert.Equal(new[] { typeof(object), symbol.GetType(), typeof(object[]), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() }, method.GetParameters().Select(p => p.ParameterType));
            object?[] arguments = [null, symbol, Array.Empty<object>(), true, true];
            Assert.Null(method.Invoke(null, arguments)); Assert.Equal(false, arguments[3]); Assert.Equal(false, arguments[4]);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"string_symbol_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
