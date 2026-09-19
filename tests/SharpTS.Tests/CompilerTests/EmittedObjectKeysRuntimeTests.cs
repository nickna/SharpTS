using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectKeysRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] OwnerTypes = [typeof(EmittedObjectKeysRuntime)];
    private static readonly string[] Stages = ["MarkProxyCallbackBodiesEmitted"];
    private static PropertyInfo[] Handles(Type type) => type.GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => OwnerTypes.SelectMany(type => Handles(type).Select(p => new object[] { type, p.Name }));

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationIsRepairableAndCompletionFreezesMetadata(Type type, string missing)
    {
        var owner = Activator.CreateInstance(type, nonPublic: true)!; var property = type.GetProperty(missing)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Fill(owner, missing); MarkBodies(owner);
        Expect<InvalidOperationException>(() => Invoke(owner, "CompleteEmission")); Assert.False(IsComplete(owner));
        var handle = Handle(property); property.SetValue(owner, handle); Assert.Same(handle, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, Handle(property)));
        Invoke(owner, "CompleteEmission"); AssertFrozen(owner);
    }

    [Fact]
    public void ForwardCallbacksRequireTheirBodyStageBeforeCompletion()
    {
        var owner = new EmittedObjectKeysRuntime(); Fill(owner);
        var ordinary = owner.Ordinary; var createList = owner.CreateProxyList;
        Expect<InvalidOperationException>(() => Invoke(owner, "CompleteEmission")); Assert.False(owner.IsComplete);
        Assert.Same(ordinary, owner.Ordinary); Assert.Same(createList, owner.CreateProxyList);
        owner.MarkProxyCallbackBodiesEmitted();
        Assert.Throws<InvalidOperationException>(() => owner.MarkProxyCallbackBodiesEmitted());
        owner.CompleteEmission(); AssertFrozen(owner);
        Assert.Throws<InvalidOperationException>(() => owner.MarkProxyCallbackBodiesEmitted());
    }

    [Fact]
    public void RequiredOwnerBelongsToOneCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.ObjectKeys, second.ObjectKeys);
        Assert.False(first.ObjectKeys.IsComplete); Assert.False(second.ObjectKeys.IsComplete);
        Assert.Equal(6, Handles(typeof(EmittedObjectKeysRuntime)).Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectKeys))!.SetMethod);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionPreservesKeysAndPerOutputGuestStorage(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        var shared = new Dictionary<string, object?> { ["persist"] = 1d };
        foreach (bool optional in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(optional));
            Assert.True(owners.Add(runtime.ObjectKeys)); AssertFrozen(runtime.ObjectKeys);
            foreach (var property in Handles(typeof(EmittedObjectKeysRuntime)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.ObjectKeys)).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("own_keys_runtime_", StringComparison.Ordinal));
            var rt=loaded.GetType("$Runtime")!;
            object? Call(string method,params object?[] values)=>rt.GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,values);
            var names=new[]{"GetOrdinaryOwnPropertyKeys","CreateProxyOwnKeysList","NormalizeOwnPropertyKeys","GetOwnPropertyNames","GetOwnPropertySymbols","GetKeys"};
            var tokens=names.Select(n=>rt.GetMethod(n)!.MetadataToken).ToArray();Assert.True(tokens.SequenceEqual(tokens.Order()),"Forward own-key token order changed");
            List<object?> List(string method,object? value)=>(List<object?>)Call(method,value)!;
            Assert.True(List("GetKeys",shared).SequenceEqual(new object?[]{"persist"}),"Descriptor storage leaked across outputs");
            Call("ObjectDefineProperty",shared,"persist",new Dictionary<string,object?>{{"value",1d},{"enumerable",false},{"writable",true},{"configurable",true}});
            Assert.True(List("GetKeys",shared).Count==0,"Descriptor filtering changed");
            var target=new Dictionary<string,object?>{{"b",2d},{"2",2d},{"a",1d},{"1",1d},{"01",0d}};
            var keys=new object?[]{"1","2","b","a","01"};Assert.True(List("GetKeys",target).SequenceEqual(keys),"Enumerable key order changed");
            Call("ObjectDefineProperty",target,"hidden",new Dictionary<string,object?>{{"value",7d},{"enumerable",false},{"writable",true},{"configurable",true}});
            Assert.True(List("GetKeys",target).SequenceEqual(keys),"Hidden key became enumerable");
            Assert.True(List("GetOwnPropertyNames",target).SequenceEqual(keys.Append("hidden")),"Own-name filtering/order changed");
            var symbol=Activator.CreateInstance(loaded.GetType("$TSSymbol")!,new object?[]{"native"})!;
            Assert.True(List("GetOwnPropertySymbols",target).Count==0&&Call("TryGetSymbolDict",target) is null,"Read-only symbol enumeration allocated storage");
            ((Dictionary<object,object?>)Call("GetSymbolDict",target)!)[symbol]=9d;
            Assert.True(List("GetOwnPropertySymbols",target).SequenceEqual(new[]{symbol}),"Own-symbol identity/order changed");
            Assert.True(List("GetOrdinaryOwnPropertyKeys",target).SequenceEqual(keys.Append("hidden").Append(symbol)),"Combined ordinary keys changed");
            target["late"]=3d;Assert.True(List("GetKeys",target).SequenceEqual(keys.Append("late")),"Guest mutation was frozen");
            var proxyList=new Dictionary<string,object?>{{"length",2.9d},{"0","a"},{"1",symbol},{"2","ignored"}};
            Assert.True(List("CreateProxyOwnKeysList",proxyList).SequenceEqual(new object?[]{"a",symbol}),"Proxy array-like list normalization changed");
            proxyList["length"]=double.NaN;Assert.True(List("CreateProxyOwnKeysList",proxyList).Count==0,"NaN proxy list length changed");
            var raw=new List<object?>{"b","2","a","1","01","4294967295","0","4294967294"};
            Assert.True(List("NormalizeOwnPropertyKeys",raw).SequenceEqual(new object?[]{"0","1","2","4294967294","b","a","01","4294967295"}),"Index boundary normalization changed");
            var undefined=loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            foreach(var method in new[]{"GetKeys","GetOwnPropertyNames","GetOwnPropertySymbols"})
            foreach(var value in new[]{null,undefined})
            {
                try{Call(method,value);throw new Exception("Missing nullish error: "+method);}
                catch(TargetInvocationException e){Assert.True(Call("WrapException",e.InnerException!)?.GetType()==loaded.GetType("$TypeError"),"Nullish error changed");}
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PromiseCallbackNamesFollowSuppliedMetadata(bool selected, bool supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = emitter.EmitAll(module, Detect(selected));
        var probe = runtime.RuntimeClass.Type.DefineNestedType("OwnNamesPromiseProbe", TypeAttributes.NestedPublic);
        var resolve = SimpleReceiver(probe, "SuppliedResolve"); var reject = SimpleReceiver(probe, "SuppliedReject");
        var promise = supplied ? new EmittedPromiseRuntime { ResolveCallbackType = resolve, RejectCallbackType = reject } : null;
        var owner = new EmittedObjectKeysRuntime { Normalize = runtime.ObjectKeys.Normalize };
        var helper = typeof(RuntimeEmitter).GetMethod("EmitGetOwnPropertyNames", Members)!;
        var inputs = MakeInputs(helper.GetParameters()[2].ParameterType, runtime, new Dictionary<string, object?> { ["Promise"] = promise });
        helper.Invoke(emitter, [probe, owner, inputs]); probe.CreateType();
        var loaded = SaveVerifyLoad(builder); var getter = loaded.GetType(probe.FullName!)!.GetMethod("GetOwnPropertyNames")!;
        var operands = ReadTypeOperands(getter).Where(p => p.OpCode == OpCodes.Isinst).Select(p => p.Type).ToArray();
        foreach (var receiver in new[] { resolve, reject })
        {
            var savedType = loaded.GetType(receiver.FullName!)!;
            Assert.Equal(supplied, operands.Contains(savedType));
            var names = Assert.IsType<List<object>>(getter.Invoke(null, [Activator.CreateInstance(savedType)]));
            Assert.Equal(supplied ? new object[] { "length", "name" } : [], names);
        }
    }

    [Fact]
    public void ScopedHelpersDoNotRetainTheWholeRuntimeHolderOrFlatAliases()
    {
        foreach (var name in ScopedMethods)
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNestedPrivate))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
        foreach (var name in FormerHandles) Assert.Null(typeof(EmittedRuntime).GetProperty(name));
    }

    private static readonly string[] ScopedMethods = ["EmitGetKeys", "EmitNormalizeOwnPropertyKeys", "EmitGetOwnPropertyNames", "EmitGetOwnPropertySymbols", "DeclareProxyOwnKeysHelpers", "EmitProxyOwnKeysHelperBodies", "EmitProxyOwnKeysCheck"];
    private static readonly string[] FormerHandles = ["GetKeys", "NormalizeOwnPropertyKeys", "GetOwnPropertyNames", "GetOrdinaryOwnPropertyKeys", "CreateProxyOwnKeysList", "GetOwnPropertySymbols"];
    private static object MakeInputs(Type type, EmittedRuntime runtime, IReadOnlyDictionary<string, object?> overrides)
    {
        var ctor = Assert.Single(type.GetConstructors());
        object? Value(ParameterInfo parameter)
        {
            string name = parameter.Name!;
            if (overrides.TryGetValue(name, out var value)) return value;
            if (name is "ProxyDescriptor" or "ProxyOwnKeys") return MakeInputs(parameter.ParameterType, runtime, overrides);
            return name switch
            {
                "OwnKeys" => runtime.ObjectKeys.Ordinary,
                "CreateList" => runtime.ObjectKeys.CreateProxyList,
                "GetOwnPropertyDescriptor" => runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                "IsExtensible" => runtime.ObjectState.IsExtensible,
                "IsSymbol" => runtime.Symbols.IsSymbol,
                "GetProperty" => runtime.ObjectRead.Property,
                _ => (name switch { "TSFunctionType" => runtime.FunctionValues.Type, _ => (name switch { "InvokeMethodUnwrapped" => runtime.ReflectedMethods.InvokeUnwrapped, _ => (name switch { "InvokeValue" => runtime.Invocation.Value, "InvokeMethodValue" => runtime.Invocation.Method, "InvokeMethodValue0" => runtime.Invocation.Method0, _ => (name switch { "IHasFieldsInterface" => runtime.ObjectFields.Interface, "IHasFieldsFieldsGetter" => runtime.ObjectFields.FieldsGetter, "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(name)!.GetValue(runtime) }) }) }) })
            };
        }
        return ctor.Invoke(ctor.GetParameters().Select(Value).ToArray());
    }
    private static void Fill(object owner, string? missing = null)
    {
        foreach (var property in Handles(owner.GetType()).Where(p => p.Name != missing)) property.SetValue(owner, Handle(property));
    }
    private static object Handle(PropertyInfo property)
    {
        if (property.PropertyType == typeof(Type)) return typeof(object);
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Declaration");
        return property.PropertyType == typeof(FieldBuilder) ? type.DefineField("Value", typeof(object), FieldAttributes.Public)
            : type.DefineMethod("Invoke", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
    }
    private static void MarkBodies(object owner)
    {
        if (owner is EmittedObjectKeysRuntime) foreach (var stage in Stages) Invoke(owner, stage);
    }
    private static bool IsComplete(object owner) => (bool)owner.GetType().GetProperty("IsComplete")!.GetValue(owner)!;
    private static object? Invoke(object owner, string method) => owner.GetType().GetMethod(method, Members)!.Invoke(owner, null);
    private static void AssertFrozen(object owner)
    {
        Assert.True(IsComplete(owner)); Expect<InvalidOperationException>(() => Invoke(owner, "CompleteEmission"));
        foreach (var property in Handles(owner.GetType()))
        {
            Assert.False(property.SetMethod!.IsPublic); Expect<InvalidOperationException>(() => property.SetValue(owner, property.GetValue(owner)));
        }
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"own_keys_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(bool optional) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(optional
        ? "Promise.resolve(1); JSON.stringify({a:1}); new Date(0); /x/.test('x'); new Map(); new Set(); 1n; new Proxy({}, {});" : "const value = 1;").ScanTokens()).ParseOrThrow());
    private static TypeBuilder SimpleReceiver(TypeBuilder parent, string name)
    {
        var type = parent.DefineNestedType(name, TypeAttributes.NestedPublic); type.DefineDefaultConstructor(MethodAttributes.Public); type.CreateType(); return type;
    }
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); var errors = verifier.Verify(bytes);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors)); return Assembly.Load(bytes.ToArray());
    }
    private static IEnumerable<(OpCode OpCode, Type Type)> ReadTypeOperands(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe ? unchecked((short)(0xfe00 | il[offset++])) : first;
            OpCode opCode = OpCodeByValue[value];
            if (opCode.OperandType == OperandType.InlineType)
                yield return (opCode, method.Module.ResolveType(BitConverter.ToInt32(il, offset)));
            offset += opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineMethod or
                    OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException($"Unsupported IL operand type {opCode.OperandType}.")
            };
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opCode => opCode.Value);
}
