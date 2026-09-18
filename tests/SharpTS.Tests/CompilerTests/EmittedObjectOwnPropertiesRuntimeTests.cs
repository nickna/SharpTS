using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectOwnPropertiesRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] OwnerTypes = [typeof(EmittedObjectOwnPropertiesRuntime)];
    private static PropertyInfo[] Handles(Type type) => type.GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => OwnerTypes.SelectMany(type => Handles(type).Select(p => new object[] { type, p.Name }));

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationIsRepairableAndCompletionFreezesMetadata(Type type, string missing)
    {
        var owner = Activator.CreateInstance(type, nonPublic: true)!; var property = type.GetProperty(missing)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Fill(owner, missing);
        Expect<InvalidOperationException>(() => Invoke(owner, "CompleteEmission")); Assert.False(IsComplete(owner));
        var handle = Handle(property); property.SetValue(owner, handle); Assert.Same(handle, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, Handle(property)));
        Invoke(owner, "CompleteEmission"); AssertFrozen(owner);
    }

    [Fact]
    public void RequiredOwnerBelongsToOneCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.ObjectOwnProperties, second.ObjectOwnProperties);
        Assert.False(first.ObjectOwnProperties.IsComplete); Assert.False(second.ObjectOwnProperties.IsComplete);
        Assert.Equal(7, Handles(typeof(EmittedObjectOwnPropertiesRuntime)).Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectOwnProperties))!.SetMethod);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionPreservesOwnPredicatesAccessorsAndPerOutputStorage(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        var shared = new Dictionary<string, object?> { ["persist"] = 1d };
        foreach (bool optional in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(optional));
            Assert.True(owners.Add(runtime.ObjectOwnProperties)); AssertFrozen(runtime.ObjectOwnProperties);
            foreach (var property in Handles(typeof(EmittedObjectOwnPropertiesRuntime)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.ObjectOwnProperties)).Module.Assembly);
            var accessorProbe=runtime.RuntimeType.DefineNestedType("NativeAccessorProbe",TypeAttributes.NestedPublic);
            var getter=accessorProbe.DefineMethod("GetValue",MethodAttributes.Public|MethodAttributes.Static,typeof(object),Type.EmptyTypes);
            var getterIl=getter.GetILGenerator();getterIl.Emit(OpCodes.Ldc_R8,7d);getterIl.Emit(OpCodes.Box,typeof(double));getterIl.Emit(OpCodes.Ret);
            var setter=accessorProbe.DefineMethod("SetValue",MethodAttributes.Public|MethodAttributes.Static,typeof(void),[typeof(object)]);
            setter.GetILGenerator().Emit(OpCodes.Ret);accessorProbe.CreateType();
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("own_properties_runtime_", StringComparison.Ordinal));
            var rt=loaded.GetType("$Runtime")!;
            object? Call(string name,params object?[] values)=>rt.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,values);
            var methodNames=new[]{"HasOwnPropertyHelper","PropertyIsEnumerableHelper","ObjectHasOwn","LookupGetterHelper","LookupSetterHelper","DefineGetterHelper","DefineSetterHelper"};
            var tokens=methodNames.Select(n=>rt.GetMethod(n)!.MetadataToken).ToArray();Assert.True(tokens.SequenceEqual(tokens.Order()),"Own-property helper declaration order changed");
            foreach(var methodName in methodNames.Where(n=>n!="ObjectHasOwn"))Assert.True(rt.GetMethod(methodName)!.GetParameters()[0].Name=="__this","Receiver parameter contract changed: "+methodName);
            var undefined=loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            Assert.True(Equals(Call("HasOwnPropertyHelper",shared,"persist"),true)&&Equals(Call("PropertyIsEnumerableHelper",shared,"persist"),true),"Guest descriptor storage leaked across outputs");
            Call("ObjectDefineProperty",shared,"persist",new Dictionary<string,object?>{{"value",1d},{"enumerable",false},{"writable",true},{"configurable",true}});
            Assert.True(Equals(Call("HasOwnPropertyHelper",shared,"persist"),true)&&Equals(Call("PropertyIsEnumerableHelper",shared,"persist"),false),"Own/enumerable descriptor distinction changed");
            var target=new Dictionary<string,object?>{{"data",1d}};
            Assert.True(Equals(Call("ObjectHasOwn",target,"data"),true)&&Equals(Call("HasOwnPropertyHelper",target,"missing"),false),"Own-property predicate changed");
            var symbol=Activator.CreateInstance(loaded.GetType("$TSSymbol")!,new object?[]{"native"})!;
            Call("ObjectDefineProperty",target,symbol,new Dictionary<string,object?>{{"value",9d},{"enumerable",true},{"configurable",true}});
            Assert.True(Equals(Call("HasOwnPropertyHelper",target,symbol),true)&&Equals(Call("PropertyIsEnumerableHelper",target,symbol),true),"Own Symbol descriptor behavior changed");
            var savedProbe=loaded.GetType(accessorProbe.FullName!)!;
            object Function(string method)=>Activator.CreateInstance(loaded.GetType("$TSFunction")!,new object?[]{null,savedProbe.GetMethod(method)})!;
            Assert.True(ReferenceEquals(Call("DefineGetterHelper",target,"value",Function("GetValue")),undefined),"Define getter return changed");
            Assert.True(ReferenceEquals(Call("DefineSetterHelper",target,"value",Function("SetValue")),undefined),"Define setter return changed");
            var descriptor=(Dictionary<string,object?>)Call("ObjectGetOwnPropertyDescriptor",target,"value")!;
            var savedGetter=Call("LookupGetterHelper",target,"value");var savedSetter=Call("LookupSetterHelper",target,"value");
            Assert.True(ReferenceEquals(savedGetter,descriptor["get"])&&ReferenceEquals(savedSetter,descriptor["set"]),"Legacy accessor descriptor identity changed");
            Assert.True(Equals(Call("GetProperty",target,"value"),7d)&&Equals(Call("PropertyIsEnumerableHelper",target,"value"),true),"Legacy accessor behavior changed");
            var derived=Call("ObjectCreate",target,undefined)!;
            Assert.True(Equals(Call("ObjectHasOwn",derived,"data"),false)&&ReferenceEquals(Call("LookupGetterHelper",derived,"value"),savedGetter),"Own/inherited property lookup changed");
            Call("ObjectDefineProperty",derived,"value",new Dictionary<string,object?>{{"value",3d},{"configurable",true}});
            Assert.True(ReferenceEquals(Call("LookupGetterHelper",derived,"value"),undefined)&&ReferenceEquals(Call("LookupSetterHelper",derived,"value"),undefined),"Data property did not stop accessor lookup");
            target["late"]=5d;Assert.True(Equals(Call("ObjectHasOwn",target,"late"),true),"Guest state was frozen with metadata");
            foreach(var value in new[]{null,undefined})
            {
                try{Call("ObjectHasOwn",value,"x");throw new Exception("Missing Object.hasOwn nullish error");}
                catch(TargetInvocationException e){Assert.True(Call("WrapException",e.InnerException!)?.GetType()==loaded.GetType("$TypeError"),"Object.hasOwn nullish error changed");}
            }
            foreach(var methodName in new[]{"DefineGetterHelper","DefineSetterHelper"})
            {
                try{Call(methodName,target,"bad",7d);throw new Exception("Missing callable accessor error");}
                catch(TargetInvocationException e){Assert.True(Call("WrapException",e.InnerException!)?.GetType()==loaded.GetType("$TypeError"),"Accessor callable error changed");}
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PromiseCallbackOwnPropertiesFollowSuppliedMetadata(bool selected, bool supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = emitter.EmitAll(module, Detect(selected));
        var probe = runtime.RuntimeType.DefineNestedType("HasOwnPromiseProbe", TypeAttributes.NestedPublic);
        var resolve = SimpleReceiver(probe, "SuppliedResolve"); var reject = SimpleReceiver(probe, "SuppliedReject");
        var promise = supplied ? new EmittedPromiseRuntime { ResolveCallbackType = resolve, RejectCallbackType = reject } : null;
        var owner = new EmittedObjectOwnPropertiesRuntime();
        var helper = typeof(RuntimeEmitter).GetMethod("EmitHasOwnPropertyHelper", Members)!;
        var inputs = MakeInputs(helper.GetParameters()[2].ParameterType, runtime, new Dictionary<string, object?> { ["Promise"] = promise });
        helper.Invoke(emitter, [probe, owner, inputs]); probe.CreateType();
        var loaded = SaveVerifyLoad(builder); var savedProbe = loaded.GetType(probe.FullName!)!;
        var hasOwn = savedProbe.GetMethod("HasOwnPropertyHelper")!;
        var operands = ReadTypeOperands(hasOwn).Where(p => p.OpCode == OpCodes.Isinst).Select(p => p.Type).ToArray();
        foreach (var receiver in new[] { resolve, reject })
        {
            var savedType = loaded.GetType(receiver.FullName!)!; var instance = Activator.CreateInstance(savedType);
            Assert.Equal(supplied, operands.Contains(savedType));
            Assert.Equal(supplied, hasOwn.Invoke(null, [instance, "name"]));
            Assert.Equal(supplied, hasOwn.Invoke(null, [instance, "length"]));
            Assert.Equal(false, hasOwn.Invoke(null, [instance, "prototype"]));
            Assert.Equal(false, hasOwn.Invoke(null, [instance, "missing"]));
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

    private static readonly string[] ScopedMethods = ["EmitHasOwnPropertyHelper", "EmitPropertyIsEnumerableHelper", "EmitLookupAccessorHelpers", "EmitDefineAccessorHelper", "EmitLookupAccessorHelper", "EmitObjectHasOwn"];
    private static readonly string[] FormerHandles = ["HasOwnPropertyHelperMethod", "PropertyIsEnumerableHelperMethod", "LookupGetterHelperMethod", "LookupSetterHelperMethod", "DefineGetterHelperMethod", "DefineSetterHelperMethod", "ObjectHasOwn"];
    private static object MakeInputs(Type type, EmittedRuntime runtime, IReadOnlyDictionary<string, object?> overrides)
    {
        var ctor = Assert.Single(type.GetConstructors());
        object? Value(ParameterInfo parameter)
        {
            string name = parameter.Name!;
            if (overrides.TryGetValue(name, out var value)) return value;
            if (name == "ProxyDescriptor") return MakeInputs(parameter.ParameterType, runtime, overrides);
            return name switch
            {
                "GetOwnPropertyDescriptor" => runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                "IsExtensible" => runtime.ObjectState.IsExtensible,
                "GetProperty" => runtime.ObjectRead.Property,
                _ => (name switch { "BoundTSFunctionType" => runtime.FunctionBindings.BoundType, "TSFunctionType" => runtime.FunctionValues.Type, _ => (name switch { "InvokeMethodUnwrapped" => runtime.ReflectedMethods.InvokeUnwrapped, _ => (name switch { "InvokeValue" => runtime.Invocation.Value, "InvokeMethodValue" => runtime.Invocation.Method, "InvokeMethodValue0" => runtime.Invocation.Method0, _ => (name switch { "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(name)!.GetValue(runtime) }) }) }) })
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
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"own_properties_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
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
