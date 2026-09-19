using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectDescriptorRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static PropertyInfo[] Handles => typeof(EmittedObjectDescriptorRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationIsRepairableAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedObjectDescriptorRuntime();
        var property = typeof(EmittedObjectDescriptorRuntime).GetProperty(missing)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Fill(owner, missing); owner.MarkGetOwnPropertyDescriptorBodyEmitted();
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        var handle = Handle(); property.SetValue(owner, handle); Assert.Same(handle, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, Handle()));
        Assert.Same(handle, property.GetValue(owner)); owner.CompleteEmission(); AssertFrozen(owner);
    }

    [Fact]
    public void EarlyLookupTokenRemainsAvailableUntilItsBodyIsCompleted()
    {
        var owner = new EmittedObjectDescriptorRuntime(); Fill(owner);
        var forward = owner.GetOwnPropertyDescriptor;
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        Assert.Same(forward, owner.GetOwnPropertyDescriptor);
        owner.MarkGetOwnPropertyDescriptorBodyEmitted();
        Assert.Throws<InvalidOperationException>(owner.MarkGetOwnPropertyDescriptorBodyEmitted);
        owner.CompleteEmission(); AssertFrozen(owner);
        Assert.Throws<InvalidOperationException>(owner.MarkGetOwnPropertyDescriptorBodyEmitted);
    }

    [Fact]
    public void RequiredOwnerBelongsToOneCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.ObjectDescriptors, second.ObjectDescriptors);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectDescriptors))!.SetMethod);
        Assert.Equal(4, Handles.Length); Assert.False(first.ObjectDescriptors.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsDeclarationsLocalAndGuestDescriptorsMutable(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new List<EmittedObjectDescriptorRuntime>();
        foreach (bool optional in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(optional));
            var owner = runtime.ObjectDescriptors; Assert.DoesNotContain(owner, owners); owners.Add(owner); AssertFrozen(owner);
            foreach (var property in Handles)
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner)).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("object_descriptor_runtime_", StringComparison.Ordinal));
            var type = loaded.GetType("$Runtime")!;
            Assert.True(type.GetMethod("ObjectGetOwnPropertyDescriptor")!.MetadataToken < type.GetMethod("ObjectDefineProperty")!.MetadataToken);
            var target = new Dictionary<string, object?>();
            Assert.Same(target, Call(type, "ObjectDefineProperty", target, "x", new Dictionary<string, object?>
                { ["value"] = 7d, ["writable"] = true, ["enumerable"] = false, ["configurable"] = true }));
            var descriptor = Assert.IsType<Dictionary<string, object?>>(Call(type, "ObjectGetOwnPropertyDescriptor", target, "x"));
            Assert.Equal(7d, descriptor["value"]); Assert.Equal(true, descriptor["writable"]);
            Assert.Equal(false, descriptor["enumerable"]); Assert.Equal(true, descriptor["configurable"]);
            Call(type, "ObjectDefineProperty", target, "x", new Dictionary<string, object?> { ["writable"] = false });
            descriptor = Assert.IsType<Dictionary<string, object?>>(Call(type, "ObjectGetOwnPropertyDescriptor", target, "x"));
            Assert.Equal(7d, descriptor["value"]); Assert.Equal(false, descriptor["writable"]); Assert.Equal(false, descriptor["enumerable"]);
            Assert.Same(target, Call(type, "ObjectDefineProperties", target, new Dictionary<string, object?>
                { ["y"] = new Dictionary<string, object?> { ["value"] = 9d, ["enumerable"] = true } }));
            var all = Assert.IsType<Dictionary<string, object?>>(Call(type, "ObjectGetOwnPropertyDescriptors", target));
            Assert.Equal(2, all.Count); Assert.Equal(9d, Assert.IsType<Dictionary<string, object?>>(all["y"])["value"]);
            var error = Assert.Throws<TargetInvocationException>(() => Call(type, "ObjectDefineProperty", null, "bad", new Dictionary<string, object?>()));
            Assert.Equal(loaded.GetType("$TypeError"), Call(type, "WrapException", error.InnerException!)!.GetType());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FunctionReceiverFollowsSuppliedPromiseTypes(bool selected, bool supplied)
    {
        var builder = NewAssembly(); var features = Detect(selected); var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
        var probe = runtime.RuntimeClass.Type.DefineNestedType("FunctionDescriptorProbe", TypeAttributes.NestedPublic);
        var resolve = SimpleReceiver(probe, "SuppliedResolve"); var reject = SimpleReceiver(probe, "SuppliedReject");
        var promise = supplied ? new EmittedPromiseRuntime { ResolveCallbackType = resolve, RejectCallbackType = reject } : null;
        var getProperty = probe.DefineMethod("SuppliedGetProperty", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object), typeof(string)]);
        var il = getProperty.GetILGenerator(); il.Emit(OpCodes.Ldstr, "supplied value"); il.Emit(OpCodes.Ret);
        var helper = typeof(RuntimeEmitter).GetMethod("EmitGetOwnDescriptorFunctionReceiver", Members)!;
        var inputs = Activator.CreateInstance(helper.GetParameters()[1].ParameterType,
            getProperty, runtime.ObjectState, promise, runtime.FunctionValues.Type, runtime.Sentinels.UndefinedType)!;
        EmitReceiverProbe(emitter, probe, helper, inputs); probe.CreateType();
        var loaded = SaveVerifyLoad(builder); var savedProbe = loaded.GetType(probe.FullName!)!;
        foreach (var receiverType in new[] { resolve, reject })
        {
            var receiver = Activator.CreateInstance(loaded.GetType(receiverType.FullName!)!);
            foreach (string key in new[] { "name", "length" })
            {
                var value = Call(savedProbe, "Lookup", receiver, key);
                if (supplied)
                {
                    var descriptor = Assert.IsType<Dictionary<string, object?>>(value);
                    Assert.Equal("supplied value", descriptor["value"]); Assert.Equal(false, descriptor["writable"]);
                    Assert.Equal(false, descriptor["enumerable"]); Assert.Equal(true, descriptor["configurable"]);
                }
                else Assert.Equal("unhandled", value);
            }
        }
        Assert.Equal("unhandled", Call(savedProbe, "Lookup", new object(), "name"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void JsonReceiverFollowsSuppliedSingletonAndImplementation(bool selected, bool supplied)
    {
        var builder = NewAssembly(); var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(selected));
        var probe = runtime.RuntimeClass.Type.DefineNestedType("JsonDescriptorProbe", TypeAttributes.NestedPublic);
        var field = probe.DefineField("SuppliedJson", typeof(Dictionary<string, object>), FieldAttributes.Public | FieldAttributes.Static);
        var json = new EmittedJsonRuntime { SingletonField = field };
        if (supplied) json.BeginImplementationEmission();
        var helper = typeof(RuntimeEmitter).GetMethod("EmitGetOwnDescriptorJsonReceiver", Members)!;
        var inputs = Activator.CreateInstance(helper.GetParameters()[1].ParameterType, json, runtime.ObjectState)!;
        EmitReceiverProbe(emitter, probe, helper, inputs); probe.CreateType();
        var loaded = SaveVerifyLoad(builder); var savedProbe = loaded.GetType(probe.FullName!)!;
        var singleton = new Dictionary<string, object>();
        foreach (string key in new[] { "parse", "stringify", "isRawJSON", "rawJSON" }) singleton[key] = new object();
        savedProbe.GetField(field.Name)!.SetValue(null, singleton);
        foreach (var pair in singleton)
        {
            var value = Call(savedProbe, "Lookup", singleton, pair.Key);
            if (supplied)
            {
                var descriptor = Assert.IsType<Dictionary<string, object?>>(value);
                Assert.Same(pair.Value, descriptor["value"]); Assert.Equal(true, descriptor["writable"]);
                Assert.Equal(false, descriptor["enumerable"]); Assert.Equal(true, descriptor["configurable"]);
            }
            else Assert.Equal("unhandled", value);
        }
        Assert.Equal("unhandled", Call(savedProbe, "Lookup", new Dictionary<string, object>(), "parse"));
        Assert.Equal("unhandled", Call(savedProbe, "Lookup", singleton, "missing"));
    }

    [Fact]
    public void ScopedHelpersDoNotRetainTheWholeRuntimeHolderOrFlatAliases()
    {
        foreach (var name in new[]
        {
            "EmitStoredPropertyDescriptorResult",
            "EmitDescriptorBoolField",
            "EmitDefinePropertyReceiverValidation",
            "EmitDefinePropertySymbolReceiver",
            "EmitDefinePropertyProxyReceiver",
            "EmitObjectDefineProperty",
            "EmitDefinePropertyArrayLengthCoercion",
            "EmitDefinePropertyDescriptorTypeValidation",
            "EmitDefinePropertyDescriptorNormalization",
            "EmitDefinePropertyDescriptorFields",
            "EmitDefinePropertyRedefinitionValidation",
            "DeclareObjectGetOwnPropertyDescriptor",
            "EmitObjectGetOwnPropertyDescriptor",
            "EmitSymbolKeyDescriptorLookup",
            "EmitObjectDefineProperties",
            "EmitObjectGetOwnPropertyDescriptors",
            "EmitDefinePropertyExtensibilityValidation",
            "EmitDefinePropertyExistingDescriptor",
            "EmitDefinePropertyExistingArrayLength",
            "EmitDefinePropertyExistingFunctionPrototype",
            "EmitDefinePropertyExistingRegExpLastIndex",
            "EmitDefinePropertyDescriptorClassification",
            "EmitDefinePropertyDescriptorMerge",
            "EmitDefinePropertyArrayIndexLengthGrowth",
            "EmitDefinePropertyAccessorStorage",
            "EmitDefinePropertyDataStorage",
            "EmitDefinePropertyDictionaryStorage",
            "EmitDefinePropertyArrayLengthStorage",
            "EmitDefinePropertyArrayIndexStorage",
            "EmitDefinePropertyListIndexStorage",
            "EmitDefinePropertyCompactStorage",
            "EmitDefinePropertyObjectStorage",
            "EmitBuiltinDataDescriptor",
            "EmitGetOwnDescriptorProxyReceiver",
            "EmitGetOwnDescriptorGlobalReceiver",
            "EmitGetOwnDescriptorArrayLength",
            "EmitGetOwnDescriptorRegExpReceiver",
            "EmitGetOwnDescriptorFunctionReceiver",
            "EmitGetOwnDescriptorConstructorReceiver",
            "EmitGetOwnDescriptorConstructorMetadata",
            "EmitGetOwnDescriptorRegExpConstructor",
            "EmitGetOwnDescriptorObjectConstructor",
            "EmitGetOwnDescriptorDateConstructor",
            "EmitGetOwnDescriptorArrayConstructor",
            "EmitGetOwnDescriptorNumberConstructor",
            "EmitGetOwnDescriptorConstructorFallback",
            "EmitGetOwnDescriptorIndexedReceiver",
            "EmitGetOwnDescriptorMathReceiver",
            "EmitGetOwnDescriptorJsonReceiver",
            "EmitGetOwnDescriptorDictionaryReceiver",
            "EmitGetOwnDescriptorLiteralAccessor",
            "EmitGetOwnDescriptorFieldsReceiver"
        })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNestedPrivate))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
        foreach (var name in new[] { "ObjectDefineProperty", "ObjectGetOwnPropertyDescriptor", "ObjectDefineProperties", "ObjectGetOwnPropertyDescriptors" })
            Assert.Null(typeof(EmittedRuntime).GetProperty(name));
    }

    private static void EmitReceiverProbe(RuntimeEmitter emitter, TypeBuilder probe, MethodInfo helper, object inputs)
    {
        var method = probe.DefineMethod("Lookup", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object), typeof(string)]);
        var il = method.GetILGenerator(); var key = il.DeclareLocal(typeof(string));
        var result = il.DeclareLocal(typeof(Dictionary<string, object>)); var missing = il.DefineLabel(); var end = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, key);
        helper.Invoke(emitter, [il, inputs, key, result, missing, end]);
        il.Emit(OpCodes.Ldstr, "unhandled"); il.Emit(OpCodes.Br, end);
        il.MarkLabel(missing); il.Emit(OpCodes.Ldnull);
        il.MarkLabel(end); il.Emit(OpCodes.Ret);
    }
    private static TypeBuilder SimpleReceiver(TypeBuilder parent, string name)
    {
        var type = parent.DefineNestedType(name, TypeAttributes.NestedPublic);
        type.DefineDefaultConstructor(MethodAttributes.Public); type.CreateType(); return type;
    }
    private static void Fill(EmittedObjectDescriptorRuntime owner, string? omitted = null)
    {
        foreach (var property in Handles.Where(property => property.Name != omitted)) property.SetValue(owner, Handle());
    }
    private static MethodBuilder Handle() => NewAssembly().DefineDynamicModule("main").DefineType("Declaration")
        .DefineMethod("Invoke", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
    private static void AssertFrozen(EmittedObjectDescriptorRuntime owner)
    {
        Assert.True(owner.IsComplete); Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        foreach (var property in Handles)
        {
            Assert.False(property.SetMethod!.IsPublic);
            Expect<InvalidOperationException>(() => property.SetValue(owner, property.GetValue(owner)));
        }
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"object_descriptor_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(bool optional) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(optional
        ? "Promise.resolve(1); JSON.stringify({a:1}); new Date(0); /x/.test('x');" : "const value = 1;").ScanTokens()).ParseOrThrow());
    private static object? Call(Type type, string name, params object?[] values) => type.GetMethod(name, StaticMembers)!.Invoke(null, values);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
