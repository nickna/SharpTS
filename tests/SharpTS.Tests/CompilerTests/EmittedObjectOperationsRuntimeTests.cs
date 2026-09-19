using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectOperationsRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] OwnerTypes = [typeof(EmittedObjectOperationsRuntime)];
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
        Assert.NotSame(first.ObjectOperations, second.ObjectOperations);
        Assert.False(first.ObjectOperations.IsComplete); Assert.False(second.ObjectOperations.IsComplete);
        Assert.Equal(6, Handles(typeof(EmittedObjectOperationsRuntime)).Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectOperations))!.SetMethod);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionPreservesObjectOperationsAndPerOutputStorage(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        var shared = new Dictionary<string, object?> { ["persist"] = 1d };
        foreach (bool optional in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(optional));
            Assert.True(owners.Add(runtime.ObjectOperations)); AssertFrozen(runtime.ObjectOperations);
            foreach (var property in Handles(typeof(EmittedObjectOperationsRuntime)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.ObjectOperations)).Module.Assembly);
            var callbackProbe=runtime.RuntimeClass.Type.DefineNestedType("NativeGroupCallback",TypeAttributes.NestedPublic);
            var callback=callbackProbe.DefineMethod("Group",MethodAttributes.Public|MethodAttributes.Static,typeof(object),[typeof(object),typeof(object)]);
            var callbackIl=callback.GetILGenerator();callbackIl.Emit(OpCodes.Ldstr,"all");callbackIl.Emit(OpCodes.Ret);callbackProbe.CreateType();
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("object_operations_runtime_", StringComparison.Ordinal));
            var rt=loaded.GetType("$Runtime")!;
            object? Call(string methodName,params object?[] args)=>rt.GetMethod(methodName,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
            var methodNames=new[]{"GetValues","GetEntries","ObjectFromEntries","ObjectIs","ObjectAssign","ObjectGroupBy"};
            var tokens=methodNames.Select(n=>rt.GetMethod(n)!.MetadataToken).ToArray();Assert.True(tokens.SequenceEqual(tokens.Order()),"Object operation declaration order changed");
            var undefined=loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            Assert.True(((List<object?>)Call("GetValues",shared)!).SequenceEqual(new object?[]{1d}),"Guest descriptor storage leaked across outputs");
            Call("ObjectDefineProperty",shared,"persist",new Dictionary<string,object?>{{"value",1d},{"enumerable",false},{"writable",true},{"configurable",true}});
            Assert.True(((List<object?>)Call("GetValues",shared)!).Count==0,"Non-enumerable value leaked");
            var input=new Dictionary<string,object?>{{"b",2d},{"a",1d}};
            Assert.True(((List<object?>)Call("GetValues",input)!).SequenceEqual(new object?[]{2d,1d}),"Value order changed");
            var entries=(List<object?>)Call("GetEntries",input)!;
            Assert.True(entries.Count==2&&((List<object?>)entries[0]!).SequenceEqual(new object?[]{"b",2d})&&((List<object?>)entries[1]!).SequenceEqual(new object?[]{"a",1d}),"Entry shape/order changed");
            Assert.True(Equals(Call("ObjectIs",double.NaN,double.NaN),true)&&Equals(Call("ObjectIs",0d,-0d),false)&&Equals(Call("ObjectIs",input,input),true)&&Equals(Call("ObjectIs",input,new Dictionary<string,object?>()),false),"SameValue semantics changed");
            var target=new Dictionary<string,object?>{{"old",9d}};
            Assert.True(ReferenceEquals(Call("ObjectAssign",target,new List<object?>{input,null,undefined,new Dictionary<string,object?>{{"a",3d}}}),target)&&Equals(target["a"],3d)&&Equals(target["b"],2d)&&Equals(target["old"],9d),"Assignment mutation/identity changed");
            var symbol=Activator.CreateInstance(loaded.GetType("$TSSymbol")!,new object?[]{"native"})!;
            Call("ObjectDefineProperty",input,symbol,new Dictionary<string,object?>{{"value",7d},{"enumerable",true},{"configurable",true}});
            Call("ObjectAssign",target,new List<object?>{input});Assert.True(Equals(Call("GetIndex",target,symbol),7d),"Symbol assignment changed");
            var iterator=loaded.GetType("$TSSymbol")!.GetField("iterator")!.GetValue(null);
            var rebuilt=(Dictionary<string,object?>)Call("ObjectFromEntries",new List<object?>{new List<object?>{"x",1d},new List<object?>{"x",2d}},iterator,rt)!;
            Assert.True(rebuilt.Count==1&&Equals(rebuilt["x"],2d),"fromEntries duplicate key behavior changed");
            var savedCallback=loaded.GetType(callbackProbe.FullName!)!.GetMethod("Group")!;
            var function=Activator.CreateInstance(loaded.GetType("$TSFunction")!,new object?[]{null,savedCallback})!;
            var groups=(Dictionary<string,object?>)Call("ObjectGroupBy",new List<object?>{1d,2d},function)!;
            Assert.True(groups.Count==1&&((List<object?>)Call("GetValues",groups["all"])!).SequenceEqual(new object?[]{1d,2d}),"Grouping changed");
            Assert.True(Call("ObjectGetPrototypeOf",groups) is null,"Group result lost null prototype");
            input["late"]=4d;Assert.True(((List<object?>)Call("GetValues",input)!).Contains(4d),"Guest state was frozen with metadata");
            foreach(var value in new[]{null,undefined})
            {
                try{Call("ObjectFromEntries",value,iterator,rt);throw new Exception("Missing fromEntries nullish error");}
                catch(TargetInvocationException e){Assert.True(Call("WrapException",e.InnerException!)?.GetType()==loaded.GetType("$TypeError"),"fromEntries nullish error changed");}
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

    private static readonly string[] ScopedMethods = ["EmitGetValues", "EmitGetEntries", "EmitObjectFromEntries", "EmitObjectIs", "EmitObjectAssign", "EmitObjectGroupBy", "EmitProxyEnumerableOwnPropertiesCheck"];
    private static readonly string[] FormerHandles = ["GetValues", "GetEntries", "ObjectFromEntries", "ObjectIs", "ObjectAssign", "ObjectGroupBy"];
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
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"object_operations_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(bool optional) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(optional
        ? "Promise.resolve(1); JSON.stringify({a:1}); new Date(0); /x/.test('x'); new Map(); new Set(); 1n; new Proxy({}, {});" : "const value = 1;").ScanTokens()).ParseOrThrow());
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); var errors = verifier.Verify(bytes);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors)); return Assembly.Load(bytes.ToArray());
    }
}
