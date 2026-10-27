using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeCallableCatalogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeContractPreservesReceiverPaddingRestAndIdentity(bool native)
    {
        const string source = """
            function value(this: any, first: number, second: number = 3, ...tail: number[]) {
                return this.name + ':' + first + ':' + second + ':' + tail.join(',');
            }
            const fn: any = value;
            const receiver = { name: 'R', fn };
            console.log(fn === receiver.fn);
            console.log(fn.call(receiver, 1));
            console.log(fn.apply(receiver, [2, 4, 5, 6]));
            console.log(fn.bind(receiver, 7)(undefined, 8));
            function capture<T>(saved: T) { return (next: T) => [saved, next].join(','); }
            console.log(capture<string>('left')('right'));
            const key = Symbol('member');
            const holder: any = { [key]: fn };
            console.log(holder[key] === fn, holder[key].call(receiver, 9));
            """;
        byte[] bytes = Compile(source, native);
        Assembly assembly = LoadVerified(bytes);
        Type? contract = assembly.GetType("$INativeCallable");
        if (native)
        {
            Assert.NotNull(contract);
            foreach (string name in new[] { "$TSFunction", "$BoundTSFunction", "$BoundAnyFunction", "$FunctionBindWrapper", "$FunctionCallWrapper", "$FunctionApplyWrapper", "$MethodCallable" })
                Assert.True(contract.IsAssignableFrom(assembly.GetType(name)), name);
        }
        else Assert.Null(contract);
        string output = TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20));
        Assert.Equal("true\nR:1:3:\nR:2:4:5,6\nR:7:3:8\nleft,right\ntrue R:9:3:\n", output);
    }

    [Fact]
    public void GenericCarrierUsesItsOwnTypedThunkWithoutOpenTypeDispatch()
    {
        var builder = NewAssembly();
        var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var catalog = DeclareCatalog(emitter, module);
        var carrier = module.DefineType("GenericCarrier", TypeAttributes.Public);
        carrier.DefineGenericParameters("T");
        carrier.DefineDefaultConstructor(MethodAttributes.Public);
        // A private target proves execution did not use the legacy public Invoke lookup.
        var target = carrier.DefineMethod("Step", MethodAttributes.Private, typeof(object), [typeof(object[])]);
        var il = target.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_Ref); il.Emit(OpCodes.Ret);
        catalog.Register(carrier, target);
        carrier.CreateType();
        var reflected = new EmittedReflectedMethodRuntime();
        emitter.EmitMethodCallableTypeDefinition(module, reflected, catalog);
        emitter.EmitMethodCallableFinalize(reflected, catalog);
        catalog.CompleteEmission();
        Assembly saved = LoadVerified(Save(builder));
        Type closed = saved.GetType("GenericCarrier")!.MakeGenericType(typeof(string));
        var thunk = closed.GetMethod("$INativeCallable.Invoke", BindingFlags.NonPublic | BindingFlags.Instance)!;
        byte[] body = thunk.GetMethodBody()!.GetILAsByteArray()!;
        Assert.Equal(8, body.Length);
        Assert.Equal((byte)OpCodes.Callvirt.Value, body[2]);
        var called = saved.ManifestModule.ResolveMethod(BitConverter.ToInt32(body, 3), closed.GetGenericArguments(), Type.EmptyTypes)!;
        Assert.Equal("Step", called.Name);
        Assert.Equal(closed, called.DeclaringType);
        object wrapped = Activator.CreateInstance(saved.GetType("$MethodCallable")!, [Activator.CreateInstance(closed)])!;
        Assert.Equal("direct", wrapped.GetType().GetMethod("Invoke")!.Invoke(wrapped, [new object[] { "direct" }]));
    }

    [Fact]
    public void UnknownHostCarrierKeepsTheExistingBridgeUntilAdmissionIsClosed()
    {
        var builder = NewAssembly();
        var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var catalog = DeclareCatalog(emitter, module);
        var reflected = new EmittedReflectedMethodRuntime();
        emitter.EmitMethodCallableTypeDefinition(module, reflected, catalog);
        emitter.EmitMethodCallableFinalize(reflected, catalog);
        catalog.CompleteEmission();
        Assembly saved = LoadVerified(Save(builder));
        object wrapper = Activator.CreateInstance(saved.GetType("$MethodCallable")!, [new HostCallable()])!;
        Assert.Equal("host", wrapper.GetType().GetMethod("Invoke")!.Invoke(wrapper, [new object[] { "host" }]));
    }

    [Fact]
    public void RegistrationRejectsForeignOwnersAndIncompleteBodiesBeforeFreezing()
    {
        var builder = NewAssembly();
        var module = builder.DefineDynamicModule("main");
        var catalog = DeclareCatalog(new RuntimeEmitter(TypeProvider.Runtime), module);
        var owner = module.DefineType("Carrier", TypeAttributes.Public);
        var other = module.DefineType("Other", TypeAttributes.Public);
        var wrong = other.DefineMethod("Invoke", MethodAttributes.Public, typeof(object), [typeof(object[])]);
        Assert.Throws<InvalidOperationException>(() => catalog.Register(owner, wrong));
        var generic = owner.DefineMethod("GenericInvoke", MethodAttributes.Public, typeof(object), [typeof(object[])]);
        generic.DefineGenericParameters("U");
        Assert.Throws<InvalidOperationException>(() => catalog.Register(owner, generic));
        var otherModule = NewAssembly().DefineDynamicModule("other");
        var foreign = otherModule.DefineType("Foreign", TypeAttributes.Public);
        var foreignInvoke = foreign.DefineMethod("Invoke", MethodAttributes.Public, typeof(object), [typeof(object[])]);
        Assert.Throws<InvalidOperationException>(() => catalog.Register(foreign, foreignInvoke));
        var invoke = owner.DefineMethod("Invoke", MethodAttributes.Public, typeof(object), [typeof(object[])]);
        catalog.Register(owner, invoke);
        Assert.Throws<InvalidOperationException>(() => catalog.Register(owner, invoke));
        Assert.Throws<InvalidOperationException>(catalog.CompleteEmission);
        Assert.False(catalog.IsComplete);
        var genericBody = generic.GetILGenerator(); genericBody.Emit(OpCodes.Ldnull); genericBody.Emit(OpCodes.Ret);
        var il = invoke.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        owner.CreateType();
        catalog.CompleteEmission();
        Assert.True(catalog.IsComplete);
        Assert.Throws<InvalidOperationException>(() => catalog.Register(owner, invoke));
    }

    public sealed class HostCallable { public object Invoke(object[] arguments) => arguments[0]; }

    private static EmittedNativeCallableRuntime DeclareCatalog(RuntimeEmitter emitter, ModuleBuilder module)
    {
        var runtime = new EmittedRuntime();
        typeof(RuntimeEmitter).GetMethod("DeclareNativeCallableCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(emitter, [module, runtime]);
        return runtime.NativeCallables!;
    }

    private static byte[] Compile(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"native_calls_{Guid.NewGuid():N}", false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        return compiler.SaveToBytes();
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"native_calls_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static byte[] Save(PersistedAssemblyBuilder builder) { using var stream = new MemoryStream(); builder.Save(stream); return stream.ToArray(); }
    private static Assembly LoadVerified(byte[] bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        using var stream = new MemoryStream(bytes);
        Assert.Empty(verifier.Verify(stream));
        return Assembly.Load(bytes);
    }
}
