using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectReadRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] OwnerTypes = [typeof(EmittedObjectReadRuntime)];
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
        Invoke(owner, "MarkPropertyBodyEmitted"); Invoke(owner, "CompleteEmission"); AssertFrozen(owner);
    }

    [Fact]
    public void RequiredOwnerBelongsToOneCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.ObjectRead, second.ObjectRead);
        Assert.False(first.ObjectRead.IsComplete); Assert.False(second.ObjectRead.IsComplete);
        Assert.Equal(6, Handles(typeof(EmittedObjectReadRuntime)).Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectRead))!.SetMethod);
    }

    [Fact]
    public void ForwardDeclarationMustHaveOneBodyBeforeCompletion()
    {
        var owner = new EmittedObjectReadRuntime();
        Assert.Throws<InvalidOperationException>(owner.MarkPropertyBodyEmitted);
        Fill(owner);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.False(owner.IsComplete);
        owner.MarkPropertyBodyEmitted();
        Assert.Throws<InvalidOperationException>(owner.MarkPropertyBodyEmitted);
        owner.CompleteEmission();
        Assert.Throws<InvalidOperationException>(owner.MarkPropertyBodyEmitted);
        AssertFrozen(owner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionPreservesReadingAndPerOutputStorage(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        var shared = new Dictionary<string, object?> { ["persist"] = 1d };
        foreach (bool optional in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(optional));
            Assert.True(owners.Add(runtime.ObjectRead)); AssertFrozen(runtime.ObjectRead);
            foreach (var property in Handles(typeof(EmittedObjectReadRuntime)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.ObjectRead)).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("object_read_runtime_", StringComparison.Ordinal));
            var rt=loaded.GetType("$Runtime")!;
            object? Call(string methodName,params object?[] args)=>rt.GetMethod(methodName,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
            var names=new[]{"GetProperty","GetFieldsProperty","GetListProperty","GetIndex","GetLength","GetElement"};
            var tokens=names.Select(n=>rt.GetMethod(n)!.MetadataToken).ToArray();
            Assert.True(tokens[0]==tokens.Min(),"Property forward declaration moved");
            Assert.True(Equals(Call("GetProperty",shared,"persist"),1d),"Per-output descriptor state leaked");
            Call("ObjectDefineProperty",shared,"persist",new Dictionary<string,object?>{{"value",7d},{"enumerable",true},{"writable",true},{"configurable",true}});
            Assert.True(Equals(Call("GetProperty",shared,"persist"),7d),"Descriptor read changed");shared["persist"]=1d;
            var input=new Dictionary<string,object?>{{"x",3d}};
            Assert.True(Equals(Call("GetProperty",input,"x"),3d)&&Equals(Call("GetIndex",input,"x"),3d),"Named/computed reads changed");
            var fields=new Dictionary<string,object?>{{"x",4d}};var obj=Activator.CreateInstance(loaded.GetType("$Object")!,new object?[]{fields})!;
            Assert.True(Equals(Call("GetFieldsProperty",obj,"x"),4d)&&Equals(Call("GetProperty",obj,"x"),4d),"Field dispatch changed");
            var list=new List<object?>{5d,6d};
            Assert.True(Equals(Call("GetLength",list),2)&&Equals(Call("GetListProperty",list,"length"),2d),"List length changed");
            Assert.True(Equals(Call("GetElement",list,1),6d)&&Equals(Call("GetIndex",list,0d),5d),"List element dispatch changed");
            Assert.True(Equals(Call("GetLength","abc"),3)&&Equals(Call("GetElement","abc",1),"b")&&Equals(Call("GetProperty","abc","length"),3d),"String reads changed");
            Assert.True(Equals(Call("GetLength",new List<double>{1d,2d,3d}),3)&&Equals(Call("GetLength",new List<bool>{true}),1),"Typed list length changed");
            var symbol=Activator.CreateInstance(loaded.GetType("$TSSymbol")!,new object?[]{"native"})!;
            Call("SetIndex",input,symbol,8d);Assert.True(Equals(Call("GetIndex",input,symbol),8d),"Symbol read changed");
            var undefined=loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            Assert.True(ReferenceEquals(Call("GetProperty",input,"missing"),undefined),"Missing property changed");
            input["x"]=9d;fields["x"]=10d;list[0]=11d;
            Assert.True(Equals(Call("GetProperty",input,"x"),9d)&&Equals(Call("GetFieldsProperty",obj,"x"),10d)&&Equals(Call("GetElement",list,0),11d),"Guest state was frozen with metadata");

        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PropertyAndIndexBranchesFollowSuppliedOptionalMetadata(bool selected, bool supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer("require('fs'); new Uint8Array(2); new AbortController(); Buffer.from('x'); 1n; Promise.resolve(1);").ScanTokens()).ParseOrThrow());
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = emitter.EmitAll(module, features);
        features.UsesBuffer = features.UsesFs = features.UsesCjsRequire = features.UsesPromise = features.UsesBigInt = features.UsesAbortController = selected;
        features.TypedArrays = selected ? RuntimeFeatureSet.TypedArrayKinds.All : RuntimeFeatureSet.TypedArrayKinds.None;
        var inputs = new Dictionary<string, object?>
        {
            ["Buffer"] = supplied ? runtime.Buffer : null, ["FileSystem"] = supplied ? runtime.FileSystem : null,
            ["CommonJs"] = supplied ? runtime.Modules.CommonJs : null, ["Promise"] = supplied ? runtime.Promise : null,
            ["Abort"] = supplied ? runtime.Abort : null, ["TypedArrays"] = supplied ? runtime.TypedArrays : new EmittedTypedArrayRuntime(),
            ["ArrayBuffer"] = supplied ? runtime.ArrayBuffer : null, ["SharedArrayBuffer"] = supplied ? runtime.SharedArrayBuffer : null,
            ["DataView"] = supplied ? runtime.DataView : null,
            ["BigInt"] = supplied ? runtime.BigInt : new EmittedBigIntRuntime { PrototypeField = runtime.BigInt.PrototypeField }
        };
        var probe = runtime.RuntimeClass.Type.DefineNestedType("SuppliedRead", TypeAttributes.NestedPublic);
        var propertyOwner = ObjectReadTestSupport.EmitProperty(emitter, probe, runtime, inputs);
        AssertFrozen(propertyOwner);
        var indexOwner = new EmittedObjectReadRuntime { Property = propertyOwner.Property };
        var indexHelper = typeof(RuntimeEmitter).GetMethod("EmitGetIndex", Members)!;
        indexHelper.Invoke(emitter, [probe, indexOwner, ObjectReadTestSupport.MakeInputs(indexHelper.GetParameters()[2].ParameterType, runtime, inputs)]);
        probe.CreateType(); var loaded = SaveVerifyLoad(builder); var saved = loaded.GetType(probe.FullName!)!;
        var property = saved.GetMethod("GetProperty")!; var index = saved.GetMethod("GetIndex")!;
        var types = ReadOperands(property).Where(p => p.OpCode == OpCodes.Isinst).Select(p => p.Member).ToArray();
        foreach (var type in new[] { runtime.Buffer!.Type, runtime.FileSystem!.StatsType, runtime.Modules.CommonJs!.Type,
            runtime.Promise!.ResolveCallbackType, runtime.Promise.RejectCallbackType,
            runtime.ArrayBuffer!.Type, runtime.SharedArrayBuffer!.Type, runtime.DataView!.Type })
            Assert.True(supplied == types.Contains(loaded.GetType(type.FullName!) ?? type), type.FullName + " branch did not follow supplied metadata.");
        Assert.Equal(supplied, types.Contains(typeof(System.Numerics.BigInteger)));
        Assert.Equal(supplied, ReadOperands(property).Any(p => p.Member.Name == "AbortSignalGetAborted"));
        foreach (var method in new[] { property, index })
            Assert.Equal(supplied, ReadOperands(method).Any(p => p.OpCode == OpCodes.Call && p.Member.Name == "IsTypedArray"));
        var receiver = new Dictionary<string, object?> { ["x"] = 7d };
        Assert.Equal(7d, property.Invoke(null, [receiver, "x"])); Assert.Equal(7d, index.Invoke(null, [receiver, "x"]));
        var signal = new Dictionary<string, object?> { ["_reasonSet"] = false, ["_token"] = new CancellationToken(true) };
        var value = property.Invoke(null, [signal, "aborted"]);
        if (supplied) Assert.Equal(true, value);
        else Assert.Same(loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null), value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TypedArrayDetectionFollowsImplementationDespiteGlobalSelection(bool selected, bool supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); emitter.EmitAll(module, Detect(selected));
        var probe = module.DefineType("SuppliedTypedArray", TypeAttributes.Public);
        var receiver = SimpleReceiver(probe, "Receiver");
        var arrays = new EmittedTypedArrayRuntime();
        if (supplied) { arrays.BeginImplementationEmission(); arrays.RequireImplementation().BaseType = receiver; }
        typeof(RuntimeEmitter).GetMethod("EmitIsTypedArrayHelper", Members)!.Invoke(emitter, [probe, arrays]);
        probe.CreateType(); var loaded = SaveVerifyLoad(builder); var method = loaded.GetType(probe.FullName!)!.GetMethod("IsTypedArray")!;
        var savedReceiver = loaded.GetType(receiver.FullName!)!;
        Assert.Equal(supplied, ReadOperands(method).Any(p => p.OpCode == OpCodes.Isinst && Equals(p.Member, savedReceiver)));
        Assert.Equal(supplied, method.Invoke(null, [Activator.CreateInstance(savedReceiver)]));
        Assert.Equal(false, method.Invoke(null, [null])); Assert.Equal(false, method.Invoke(null, [new object()]));
    }

    public static IEnumerable<object[]> ListSelections => Enumerable.Range(0, 4).SelectMany(selected => Enumerable.Range(0, 4).Select(supplied => new object[] { selected, supplied }));

    [Theory]
    [MemberData(nameof(ListSelections))]
    public void ListFastPathUsesExplicitMutationSelections(int selected, int supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main"); var features = Detect(false);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = emitter.EmitAll(module, features);
        features.UsesArrayPrototypeMutation = (selected & 1) != 0; features.UsesDynamicPropertyDescriptors = (selected & 2) != 0;
        var probe = module.DefineType("SuppliedList", TypeAttributes.Public); var owner = new EmittedObjectReadRuntime();
        var helper = typeof(RuntimeEmitter).GetMethod("EmitGetListProperty", Members)!;
        var inputs = ObjectReadTestSupport.MakeInputs(helper.GetParameters()[2].ParameterType, runtime,
            new Dictionary<string, object?> { ["ArrayPrototypeMutation"] = (supplied & 1) != 0, ["DynamicPropertyDescriptors"] = (supplied & 2) != 0 });
        helper.Invoke(emitter, [probe, owner, inputs]); probe.CreateType(); var loaded = SaveVerifyLoad(builder);
        var method = loaded.GetType("SuppliedList")!.GetMethod("GetListProperty")!;
        Assert.Equal(supplied == 0, ReadOperands(method).Any(p => p.OpCode == OpCodes.Newobj && p.Member.DeclaringType!.Name == runtime.ArrayOperations.BoundMethodCtor.DeclaringType!.Name));
        var list = new List<object?> { 1d, 2d }; Assert.Equal(2d, method.Invoke(null, [list, "length"]));
        var bound = method.Invoke(null, [list, "join"]);
        Assert.Equal("1,2", loaded.GetType("$Runtime")!.GetMethod("InvokeMethodValue")!.Invoke(null, [list, bound, new object?[] { "," }]));
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

    private static readonly string[] ScopedMethods = ["EmitGetProperty", "EmitGetFieldsProperty", "EmitGetListProperty", "EmitGetIndex", "EmitRegExpSymbolDispatch", "EmitRegExpSymbolCase", "EmitGetLength", "EmitGetElement", "EmitProxyGetPropertyCheck", "EmitProxyGetIndexCheck", "EmitNamespaceGetBranch", "EmitFunctionGetBranch", "EmitMapGetBranch", "EmitSetGetBranch", "EmitObjectArrayGetBranch", "EmitSharpTSArrayGetBranch", "EmitListGetBranch", "EmitBufferGetBranch", "EmitStatsGetBranch", "EmitStringGetBranch", "EmitTSObjectGetBranch", "EmitRegExpGetBranch", "EmitDictGetBranch", "DeclareObjectReadProperty", "EmitIsTypedArrayHelper"];
    private static readonly string[] FormerHandles = ["GetProperty", "GetFieldsProperty", "GetListProperty", "GetIndex", "GetLength", "GetElement"];
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
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"object_read_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(bool optional) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(optional
        ? "Promise.resolve(1); JSON.stringify({a:1}); new Date(0); /x/.test('x'); new Map(); new Set(); 1n; new Proxy({}, {}); new Uint8Array(2); new AbortController(); Buffer.from('x');" : "const value = 1;").ScanTokens()).ParseOrThrow());
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); var errors = verifier.Verify(bytes);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors)); return Assembly.Load(bytes.ToArray());
    }
    private static TypeBuilder SimpleReceiver(TypeBuilder parent, string name)
    {
        var type = parent.DefineNestedType(name, TypeAttributes.NestedPublic); type.DefineDefaultConstructor(MethodAttributes.Public); type.CreateType(); return type;
    }
    private static IEnumerable<(OpCode OpCode, MemberInfo Member)> ReadOperands(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe ? unchecked((short)(0xfe00 | il[offset++])) : first;
            OpCode opCode = OpCodeByValue[value];
            if (opCode.OperandType is OperandType.InlineType or OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok)
                yield return (opCode, method.Module.ResolveMember(BitConverter.ToInt32(il, offset))!);
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
