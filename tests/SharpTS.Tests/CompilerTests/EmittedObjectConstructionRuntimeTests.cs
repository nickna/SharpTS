using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectConstructionRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] OwnerTypes = [typeof(EmittedObjectConstructionRuntime)];
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
        Assert.NotSame(first.ObjectConstruction, second.ObjectConstruction);
        Assert.False(first.ObjectConstruction.IsComplete); Assert.False(second.ObjectConstruction.IsComplete);
        Assert.Equal(6, Handles(typeof(EmittedObjectConstructionRuntime)).Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectConstruction))!.SetMethod);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionPreservesConstructionAndPerOutputStorage(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        var shared = new Dictionary<string, object?> { ["persist"] = 1d };
        foreach (bool optional in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(optional));
            Assert.True(owners.Add(runtime.ObjectConstruction)); AssertFrozen(runtime.ObjectConstruction);
            foreach (var property in Handles(typeof(EmittedObjectConstructionRuntime)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.ObjectConstruction)).Module.Assembly);
            var callbackProbe=runtime.RuntimeType.DefineNestedType("NativeConstructionCallback",TypeAttributes.NestedPublic);
            var callback=callbackProbe.DefineMethod("Read",MethodAttributes.Public|MethodAttributes.Static,typeof(object),Type.EmptyTypes);
            var callbackIl=callback.GetILGenerator();callbackIl.Emit(OpCodes.Ldc_R8,7d);callbackIl.Emit(OpCodes.Box,typeof(double));callbackIl.Emit(OpCodes.Ret);callbackProbe.CreateType();
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("object_construction_runtime_", StringComparison.Ordinal));
            var rt=loaded.GetType("$Runtime")!;
            object? Call(string methodName,params object?[] args)=>rt.GetMethod(methodName,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
            var methodNames=new[]{"CreateObject","MergeIntoTSObject","MergeIntoObject","DefineSymbolAccessor","TSObjectMergeEnumerable","ObjectRest"};
            var tokens=methodNames.Select(n=>rt.GetMethod(n)!.MetadataToken).ToArray();Assert.True(tokens.SequenceEqual(tokens.Order()),"Object construction declaration order changed");
            var undefined=loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            var target=new Dictionary<string,object?>();Call("MergeIntoObject",target,shared);
            Assert.True(target.Count==1&&Equals(target["persist"],1d),"Per-output descriptor storage leaked");
            Call("ObjectDefineProperty",shared,"persist",new Dictionary<string,object?>{{"value",1d},{"enumerable",false},{"writable",true},{"configurable",true}});
            target.Clear();Call("MergeIntoObject",target,shared);Assert.True(target.Count==0,"Spread copied a hidden key");
            var input=new Dictionary<string,object?>{{"b",2d},{"a",1d}};
            Assert.True(ReferenceEquals(Call("CreateObject",input),input),"Literal identity changed");
            Call("MergeIntoObject",target,input);Assert.True(target.Keys.SequenceEqual(new[]{"b","a"})&&Equals(target["a"],1d),"Dictionary spread changed");
            Call("MergeIntoObject",target,null);Call("MergeIntoObject",target,undefined);Assert.True(target.Count==2,"Nullish spread changed");
            var rest=(Dictionary<string,object?>)Call("ObjectRest",input,new List<object?>{"b",null})!;
            Assert.True(!ReferenceEquals(rest,input)&&rest.Count==1&&Equals(rest["a"],1d),"Rest exclusion or identity changed");
            Assert.True(((Dictionary<string,object?>)Call("ObjectRest",3d,new List<object?>())!).Count==0,"Primitive rest fallback changed");
            var fields=new Dictionary<string,object?>{{"x",3d}};
            var obj=Activator.CreateInstance(loaded.GetType("$Object")!,new object?[]{fields})!;
            Call("MergeIntoTSObject",obj,input);Assert.True(Equals(fields["a"],1d)&&Equals(fields["b"],2d),"Object spread from dictionary changed");
            var otherFields=new Dictionary<string,object?>();var other=Activator.CreateInstance(loaded.GetType("$Object")!,new object?[]{otherFields})!;
            Call("MergeIntoTSObject",other,obj);Assert.True(otherFields.Count==3&&Equals(otherFields["x"],3d),"Object spread from fields changed");
            var objectRest=(Dictionary<string,object?>)Call("ObjectRest",obj,new List<object?>{"x","b"})!;
            Assert.True(objectRest.Count==1&&Equals(objectRest["a"],1d),"Object rest from fields changed");
            var savedCallback=loaded.GetType(callbackProbe.FullName!)!.GetMethod("Read")!;
            var function=Activator.CreateInstance(loaded.GetType("$TSFunction")!,new object?[]{null,savedCallback})!;
            var symbol=Activator.CreateInstance(loaded.GetType("$TSSymbol")!,new object?[]{"native"})!;
            Call("DefineSymbolAccessor",input,symbol,function,null);Assert.True(Equals(Call("GetIndex",input,symbol),7d),"Symbol accessor changed");
            Call("DefineSymbolAccessor",obj,"computed",function,null);Assert.True(Equals(Call("GetProperty",obj,"computed"),7d),"String accessor fallback changed");
            var projected=(Dictionary<string,object?>)Call("TSObjectMergeEnumerable",obj)!;
            Assert.True(!ReferenceEquals(projected,fields)&&projected.Count==4&&Equals(projected["computed"],7d)&&Equals(projected["x"],3d),"Enumerable getter projection changed");
            Assert.True(Call("TSObjectMergeEnumerable",3d) is null,"Enumerable projection primitive fallback changed");
            input["late"]=4d;target.Clear();Call("MergeIntoObject",target,input);Assert.True(Equals(target["late"],4d),"Guest state was frozen with metadata");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ProxySpreadBranchFollowsExplicitSelection(bool globalFlag, bool selected)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var features = Detect(true); var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, features); features.UsesProxy = globalFlag;
        var probe = runtime.RuntimeType.DefineNestedType("ExplicitProxySpread", TypeAttributes.NestedPublic);
        var owner = new EmittedObjectConstructionRuntime();
        var helper = typeof(RuntimeEmitter).GetMethod("EmitMergeIntoObject", Members)!;
        var inputs = MakeInputs(helper.GetParameters()[2].ParameterType, runtime,
            new Dictionary<string, object?> { ["ProxySelected"] = selected });
        helper.Invoke(emitter, [probe, owner, inputs]); probe.CreateType();
        var loaded = SaveVerifyLoad(builder); var merge = loaded.GetType(probe.FullName!)!.GetMethod("MergeIntoObject")!;
        var strings = ReadStrings(merge).ToArray();
        Assert.Equal(selected, strings.Contains("SharpTS.Runtime.Types.SharpTSProxy"));
        Assert.Equal(selected, strings.Contains("TrapOwnKeysCompiled"));
        var source = new Dictionary<string, object?> { ["b"] = 2d, ["a"] = 1d };
        var target = new Dictionary<string, object?>(); merge.Invoke(null, [target, source]);
        Assert.Equal(new[] { "b", "a" }, target.Keys); Assert.Equal(1d, target["a"]);
        var proxy = new SharpTS.Runtime.Types.SharpTSProxy(source, new Dictionary<string, object?>());
        target.Clear(); merge.Invoke(null, [target, proxy]);
        Assert.Equal(new[] { "b", "a" }, target.Keys); Assert.Equal(2d, target["b"]);
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

    private static readonly string[] ScopedMethods = ["EmitCreateObject", "EmitMergeIntoObject", "EmitPlainDataSpreadCopy", "EmitMergeIntoTSObject", "EmitTSObjectMergeEnumerable", "EmitDefineSymbolAccessor", "EmitObjectRest"];
    private static readonly string[] FormerHandles = ["CreateObject", "MergeIntoObject", "MergeIntoTSObject", "TSObjectMergeEnumerable", "DefineSymbolAccessor", "ObjectRest"];
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
                "GetIndex" => runtime.ObjectRead.Index,
                "SetIndex" => runtime.ObjectWrite.Index,
                _ => (name switch { "InvokeMethodUnwrapped" => runtime.ReflectedMethods.InvokeUnwrapped, _ => (name switch { "InvokeValue" => runtime.Invocation.Value, "InvokeMethodValue" => runtime.Invocation.Method, "InvokeMethodValue0" => runtime.Invocation.Method0, _ => (name switch { "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(name)!.GetValue(runtime) }) }) })
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
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"object_construction_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(bool optional) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(optional
        ? "Promise.resolve(1); JSON.stringify({a:1}); new Date(0); /x/.test('x'); new Map(); new Set(); 1n; new Proxy({}, {});" : "const value = 1;").ScanTokens()).ParseOrThrow());
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); var errors = verifier.Verify(bytes);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors)); return Assembly.Load(bytes.ToArray());
    }
    private static IEnumerable<string> ReadStrings(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe ? unchecked((short)(0xfe00 | il[offset++])) : first;
            OpCode opCode = OpCodeByValue[value];
            if (opCode.OperandType == OperandType.InlineString)
                yield return method.Module.ResolveString(BitConverter.ToInt32(il, offset));
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
