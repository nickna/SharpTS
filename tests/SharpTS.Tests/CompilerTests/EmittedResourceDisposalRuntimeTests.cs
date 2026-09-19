using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedResourceDisposalRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo Handle = typeof(EmittedResourceDisposalRuntime).GetProperty("Dispose")!;

    [Fact]
    public void DeclarationRejectsMissingNullAndDuplicateValues()
    {
        var owner = new EmittedRuntime().ResourceDisposal;
        Assert.Throws<InvalidOperationException>(() => owner.Dispose);
        Expect<ArgumentNullException>(() => Handle.SetValue(owner, null));
        var value = NewAssembly().DefineDynamicModule("main").DefineType("Handles")
            .DefineMethod("Dispose", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(object), typeof(object)]);
        Handle.SetValue(owner, value);
        Assert.Same(value, owner.Dispose);
        Expect<InvalidOperationException>(() => Handle.SetValue(owner, value));
        Assert.False(owner.IsComplete);
    }

    [Fact]
    public void MissingDeclarationCanBeSuppliedAfterFailedCompletionThenMetadataFreezes()
    {
        var owner = new EmittedRuntime().ResourceDisposal;
        Expect<InvalidOperationException>(() => Complete(owner));
        Assert.False(owner.IsComplete);
        var value = NewAssembly().DefineDynamicModule("main").DefineType("Handles")
            .DefineMethod("Dispose", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(object), typeof(object)]);
        Handle.SetValue(owner, value); Complete(owner);
        Assert.True(owner.IsComplete);
        Expect<InvalidOperationException>(() => Complete(owner));
        Expect<InvalidOperationException>(() => Handle.SetValue(owner, value));
    }

    [Fact]
    public void HelperAcceptsOnlyItsDestinationOwnerAndExactDependencies()
    {
        Assert.NotSame(new EmittedRuntime().ResourceDisposal, new EmittedRuntime().ResourceDisposal);
        Assert.Null(typeof(EmittedRuntime).GetProperty("ResourceDisposal")!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty("DisposeResource"));
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedResourceDisposalRuntime), typeof(MethodInfo), typeof(Type), typeof(MethodInfo) },
            typeof(RuntimeEmitter).GetMethod("EmitDisposeResource", Members)!.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void ScopedHelperUsesInjectedLookupAndInvocationWithoutOtherOwners()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var type = module.DefineType("Probe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var returned = type.DefineField("Returned", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        var receiver = type.DefineField("Receiver", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        var method = type.DefineField("Method", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        var arity = type.DefineField("Arity", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var lookup = type.DefineMethod("Lookup", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(object)]);
        var lookupIl = lookup.GetILGenerator(); lookupIl.Emit(OpCodes.Ldsfld, returned); lookupIl.Emit(OpCodes.Ret);
        var invoke = type.DefineMethod("Invoke", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(object), typeof(object[])]);
        var il = invoke.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stsfld, receiver);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stsfld, method);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Stsfld, arity);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        var owner = new EmittedRuntime().ResourceDisposal;
        typeof(RuntimeEmitter).GetMethod("EmitDisposeResource", Members)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [type, owner, lookup, typeof(string), invoke]);
        Assert.Same(type, owner.Dispose.DeclaringType); Assert.False(owner.IsComplete);
        type.CreateType(); Complete(owner);
        var loaded = SaveVerifyLoad(builder).GetType("Probe")!; var dispose = loaded.GetMethod("DisposeResource")!;
        var resource = new object(); var callable = new object();
        loaded.GetField("Returned")!.SetValue(null, callable);
        dispose.Invoke(null, [null, new object()]); Assert.Null(loaded.GetField("Receiver")!.GetValue(null));
        dispose.Invoke(null, [resource, new object()]);
        Assert.Same(resource, loaded.GetField("Receiver")!.GetValue(null));
        Assert.Same(callable, loaded.GetField("Method")!.GetValue(null));
        Assert.Equal(0, loaded.GetField("Arity")!.GetValue(null));
        var managed = new ManagedDisposable();
        loaded.GetField("Returned")!.SetValue(null, "selected undefined-type sentinel");
        dispose.Invoke(null, [managed, new object()]); Assert.Equal(1, managed.Count);
        loaded.GetField("Returned")!.SetValue(null, null);
        dispose.Invoke(null, [managed, new object()]); Assert.Equal(2, managed.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterOwnsFreshDeclarationsAndPreservesDisposalAndDeployment(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedResourceDisposalRuntime>(); var handles = new HashSet<MethodBuilder>();
        foreach (string source in new[] { "const n=1;", "new Map();Buffer.from('x');", "const n=2;" })
        {
            var builder = NewAssembly(); var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), new RuntimeFeatureDetector().Detect(statements));
            var owner = runtime.ResourceDisposal;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete); Assert.True(handles.Add(owner.Dispose));
            Assert.Same(builder, owner.Dispose.Module.Assembly); Assert.Same(runtime.RuntimeClass.Type, owner.Dispose.DeclaringType);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!; var dispose = type.GetMethod("DisposeResource")!;
            Assert.True(dispose.IsPublic && dispose.IsStatic); Assert.Equal(typeof(void), dispose.ReturnType);
            Assert.Equal(new[] { typeof(object), typeof(object) }, dispose.GetParameters().Select(p => p.ParameterType));
            Assert.True(type.GetMethod("GetIndex")!.MetadataToken < dispose.MetadataToken);
            Assert.True(dispose.MetadataToken < type.GetMethod("SetIndex")!.MetadataToken);
            object? ReadField(FieldInfo field) => loaded.GetType(field.DeclaringType!.FullName!)!
                .GetField(field.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            var symbol = ReadField(runtime.Symbols.Dispose)!; var undefined = ReadField(runtime.Sentinels.UndefinedInstance)!;
            var set = type.GetMethod("SetIndex")!;
            void Dispose(object? value) => dispose.Invoke(null, [value, symbol]);
            void Set(object value, object? callable) => set.Invoke(null, [value, symbol, callable]);
            Dispose(null); Dispose(new object());
            var resource = new ManagedDisposable(); Dispose(resource); Assert.Equal(1, resource.Count);
            Set(resource, null); Dispose(resource); Assert.Equal(2, resource.Count);
            Set(resource, undefined); Dispose(resource); Assert.Equal(3, resource.Count);
            var target = new ManagedCallback();
            var callback = Activator.CreateInstance(loaded.GetType("$TSFunction")!, [target, typeof(ManagedCallback).GetMethod("Invoke")!])!;
            Set(resource, callback); Dispose(resource);
            Assert.Equal(1, target.Calls); Assert.Same(resource, target.Receiver); Assert.Equal(3, resource.Count);
            Set(resource, null); Dispose(resource); Assert.Equal(4, resource.Count);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    public sealed class ManagedDisposable : IDisposable
    {
        public int Count { get; private set; }
        public void Dispose() => Count++;
    }
    public sealed class ManagedCallback
    {
        public int Calls { get; private set; }
        public object? Receiver { get; private set; }
        public object? Invoke(object __this) { Calls++; Receiver = __this; return null; }
    }
    private static void Expect<T>(Action action) where T : Exception =>
        Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static void Complete(EmittedResourceDisposalRuntime owner) =>
        typeof(EmittedResourceDisposalRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"resource_disposal_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
