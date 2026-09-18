using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectWriteRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] OwnerTypes = [typeof(EmittedObjectWriteRuntime)];
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
        Assert.NotSame(first.ObjectWrite, second.ObjectWrite);
        Assert.False(first.ObjectWrite.IsComplete); Assert.False(second.ObjectWrite.IsComplete);
        Assert.Equal(6, Handles(typeof(EmittedObjectWriteRuntime)).Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectWrite))!.SetMethod);
    }

    [Fact]
    public void ForwardDeclarationMustHaveOneBodyBeforeCompletion()
    {
        var owner = new EmittedObjectWriteRuntime();
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
    public void RepeatedEmissionPreservesWritingAndPerOutputStorage(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        var shared = new Dictionary<string, object?> { ["persist"] = 1d };
        foreach (bool optional in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(optional));
            Assert.True(owners.Add(runtime.ObjectWrite)); AssertFrozen(runtime.ObjectWrite);
            foreach (var property in Handles(typeof(EmittedObjectWriteRuntime)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.ObjectWrite)).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("object_write_runtime_", StringComparison.Ordinal));
            var rt=loaded.GetType("$Runtime")!;
            object? Call(string methodName,params object?[] args)=>rt.GetMethod(methodName,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
            void Rejected(string methodName,params object?[] args)
            {
                try { Call(methodName,args); throw new InvalidOperationException("Strict write did not throw: "+methodName); }
                catch(TargetInvocationException error) { Assert.True(error.InnerException is not null && error.InnerException.Message.Contains("TypeError",StringComparison.Ordinal),"Wrong strict error: "+error); }
            }
            var names=new[]{"SetProperty","SetPropertyStrict","SetFieldsProperty","SetFieldsPropertyStrict","SetIndex","SetIndexStrict"};
            var tokens=names.Select(n=>rt.GetMethod(n)!.MetadataToken).ToArray();
            Assert.True(tokens[0]==tokens.Min(),"Property forward declaration moved");
            Call("SetProperty",shared,"persist",2d);
            Assert.True(Equals(shared["persist"],2d),"Per-output frozen state leaked");
            Call("ObjectFreeze",shared);Call("SetProperty",shared,"persist",3d);
            Assert.True(Equals(shared["persist"],2d),"Frozen sloppy write changed");
            Rejected("SetPropertyStrict",shared,"persist",4d,true);shared["persist"]=1d;
            var input=new Dictionary<string,object?>{{"x",3d}};
            Call("SetProperty",input,"x",4d);Call("SetIndex",input,"y",5d);
            Call("SetPropertyStrict",input,"z",6d,true);Call("SetIndexStrict",input,"w",7d,true);
            Assert.True(Equals(input["x"],4d)&&Equals(input["y"],5d)&&Equals(input["z"],6d)&&Equals(input["w"],7d),"Named/computed/strict writes changed");
            var fields=new Dictionary<string,object?>{{"x",4d}};var obj=Activator.CreateInstance(loaded.GetType("$Object")!,new object?[]{fields})!;
            Call("SetFieldsProperty",obj,"x",8d);Call("SetFieldsPropertyStrict",obj,"y",9d,true);
            Assert.True(Equals(Call("GetFieldsProperty",obj,"x"),8d)&&Equals(Call("GetFieldsProperty",obj,"y"),9d),"Field writes changed");
            Call("ObjectFreeze",obj);Call("SetFieldsProperty",obj,"x",10d);
            Assert.True(Equals(Call("GetFieldsProperty",obj,"x"),8d),"Frozen field write changed");
            Rejected("SetFieldsPropertyStrict",obj,"x",11d,true);
            var list=new List<object?>{5d,6d};Call("SetIndex",list,0d,10d);Call("SetIndexStrict",list,1d,11d,true);
            Assert.True(Equals(list[0],10d)&&Equals(list[1],11d),"List index writes changed");
            var symbol=Activator.CreateInstance(loaded.GetType("$TSSymbol")!,new object?[]{"native"})!;
            Call("SetIndex",input,symbol,12d);Call("SetIndexStrict",input,symbol,13d,true);
            Assert.True(Equals(Call("GetIndex",input,symbol),13d),"Symbol strict/sloppy write changed");
            Call("ObjectFreeze",input);Call("SetIndex",input,symbol,14d);
            Assert.True(Equals(Call("GetIndex",input,symbol),13d),"Frozen Symbol write changed");
            Rejected("SetIndexStrict",input,symbol,15d,true);
            Call("SetPropertyStrict",input,"x",99d,false);Call("SetIndexStrict",input,"y",99d,false);
            Assert.True(Equals(input["x"],4d)&&Equals(input["y"],5d),"Explicit sloppy mode changed");
            var fresh=new Dictionary<string,object?>();Call("SetProperty",fresh,"value",16d);
            Assert.True(Equals(fresh["value"],16d),"Guest state frozen with metadata");

        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WriteBranchesFollowSuppliedOptionalMetadata(bool selected, bool supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var features = Detect(true); features.UsesCjsRequire = true;
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = emitter.EmitAll(module, features);
        features.UsesBuffer = features.UsesCjsRequire = features.UsesPromise = features.UsesAbortController = selected;
        features.TypedArrays = selected ? RuntimeFeatureSet.TypedArrayKinds.All : RuntimeFeatureSet.TypedArrayKinds.None;
        var inputs = new Dictionary<string, object?>
        {
            ["Buffer"] = supplied ? runtime.Buffer : null, ["CommonJs"] = supplied ? runtime.Modules.CommonJs : null,
            ["Promise"] = supplied ? runtime.Promise : null, ["Abort"] = supplied ? runtime.Abort : null,
            ["TypedArrays"] = supplied ? runtime.TypedArrays : new EmittedTypedArrayRuntime(), ["ProxySelected"] = false,
            ["ReflectAssignment"] = null
        };
        var probe = runtime.RuntimeType.DefineNestedType("SuppliedWrite", TypeAttributes.NestedPublic);
        var owner = EmitWrites(emitter, probe, runtime, inputs); AssertFrozen(owner); probe.CreateType();
        var loaded = SaveVerifyLoad(builder); var saved = loaded.GetType(probe.FullName!)!;
        foreach (var name in new[] { "SetFieldsProperty", "SetFieldsPropertyStrict" })
            Assert.Equal(supplied, ReadOperands(saved.GetMethod(name)!).Any(p => p.OpCode == OpCodes.Isinst && p.Member.Name == runtime.Promise!.Type.Name));
        foreach (var name in new[] { "SetProperty", "SetPropertyStrict" })
            Assert.Equal(supplied, ReadOperands(saved.GetMethod(name)!).Any(p => p.OpCode == OpCodes.Isinst && p.Member.Name == runtime.Modules.CommonJs!.Type.Name));
        var property = saved.GetMethod("SetProperty")!; var index = saved.GetMethod("SetIndex")!;
        Assert.Equal(supplied, ReadOperands(property).Any(p => p.Member.Name == "AbortSignalSetOnAbort"));
        Assert.Equal(supplied, ReadOperands(index).Any(p => p.OpCode == OpCodes.Isinst && p.Member.Name == runtime.Buffer!.Type.Name));
        Assert.Equal(supplied, ReadOperands(index).Any(p => p.OpCode == OpCodes.Call && p.Member.Name == "IsTypedArray"));
        var receiver = new Dictionary<string, object?>(); property.Invoke(null, [receiver, "x", 7d]); index.Invoke(null, [receiver, "y", 8d]);
        Assert.Equal(7d, receiver["x"]); Assert.Equal(8d, receiver["y"]);
        var signal = new Dictionary<string, object?> { ["_reasonSet"] = false };
        property.Invoke(null, [signal, "onabort", "handler"]);
        Assert.Equal("handler", signal[supplied ? "_onabort" : "onabort"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ProxyWriteBranchesFollowExplicitSelection(bool globalFlag, bool selected)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main"); var features = Detect(true);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = emitter.EmitAll(module, features); features.UsesProxy = globalFlag;
        var probe = runtime.RuntimeType.DefineNestedType("SelectedProxyWrite", TypeAttributes.NestedPublic);
        var owner = EmitWrites(emitter, probe, runtime, new Dictionary<string, object?>
        {
            ["ProxySelected"] = selected, ["ReflectAssignment"] = selected ? runtime.Reflect.Assignment : null
        });
        AssertFrozen(owner); probe.CreateType(); var loaded = SaveVerifyLoad(builder); var saved = loaded.GetType(probe.FullName!)!;
        var reflectSet = loaded.GetType("$Runtime")!.GetMethod("ReflectSet")!;
        foreach (var name in new[] { "SetProperty", "SetPropertyStrict", "SetIndex" })
        {
            var method = saved.GetMethod(name)!;
            Assert.Equal(selected, ReadOperands(method).Any(p => p.OpCode == OpCodes.Ldftn && Equals(p.Member, reflectSet)));
            var receiver = new Dictionary<string, object?>();
            method.Invoke(null, name == "SetPropertyStrict" ? [receiver, "x", 9d, true] : [receiver, "x", 9d]);
            Assert.Equal(9d, receiver["x"]);
        }
    }

    private static EmittedObjectWriteRuntime EmitWrites(RuntimeEmitter emitter, TypeBuilder probe, EmittedRuntime runtime,
        IReadOnlyDictionary<string, object?> overrides)
    {
        var owner = new EmittedObjectWriteRuntime();
        typeof(RuntimeEmitter).GetMethod("DeclareObjectWriteProperty", Members)!.Invoke(emitter, [probe, owner]);
        foreach (var name in new[] { "EmitSetFieldsProperty", "EmitSetFieldsPropertyStrict", "EmitSetProperty", "EmitSetPropertyStrict", "EmitSetIndex", "EmitSetIndexStrict" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            method.Invoke(emitter, method.GetParameters().Select(parameter =>
                parameter.ParameterType == typeof(TypeBuilder) ? (object)probe
                : parameter.ParameterType == typeof(EmittedObjectWriteRuntime) ? owner
                : MakeInputs(parameter.ParameterType, runtime, overrides)).ToArray());
        }
        owner.CompleteEmission(); return owner;
    }

    private static object MakeInputs(Type type, EmittedRuntime runtime, IReadOnlyDictionary<string, object?> overrides)
    {
        var constructor = Assert.Single(type.GetConstructors());
        return constructor.Invoke(constructor.GetParameters().Select(parameter =>
        {
            if (overrides.TryGetValue(parameter.Name!, out var value)) return value;
            if (parameter.Name == "CommonJs") return runtime.Modules.CommonJs;
            if (parameter.Name == "ReflectAssignment") return runtime.Reflect.Assignment;
            return (parameter.Name switch { "ArgumentsLengthField" => runtime.Arguments.LengthField, "ArgumentsType" => runtime.Arguments.Type, "BoundAnyFunctionType" => runtime.FunctionBindings.AnyType, "BoundTSFunctionType" => runtime.FunctionBindings.BoundType, "TSFunctionInvokeWithThis" => runtime.FunctionValues.InvokeWithThis, "TSFunctionType" => runtime.FunctionValues.Type, _ => (parameter.Name switch { "InvokeMethodUnwrapped" => runtime.ReflectedMethods.InvokeUnwrapped, "SafeGetMethod" => runtime.ReflectedMethods.FindMethod, _ => (parameter.Name switch { "InvokeValue" => runtime.Invocation.Value, "InvokeMethodValue" => runtime.Invocation.Method, "InvokeMethodValue0" => runtime.Invocation.Method0, _ => (parameter.Name! switch { "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(parameter.Name!)!.GetValue(runtime) }) }) }) });
        }).ToArray());
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

    private static readonly string[] ScopedMethods = ["EmitSetFieldsProperty", "EmitSetFieldsPropertyStrict", "EmitSetProperty", "EmitToLengthBoxed", "EmitSetPropertyStrict", "EmitSetIndexStrict", "EmitSetIndex", "EmitProxySetPropertyCheck", "EmitProxySetIndexCheck", "EmitGlobalThisSetRedirect", "EmitDefineDataDescriptorFromValue", "EmitInvokePdsSetterWithValueAndReturn", "DeclareObjectWriteProperty", "EmitProxySetCompiledCall"];
    private static readonly string[] FormerHandles = ["SetProperty", "SetPropertyStrict", "SetFieldsProperty", "SetFieldsPropertyStrict", "SetIndex", "SetIndexStrict"];
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
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"object_write_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(bool optional) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(optional
        ? "Promise.resolve(1); JSON.stringify({a:1}); new Date(0); /x/.test('x'); new Map(); new Set(); 1n; new Proxy({}, {}); new Uint8Array(2); new AbortController(); Buffer.from('x');" : "const value = 1;").ScanTokens()).ParseOrThrow());
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); var errors = verifier.Verify(bytes);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors)); return Assembly.Load(bytes.ToArray());
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
