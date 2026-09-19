using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedEventSubscriptionRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo[] Handles = typeof(EmittedEventSubscriptionRuntime).GetProperties()
        .Where(p => p.PropertyType != typeof(bool)).ToArray();
    public static IEnumerable<object[]> HandleNames => Handles.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void DeclarationsRejectMissingNullAndDuplicateValues(string name)
    {
        var owner = new EmittedRuntime().EventSubscriptions;
        var property = Handles.Single(p => p.Name == name);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.GetValue(owner)).InnerException);
        Assert.IsType<ArgumentNullException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null)).InnerException);
        var value = Handle(property.PropertyType); property.SetValue(owner, value);
        Assert.Same(value, property.GetValue(owner));
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, value)).InnerException);
        Assert.False(owner.IsComplete);
    }

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void FailedCompletionCanBeRepairedAndCompletedMetadataIsFrozen(string missing)
    {
        var owner = new EmittedRuntime().EventSubscriptions;
        foreach (var property in Handles.Where(p => p.Name != missing)) property.SetValue(owner, Handle(property.PropertyType));
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        Assert.False(owner.IsComplete);
        var omitted = Handles.Single(p => p.Name == missing); omitted.SetValue(owner, Handle(omitted.PropertyType)); Complete(owner);
        Assert.True(owner.IsComplete);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        foreach (var property in Handles)
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, Handle(property.PropertyType))).InnerException);
    }

    [Fact]
    public void HelpersAcceptOnlyTheirDestinationAndRequiredMetadata()
    {
        Assert.Equal(3, Handles.Length);
        Assert.NotSame(new EmittedRuntime().EventSubscriptions, new EmittedRuntime().EventSubscriptions);
        Assert.Null(typeof(EmittedRuntime).GetProperty("EventSubscriptions")!.SetMethod);
        foreach (string name in new[] { "EventSubscriptionsField", "AddEventSubscription", "RemoveEventSubscription" })
            Assert.Null(typeof(EmittedRuntime).GetProperty(name));
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedEventSubscriptionRuntime), typeof(ILGenerator) },
            typeof(RuntimeEmitter).GetMethod("EmitEventSubscriptionHelpers", Members)!.GetParameters().Select(p => p.ParameterType));
        foreach (string name in new[] { "EmitAddEventSubscription", "EmitRemoveEventSubscription" })
            Assert.Equal(new[] { typeof(TypeBuilder), typeof(FieldBuilder), typeof(Type) },
                typeof(RuntimeEmitter).GetMethod(name, Members)!.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void ScopedHelperEmitsRegistryWithoutOtherRuntimeOwners()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var type = module.DefineType("$Runtime", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var initializer = type.DefineTypeInitializer().GetILGenerator(); var owner = new EmittedRuntime().EventSubscriptions;
        typeof(RuntimeEmitter).GetMethod("EmitEventSubscriptionHelpers", Members)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [type, owner, initializer]);
        Assert.False(owner.IsComplete);
        foreach (var property in Handles) Assert.Same(type, ((MemberInfo)property.GetValue(owner)!).DeclaringType);
        initializer.Emit(OpCodes.Ret); type.CreateType(); Complete(owner);
        VerifyRegistry(SaveVerifyLoad(builder).GetType("$Runtime")!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterOwnsFreshRegistriesAndPreservesDeployment(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedEventSubscriptionRuntime>(); var handles = new HashSet<object>(); var registries = new HashSet<object>();
        foreach (string source in new[] { "const n=1;", "new Map();Buffer.from('x');", "const n=2;" })
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements)); var owner = runtime.EventSubscriptions;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            foreach (var property in Handles)
            {
                var handle = (MemberInfo)property.GetValue(owner)!;
                Assert.True(handles.Add(handle)); Assert.Same(builder, handle.Module.Assembly); Assert.Same(runtime.RuntimeClass.Type, handle.DeclaringType);
            }
            var loaded = SaveVerifyLoad(builder); Assert.True(registries.Add(VerifyRegistry(loaded.GetType("$Runtime")!)));
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    private static object VerifyRegistry(Type type)
    {
        var field=type.GetField("_eventSubscriptions",BindingFlags.NonPublic|BindingFlags.Static)!;
        Check(field.IsPrivate&&field.IsStatic&&field.IsInitOnly&&field.FieldType==typeof(List<object[]>),"Registry field ABI");
        var add=type.GetMethod("AddEventSubscription")!;var remove=type.GetMethod("RemoveEventSubscription")!;
        Check(add.IsPublic&&add.IsStatic&&add.ReturnType==typeof(bool)&&add.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(object),typeof(string),typeof(object),typeof(Delegate)}),"Add ABI");
        Check(remove.IsPublic&&remove.IsStatic&&remove.ReturnType==typeof(Delegate)&&remove.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(object),typeof(string),typeof(object)}),"Remove ABI");
        Check(add.MetadataToken<remove.MetadataToken,"Helper declaration order");
        var entries=(List<object[]>)field.GetValue(null)!;Check(entries.Count==0,"Fresh initialized registry");
        var a=new object();var b=new object();var fn=new object();var otherFn=new object();Action first=()=>{};Action replacement=()=>{};
        bool Add(object target,string name,object handler,Delegate callback)=>(bool)add.Invoke(null,new object[]{target,name,handler,callback})!;
        object? Remove(object target,string name,object handler)=>remove.Invoke(null,new object[]{target,name,handler});
        Check(Add(a,"one",fn,first),"First insertion");
        Check(!Add(a,new string(new[]{'o','n','e'}),fn,replacement)&&entries.Count==1,"Duplicate triple/string value equality");
        Check(Add(b,"one",fn,replacement)&&Add(a,"two",fn,replacement)&&Add(a,"one",otherFn,replacement)&&entries.Count==4,"Owner/name/function discrimination");
        Check(Remove(a,"absent",fn) is null&&Remove(new object(),"one",fn) is null&&Remove(a,"one",new object()) is null&&entries.Count==4,"Missing removal preserves entries");
        Check(ReferenceEquals(Remove(a,"one",fn),first)&&entries.Count==3&&Remove(a,"one",fn) is null,"Remove returns original delegate exactly once");
        Check(Add(a,"one",fn,replacement)&&ReferenceEquals(Remove(a,"one",fn),replacement),"Re-add stores new delegate");
        Check(ReferenceEquals(Remove(b,"one",fn),replacement)&&ReferenceEquals(Remove(a,"two",fn),replacement)&&ReferenceEquals(Remove(a,"one",otherFn),replacement)&&entries.Count==0,"Other triples retained and removed independently");
        return entries;
    }

    private static void Check(bool condition, string message) => Assert.True(condition, message);
    private static object Handle(Type type)
    {
        var builder = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        return type == typeof(FieldBuilder)
            ? builder.DefineField("Entries", typeof(List<object[]>), FieldAttributes.Static)
            : builder.DefineMethod("Value", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
    }
    private static void Complete(EmittedEventSubscriptionRuntime owner) => typeof(EmittedEventSubscriptionRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"event_subscriptions_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
