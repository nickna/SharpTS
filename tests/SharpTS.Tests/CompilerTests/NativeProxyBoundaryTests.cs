using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeProxyBoundaryTests
{
    private const BindingFlags StaticMethods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    [Fact]
    public void NativeOperationsRefuseInjectedProxiesBeforeReadingOrInvokingAnyTrap()
    {
        var (_, assembly) = Compile(true);
        var runtime = assembly.GetType("$Runtime")!;
        object? Call(string name, params object?[] args) => Invoke(runtime, name, args);
        var counters = new Counters();
        var proxy = Proxy(counters, assembly);
        object empty = new Dictionary<string, object?>();
        object?[] arguments = [];
        var operations = new (string Name, Action Run)[]
        {
            ("get", () => Call("GetProperty", proxy, "value")),
            ("get index", () => Call("GetIndex", proxy, "value")),
            ("set", () => Call("SetProperty", proxy, "value", 8d)),
            ("set index", () => Call("SetIndex", proxy, "value", 8d)),
            ("has", () => Call("HasIn", "value", proxy)),
            ("hasOwn", () => Call("HasOwnPropertyHelper", proxy, "value")),
            ("keys", () => Call("GetKeys", proxy)),
            ("own names", () => Call("GetOwnPropertyNames", proxy)),
            ("own symbols", () => Call("GetOwnPropertySymbols", proxy)),
            ("values", () => Call("GetValues", proxy)),
            ("entries", () => Call("GetEntries", proxy)),
            ("descriptor", () => Call("ObjectGetOwnPropertyDescriptor", proxy, "value")),
            ("define", () => Call("ObjectDefineProperty", proxy, "value", empty)),
            ("delete", () => Call("DeleteProperty", proxy, "value")),
            ("strict delete", () => Call("DeletePropertyStrict", proxy, "value", true)),
            ("delete index", () => Call("DeleteIndex", proxy, "value")),
            ("strict delete index", () => Call("DeleteIndexStrict", proxy, "value", true)),
            ("get prototype", () => Call("ObjectGetPrototypeOf", proxy)),
            ("set prototype", () => Call("ObjectSetPrototypeOf", proxy, empty)),
            ("isExtensible", () => Call("ObjectIsExtensible", proxy)),
            ("preventExtensions", () => Call("ObjectPreventExtensions", proxy)),
            ("typeof", () => Call("TypeOf", proxy)),
            ("isArray", () => Call("IsArray", proxy)),
            ("invoke", () => Call("InvokeValue", proxy, arguments)),
            ("invoke with receiver", () => Call("InvokeMethodValue", empty, proxy, arguments)),
            ("Reflect get", () => Call("ReflectGet", proxy, "value", proxy)),
            ("Reflect set", () => Call("ReflectSet", proxy, "value", 8d, proxy)),
            ("Reflect define", () => Call("ReflectDefineProperty", proxy, "value", empty)),
            ("Reflect delete", () => Call("ReflectDeleteProperty", proxy, "value")),
            ("Reflect preventExtensions", () => Call("ReflectPreventExtensions", proxy)),
            ("Reflect setPrototypeOf", () => Call("ReflectSetPrototypeOf", proxy, empty)),
            ("Reflect ownKeys", () => Call("ReflectOwnKeys", proxy)),
            ("Reflect apply", () => Call("ReflectApply", proxy, empty, new List<object>())),
            ("Reflect construct", () => Call("ReflectConstruct", proxy, new List<object>(), proxy)),
            ("assign", () => Call("ObjectAssign", empty, new List<object> { proxy })),
            ("JSON", () => Call("JsonStringify", proxy)),
            ("JSON full", () => Call("JsonStringifyFull", proxy, null, null))
        };
        foreach (var operation in operations) AssertRejected(runtime, operation.Run, operation.Name);
        foreach (string name in new[] { "$FunctionCallWrapper", "$FunctionApplyWrapper", "$FunctionBindWrapper" })
        {
            object wrapper = Activator.CreateInstance(assembly.GetType(name)!, [proxy])!;
            AssertRejected(runtime, () => Call("InvokeValue", wrapper, new object?[] { empty, Array.Empty<object>() }), name);
        }
        var reviver = Activator.CreateInstance(assembly.GetType("$TSFunction")!, [null, typeof(NativeProxyBoundaryTests).GetMethod(nameof(Reviver))])!;
        AssertRejected(runtime, () => Call("ApplyReviver", new Dictionary<string, object?> { ["value"] = proxy }, "value", reviver), "reviver proxy value");
        AssertRejected(runtime, () => Call("ApplyReviver", proxy, "value", reviver), "reviver proxy holder");
        proxy.Revoke();
        AssertRejected(runtime, () => Call("GetProperty", proxy, "value"), "revoked proxy");
        Assert.Equal(0, counters.Reads);
        Assert.Equal(0, counters.Calls);

        // Ordinary guest dictionaries/arrays still follow their existing paths.
        var plain = new Dictionary<string, object?> { ["value"] = 3d };
        Assert.Equal(3d, Call("GetProperty", plain, "value"));
        Assert.Equal(new object[] { "value" }, Assert.IsAssignableFrom<IEnumerable<object>>(Call("GetKeys", plain)));
        Assert.Equal(true, Call("IsArray", new List<object> { 1d }));
        Assert.Equal("{\"value\":3}", Call("JsonStringify", plain));
    }

    [Fact]
    public void ManagedOperationsKeepProxyTrapsReceiverAndGuestErrorIdentity()
    {
        var (_, assembly) = Compile(false);
        var runtime = assembly.GetType("$Runtime")!;
        var counters = new Counters();
        var proxy = Proxy(counters, assembly);
        Assert.Equal(7d, Invoke(runtime, "GetProperty", proxy, "value"));
        Assert.Equal(true, Invoke(runtime, "HasIn", "value", proxy));
        Assert.Equal("function", Invoke(runtime, "TypeOf", proxy));
        object receiver = new Dictionary<string, object?>();
        counters.ExpectedReceiver = receiver;
        Assert.Equal("applied", Invoke(runtime, "InvokeMethodValue", receiver, proxy, new object[] { 1d }));
        Assert.True(counters.Reads >= 3);
        Assert.True(counters.Calls >= 3);
        proxy.Revoke();
        var error = Assert.Throws<TargetInvocationException>(() => Invoke(runtime, "GetProperty", proxy, "value"));
        object wrapped = Invoke(runtime, "WrapException", error.InnerException)!;
        Assert.Equal("$TypeError", wrapped.GetType().Name);
        Assert.Contains("revoked", wrapped.GetType().GetProperty("Message")!.GetValue(wrapped)!.ToString());
    }

    [Fact]
    public void NativeEmissionRemovesOnlyProxyBridgeTokens()
    {
        var (managed, managedAssembly) = Compile(false);
        var (native, nativeAssembly) = Compile(true);
        Assert.Null(managedAssembly.GetType("$Runtime")!.GetMethod("RejectNativeProxy", StaticMethods));
        Assert.NotNull(nativeAssembly.GetType("$Runtime")!.GetMethod("RejectNativeProxy", StaticMethods));
        string[] bridgeNames = ["TrapGetCompiled", "TrapSetCompiled", "TrapGetIndexCompiled", "TrapHasCompiled", "TrapApplyCompiled", "TrapGet", "TrapOwnKeys"];
        foreach (string name in bridgeNames)
        {
            Assert.Contains(name, UserStrings(managed));
            Assert.DoesNotContain(name, UserStrings(native));
        }
        Assert.DoesNotContain(UserStrings(native), name => name.StartsWith("Trap", StringComparison.Ordinal));
        // General CLR reflection is intentionally retained outside Proxy arms.
        using (var pe = new PEReader(new MemoryStream(native)))
        {
            var metadata = pe.GetMetadataReader();
            Assert.Contains(metadata.MemberReferences, handle => metadata.GetString(metadata.GetMemberReference(handle).Name) == "GetMethod");
        }
        Assert.DoesNotContain(nativeAssembly.GetReferencedAssemblies(), reference => reference.Name == "SharpTS");
    }

    [Fact]
    public void SuppliedRejectionPeerControlsEmissionRegardlessOfAmbientNativeSelection()
    {
        foreach (bool supplied in new[] { false, true })
        {
            var builder = new PersistedAssemblyBuilder(new AssemblyName("proxy_peer_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
            var owner = builder.DefineDynamicModule("main").DefineType("Probe", TypeAttributes.Public);
            var reject = owner.DefineMethod("Reject", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
            reject.GetILGenerator().Emit(OpCodes.Ldstr, "provided"); reject.GetILGenerator().Emit(OpCodes.Ret);
            var read = owner.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
            var il = read.GetILGenerator();
            int emittedArguments = 0;
            Action load = () => il.Emit(OpCodes.Ldarg_0);
            Action args = () => { emittedArguments++; il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, typeof(object)); };
            typeof(RuntimeEmitter).GetMethod("EmitProxyMethodCall", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(new RuntimeEmitter(TypeProvider.Runtime, false, !supplied), [il, load, "get_IsCallable", args, supplied ? reject : null]);
            il.Emit(OpCodes.Ret); owner.CreateType();
            using var bytes = new MemoryStream(); builder.Save(bytes);
            var assembly = LoadVerified(bytes.ToArray());
            Assert.Equal(supplied ? 0 : 1, emittedArguments);
            object result = assembly.GetType("Probe")!.GetMethod("Read")!.Invoke(null, [Proxy(new Counters())])!;
            Assert.Equal(supplied ? "provided" : (object)true, result);
        }
    }

    [Fact]
    public void RejectionMetadataRejectsForeignOwnersDuplicatesAndLateWrites()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("proxy_metadata_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("main");
        var owner = module.DefineType("Owner"); var other = module.DefineType("Other");
        var valid = owner.DefineMethod("Reject", MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        var invalid = other.DefineMethod("Reject", MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        var metadata = new EmittedReflectedMethodRuntime();
        Assert.Null(metadata.NativeProxyRejection);
        Assert.Throws<InvalidOperationException>(() => metadata.DeclareNativeProxyRejection(owner, invalid));
        metadata.DeclareNativeProxyRejection(owner, valid);
        Assert.Throws<InvalidOperationException>(() => metadata.DeclareNativeProxyRejection(owner, valid));
        Assert.False(typeof(EmittedReflectedMethodRuntime).GetProperty(nameof(EmittedReflectedMethodRuntime.NativeProxyRejection))!.SetMethod!.IsPublic);
        var complete = new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(module,
            new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow()));
        Assert.True(complete.ReflectedMethods.IsComplete);
        Assert.Throws<InvalidOperationException>(() => complete.ReflectedMethods.DeclareNativeProxyRejection(owner, valid));
    }

    public static object Reviver(object key, object value) => value;

    private sealed class Counters { public int Reads; public int Calls; public object? ExpectedReceiver; }
    private sealed class TrapGetter(Counters counters, BuiltInMethod trap)
    {
        public object Read() { counters.Reads++; return trap; }
    }

    private static SharpTSProxy Proxy(Counters counters, Assembly? assembly = null)
    {
        var target = new BuiltInMethod("target", 0, (_, _, _) => "target");
        var handler = new Dictionary<string, object?>();
        foreach (string name in new[] { "get", "set", "has", "ownKeys", "getOwnPropertyDescriptor", "deleteProperty", "getPrototypeOf", "setPrototypeOf", "isExtensible", "preventExtensions", "apply" })
        {
            var trap = new BuiltInMethod(name, 0, 4, (_, _, arguments) =>
            {
                counters.Calls++;
                return name switch
                {
                    "get" => 7d,
                    "ownKeys" => new List<object> { "value" },
                    "getOwnPropertyDescriptor" => new SharpTSObject(new Dictionary<string, object?> { ["value"] = 7d, ["writable"] = true, ["enumerable"] = true, ["configurable"] = true }),
                    "getPrototypeOf" => null,
                    "apply" => Apply(arguments),
                    _ => true
                };
            });
            if (assembly is null) handler[name] = trap;
            else
            {
                var getter = Activator.CreateInstance(assembly.GetType("$TSFunction")!,
                    [new TrapGetter(counters, trap), typeof(TrapGetter).GetMethod(nameof(TrapGetter.Read))])!;
                Invoke(assembly.GetType("$Runtime")!, "ObjectDefineProperty", handler, name,
                    new Dictionary<string, object?> { ["get"] = getter, ["configurable"] = true });
            }
        }
        return new SharpTSProxy(target, handler);

        object Apply(List<object?> arguments)
        {
            if (counters.ExpectedReceiver is not null) Assert.Same(counters.ExpectedReceiver, arguments[1]);
            return "applied";
        }
    }

    private static void AssertRejected(Type runtime, Action action, string operation)
    {
        var caught = Record.Exception(action);
        Assert.True(caught is TargetInvocationException, operation + ": " + (caught?.GetType().Name ?? "no exception"));
        var failure = (TargetInvocationException)caught!;
        object wrapped = Invoke(runtime, "WrapException", failure.InnerException)!;
        Assert.True(wrapped.GetType().Name == "$TypeError", operation + ": " + wrapped.GetType().Name);
        Assert.Contains("Proxy is not supported in native guest output", wrapped.GetType().GetProperty("Message")!.GetValue(wrapped)!.ToString());
    }

    private static object? Invoke(Type runtime, string name, params object?[] args) =>
        (runtime.GetMethod(name, StaticMethods) ?? throw new InvalidOperationException("Missing emitted helper: " + name)).Invoke(null, args);

    private static (byte[] Bytes, Assembly Assembly) Compile(bool native)
    {
        const string source = "console.log(JSON.stringify({ value: 1 }), Reflect.ownKeys({ value: 1 }).length);";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("proxy_boundary_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes();
        return (bytes, LoadVerified(bytes));
    }

    private static Assembly LoadVerified(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
        return Assembly.Load(bytes);
    }

    private static IEnumerable<string> UserStrings(byte[] bytes)
    {
        using var pe = new PEReader(new MemoryStream(bytes));
        var metadata = pe.GetMetadataReader();
        for (var handle = MetadataTokens.UserStringHandle(1); !handle.IsNil; handle = metadata.GetNextHandle(handle))
            yield return metadata.GetUserString(handle);
    }
}
