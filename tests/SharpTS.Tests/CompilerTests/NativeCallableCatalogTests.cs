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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllRuntimeCarrierFamiliesAreExplicitlyCataloguedOnlyForNativeOutput(bool native)
    {
        var builder = NewAssembly();
        var module = builder.DefineDynamicModule("main");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime, false, native)
            .EmitAll(module, RuntimeFeatureSet.EmitEverything());
        Assembly saved = LoadVerified(Save(builder));
        var names = new[]
        {
            "$TSFunction", "$BoundTSFunction", "$BoundAnyFunction", "$FunctionBindWrapper", "$FunctionCallWrapper",
            "$FunctionApplyWrapper", "$FunctionPrototype", "$MethodCallable", "$ClassDefinition", "$BoundArrayMethod", "$BoundMapMethod",
            "$BoundSetMethod", "$BoundTypedArrayMethod", "$TextDecoderDecodeMethod", "$PromiseResolveCallback",
            "$PromiseRejectCallback", "$ReflectMetadataDecorator", "$MapTransformCallback", "$FilterTransformCallback",
            "$StreamFinishedCleanup", "$TransformDoneCallback", "$WriteCallbackWrapper", "$BoundDHMethod", "$BoundECDHMethod"
        };
        var carriers = saved.GetTypes().Where(type => !type.IsInterface && type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(method => method.Name == "Invoke" && method.ReturnType == typeof(object) &&
                method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(object[]) })))
            .OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(names.Order(StringComparer.Ordinal), carriers.Select(type => type.Name));
        Type? contract = saved.GetType("$INativeCallable");
        if (native)
        {
            Assert.NotNull(contract);
            Assert.True(runtime.NativeCallables!.IsComplete);
            Assert.Equal(names.Length, runtime.NativeCallables.CarrierCount);
            foreach (Type carrier in carriers)
            {
                var implementation = Assert.Single(carrier.GetInterfaceMap(contract).TargetMethods);
                Assert.True(implementation.IsPrivate);
                Assert.True(implementation.IsFinal);
                Assert.Equal("$INativeCallable.Invoke", implementation.Name);
            }
        }
        else
        {
            Assert.Null(contract);
            Assert.Null(runtime.NativeCallables);
            Assert.All(carriers, carrier => Assert.Null(carrier.GetMethod("$INativeCallable.Invoke", BindingFlags.Instance | BindingFlags.NonPublic)));
        }
    }

    [Fact]
    public void ExactGuestDelegateUsesDirectInvokeAndOtherDelegateSignaturesKeepTheirBridge()
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
        Type wrapperType = saved.GetType("$MethodCallable")!;
        MethodInfo invoke = wrapperType.GetMethod("Invoke")!;
        object? Call(object callable, params object[] args) => invoke.Invoke(Activator.CreateInstance(wrapperType, [callable]), [args]);
        Func<object[], object> exact = arguments => (int)arguments[0] + 1;
        Assert.Equal(42d, Call(exact, 41));
        Func<object[], string> covariant = arguments => "typed:" + arguments[0];
        Assert.Equal("typed:result", Call(covariant, "result"));
        object? received = null;
        Action<object[]> action = arguments => received = arguments[0];
        Assert.Null(Call(action, "action"));
        Assert.Equal("action", received);
        HostDelegate other = arguments => arguments[0];
        Assert.Equal("other signature", Call(other, "other signature"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceAssemblyGuestPreservesCollectionDecoderAndPromiseCallbacks(bool native)
    {
        const string source = """
            const numbers: any = [1, 2];
            const append: any = numbers.push;
            console.log(append.call(numbers, 3), numbers.join(','));
            const map: any = new Map<string, number>();
            const put: any = map.set;
            put.call(map, 'x', 4);
            console.log(map.get('x'));
            const set: any = new Set<number>();
            const add: any = set.add;
            add.call(set, 5);
            console.log(set.has(5));
            const bytes: any = new Uint8Array([65]);
            const fill: any = bytes.fill;
            fill.call(bytes, 66);
            const decoder: any = new TextDecoder();
            const decode: any = decoder.decode;
            console.log(decode.call(decoder, bytes));
            Promise.resolve(6).then(n => console.log(n));
            """;
        Assembly saved = LoadVerified(Compile(source, native));
        Assert.Equal("3 1,2,3\n4\ntrue\nB\n6\n",
            TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
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
    public delegate object HostDelegate(object[] arguments);

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
