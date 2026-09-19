using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectDeletionRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] OwnerTypes = [typeof(EmittedObjectDeletionRuntime)];
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
        Assert.NotSame(first.ObjectDeletion, second.ObjectDeletion);
        Assert.False(first.ObjectDeletion.IsComplete); Assert.False(second.ObjectDeletion.IsComplete);
        Assert.Equal(5, Handles(typeof(EmittedObjectDeletionRuntime)).Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectDeletion))!.SetMethod);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionPreservesDeletionAndPerOutputStorage(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        var shared = new Dictionary<string, object?> { ["persist"] = 1d };
        foreach (bool optional in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(optional));
            Assert.True(owners.Add(runtime.ObjectDeletion)); AssertFrozen(runtime.ObjectDeletion);
            foreach (var property in Handles(typeof(EmittedObjectDeletionRuntime)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.ObjectDeletion)).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("object_deletion_runtime_", StringComparison.Ordinal));
            var rt=loaded.GetType("$Runtime")!;
            object? Call(string methodName,params object?[] args)=>rt.GetMethod(methodName,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
            var names=new[]{"CompactDictionaryOrder","DeleteProperty","DeletePropertyStrict","DeleteIndex","DeleteIndexStrict"};
            var tokens=names.Select(n=>rt.GetMethod(n)!.MetadataToken).ToArray();Assert.True(tokens.SequenceEqual(tokens.Order()),"Deletion declaration order changed");
            Assert.True(Equals(Call("DeleteProperty",shared,"persist"),true)&&!shared.ContainsKey("persist"),"Per-output descriptor state leaked");
            shared["persist"]=1d;
            Call("ObjectDefineProperty",shared,"persist",new Dictionary<string,object?>{{"value",1d},{"enumerable",true},{"writable",true},{"configurable",false}});
            Assert.True(Equals(Call("DeleteProperty",shared,"persist"),false)&&shared.ContainsKey("persist"),"Non-configurable property was deleted");
            var input=new Dictionary<string,object?>{{"a",1d},{"b",2d},{"c",3d}};
            Assert.True(Equals(Call("DeleteProperty",input,"b"),true)&&!input.ContainsKey("b"),"Named deletion changed");
            input["d"]=4d;Assert.True(input.Keys.SequenceEqual(new[]{"a","c","d"}),"Deletion compaction changed insertion order");
            Assert.True(Equals(Call("DeleteIndex",input,"a"),true)&&!input.ContainsKey("a"),"Computed deletion changed");
            Assert.True(Equals(Call("DeletePropertyStrict",input,"missing",true),true),"Strict missing property deletion changed");
            Assert.True(Equals(Call("DeleteIndexStrict",input,"c",false),true)&&!input.ContainsKey("c"),"Sloppy strict-dispatch deletion changed");
            var fields=new Dictionary<string,object?>{{"x",3d}};var obj=Activator.CreateInstance(loaded.GetType("$Object")!,new object?[]{fields})!;
            Assert.True(Equals(Call("DeleteProperty",obj,"x"),true)&&!fields.ContainsKey("x"),"Object fields deletion changed");
            var symbol=Activator.CreateInstance(loaded.GetType("$TSSymbol")!,new object?[]{"native"})!;
            Call("SetIndex",input,symbol,7d);Assert.True(Equals(Call("GetIndex",input,symbol),7d),"Symbol probe setup changed");
            Assert.True(Equals(Call("DeleteIndex",input,symbol),true),"Symbol deletion changed");
            var undefined=loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            Assert.True(ReferenceEquals(Call("GetIndex",input,symbol),undefined),"Deleted Symbol remains visible");
            var locked=new Dictionary<string,object?>{{"x",1d}};Call("ObjectFreeze",locked);
            Assert.True(Equals(Call("DeleteProperty",locked,"x"),false)&&Equals(Call("DeleteIndex",locked,"x"),false),"Frozen sloppy deletion changed");
            foreach(var methodName in new[]{"DeletePropertyStrict","DeleteIndexStrict"})
            {
                bool caught=false;try{Call(methodName,locked,"x",true);}catch(TargetInvocationException error){caught=error.InnerException?.ToString().Contains("TypeError")==true;}
                Assert.True(caught,"Strict frozen deletion did not throw TypeError");
            }
            input["late"]=9d;Assert.True(Equals(Call("DeleteIndex",input,"late"),true)&&!input.ContainsKey("late"),"Guest state was frozen with metadata");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IndexDeletionFollowsSuppliedPromiseMetadata(bool selected, bool supplied)
    {
        var builder = NewAssembly(); var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(selected));
        var probe = runtime.RuntimeType.DefineNestedType("SuppliedPromiseDeletion", TypeAttributes.NestedPublic);
        var resolve = SimpleReceiver(probe, "SuppliedResolve"); var reject = SimpleReceiver(probe, "SuppliedReject");
        var promise = supplied ? new EmittedPromiseRuntime { ResolveCallbackType = resolve, RejectCallbackType = reject } : null;
        var owner = new EmittedObjectDeletionRuntime { Property = runtime.ObjectDeletion.Property, PropertyStrict = runtime.ObjectDeletion.PropertyStrict };
        foreach (var helperName in new[] { "EmitDeleteIndex", "EmitDeleteIndexStrict" })
        {
            var helper = typeof(RuntimeEmitter).GetMethod(helperName, Members)!;
            var inputs = MakeInputs(helper.GetParameters()[2].ParameterType, runtime, new Dictionary<string, object?> { ["Promise"] = promise });
            helper.Invoke(emitter, [probe, owner, inputs]);
        }
        probe.CreateType(); var loaded = SaveVerifyLoad(builder);
        var isDeleted = loaded.GetType("$Runtime")!.GetMethod("IsBuiltinDeleted")!;
        foreach (var methodName in new[] { "DeleteIndex", "DeleteIndexStrict" })
        {
            var method = loaded.GetType(probe.FullName!)!.GetMethod(methodName)!;
            var operands = ReadTypeOperands(method).Where(p => p.OpCode == OpCodes.Isinst).Select(p => p.Type).ToArray();
            foreach (var receiver in new[] { resolve, reject })
            {
                var savedType = loaded.GetType(receiver.FullName!)!; Assert.Equal(supplied, operands.Contains(savedType));
                var value = Activator.CreateInstance(savedType)!;
                object?[] args = methodName == "DeleteIndex" ? [value, "name"] : [value, "name", true];
                Assert.Equal(true, method.Invoke(null, args));
                Assert.Equal(supplied, isDeleted.Invoke(null, [value, "name"]));
            }
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

    private static readonly string[] ScopedMethods = ["EmitDeleteProperty", "EmitCompactDictionaryOrder", "EmitDeletePropertyCore", "EmitDeletePropertyStrict", "EmitDeleteIndex", "EmitDeleteIndexCore", "EmitDeleteIndexStrict", "EmitProxyDeleteCheck", "EmitThrowTypeError", "EmitThrowTypeErrorWithName"];
    private static readonly string[] FormerHandles = ["DeleteProperty", "DeletePropertyStrict", "CompactDictionaryOrder", "DeleteIndex", "DeleteIndexStrict"];
    private static object MakeInputs(Type type, EmittedRuntime runtime, IReadOnlyDictionary<string, object?> overrides)
    {
        var constructor = Assert.Single(type.GetConstructors());
        return constructor.Invoke(constructor.GetParameters().Select(parameter => overrides.TryGetValue(parameter.Name!, out var value)
            ? value : (parameter.Name switch { "TSFunctionType" => runtime.FunctionValues.Type, _ => (parameter.Name switch { "InvokeMethodUnwrapped" => runtime.ReflectedMethods.InvokeUnwrapped, _ => (parameter.Name switch { "InvokeValue" => runtime.Invocation.Value, "InvokeMethodValue" => runtime.Invocation.Method, "InvokeMethodValue0" => runtime.Invocation.Method0, _ => (parameter.Name! switch { "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(parameter.Name!)!.GetValue(runtime) }) }) }) })).ToArray());
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
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"object_deletion_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(bool optional) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(optional
        ? "Promise.resolve(1); JSON.stringify({a:1}); new Date(0); /x/.test('x'); new Map(); new Set(); 1n; new Proxy({}, {});" : "const value = 1;").ScanTokens()).ParseOrThrow());
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
