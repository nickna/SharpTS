using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.Execution;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ProxyStackGuardTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["coercion-original", "const root:any={};const intermediary:any=new Proxy(Object.create(root),{});const leaf:any=Object.create(intermediary);Object.setPrototypeOf(root,leaf);try{console.log(String(leaf));}catch(error){console.log(error instanceof RangeError);}", "true\n"];
        yield return ["symbol-cycle", "const root:any={},key=Symbol(\"missing\");const proxy:any=new Proxy(Object.create(root),{}),leaf:any=Object.create(proxy);Object.setPrototypeOf(root,leaf);try{console.log(leaf[key]);}catch(error){console.log(error instanceof RangeError);}console.log(Object.getPrototypeOf(root)===leaf);", "true\ntrue\n"];
        yield return ["string-cycle", "const root:any={};const proxy:any=new Proxy(Object.create(root),{}),leaf:any=Object.create(proxy);Object.setPrototypeOf(root,leaf);try{console.log(Reflect.get(leaf,\"missing\"));}catch(error){console.log(error instanceof RangeError);}console.log(Object.getPrototypeOf(root)===leaf);", "true\ntrue\n"];
        yield return ["live-trap-breaks-cycle", "const root:any={},key=Symbol(\"key\");let reads=0;const handler:any={get get(){reads++;return reads===100?function(target:any,property:any,receiver:any){return property===key?7:99;}:undefined;}};const proxy:any=new Proxy(Object.create(root),handler),leaf:any=Object.create(proxy);Object.setPrototypeOf(root,leaf);console.log(leaf[key],reads);", "7 100\n"];
        yield return ["trap-getter-abrupt", "const marker={},proxy:any=new Proxy({},{get get():any{throw marker;}});try{console.log(proxy.missing);}catch(error){console.log(error===marker);}console.log(\"after\");", "true\nafter\n"];
        yield return ["revoked-first", "let reads=0;const pair:any=Proxy.revocable({},{get get(){reads++;return undefined;}});pair.revoke();try{console.log(pair.proxy.missing);}catch(error){console.log(error instanceof TypeError,reads);}console.log(\"after\");", "true 0\nafter\n"];
        yield return ["ordinary-forwarding", "const key=Symbol(\"key\");let value:any={present:7,[key]:9};for(let i=0;i<32;i++)value=new Proxy(value,{});console.log(value.present,value[key]);", "7 9\n"];
        yield return ["finite-two-hundred", "const root:any={},key=Symbol(\"key\");let reads=0;const handler:any={get get(){reads++;return reads===200?function(target:any,property:any,receiver:any){return property===key?7:99;}:undefined;}};const proxy:any=new Proxy(Object.create(root),handler),leaf:any=Object.create(proxy);Object.setPrototypeOf(root,leaf);console.log(leaf[key],reads);", "7 200\n"];
        yield return ["scope-resets", "const root:any={},key=Symbol(\"missing\"),proxy:any=new Proxy(Object.create(root),{}),leaf:any=Object.create(proxy);Object.setPrototypeOf(root,leaf);for(let i=0;i<3;i++){try{console.log(leaf[key]);}catch(error){console.log(error instanceof RangeError);}console.log(new Proxy({value:7},{}).value);}", "true\n7\ntrue\n7\ntrue\n7\n"];
        yield return ["distinct-proxies-two-hundred", "const key=Symbol(\"key\");let value:any={present:7,[key]:9};for(let i=0;i<200;i++)value=new Proxy(value,{});console.log(value.present,value[key]);", "7 9\n"];
        yield return ["recursive-trap", "let proxy:any;proxy=new Proxy({},{get(target:any,key:any,receiver:any):any{return Reflect.get(proxy,key,receiver);}});try{console.log(proxy.missing);}catch(error){console.log(error instanceof RangeError);}console.log(new Proxy({value:7},{}).value);", "true\n7\n"];
        yield return ["noncallable-trap", "const proxy:any=new Proxy({},{get:1} as any);try{console.log(Reflect.get(proxy,\"missing\"));}catch(error){console.log(error instanceof TypeError);}console.log(\"after\");", "true\nafter\n"];
        yield return ["guest-error-identity", "const marker=new RangeError(\"marker\"),proxy:any=new Proxy({},{get(){throw marker;}});try{console.log(Reflect.get(proxy,\"missing\"));}catch(error){console.log(error===marker,error instanceof RangeError);}console.log(\"after\");", "true true\nafter\n"];
        yield return ["recursive-dot-trap", "let proxy:any;proxy=new Proxy({},{get():any{return proxy.missing;}});try{console.log(proxy.missing);}catch(error){console.log(error instanceof RangeError);}console.log(new Proxy({value:7},{}).value);", "true\n7\n"];
        yield return ["recursive-index-trap", "const key=Symbol(\"key\");let proxy:any;proxy=new Proxy({},{get():any{return proxy[key];}});try{console.log(proxy[key]);}catch(error){console.log(error instanceof RangeError);}console.log(new Proxy({value:7},{}).value);", "true\n7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void RecursionIsCatchableWithoutChangingLiveProxyBehavior(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Fact]
    public async Task OverlappingThreadsKeepIndependentGetBudgets()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = Task.Run(() => ReadChain(() =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
        }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        try
        {
            var second = Task.Run(() => ReadChain(() => { }));
            Assert.Equal(7d, await second.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { release.Set(); }
        Assert.Equal(7d, await first.WaitAsync(TimeSpan.FromSeconds(10)));

        static double ReadChain(Action atLeaf)
        {
            using var interpreter = new Interpreter(new StringWriter(), new StringWriter());
            var target = new SharpTSObject([]);
            target.DefineGetter("value", new Getter(atLeaf));
            object value = target;
            for (int i = 0; i < 200; i++)
                value = new SharpTSProxy(value, new SharpTSObject([]));
            return Assert.IsType<double>(((SharpTSProxy)value).TrapGet("value", interpreter));
        }
    }

    [Fact]
    public void SoftErrorProtocolRecognizesOnlyItsActualCoreErrors()
    {
        var foreign = new Exception("foreign");
        foreign.Data["Value"] = new SharpTSRangeError("forged");
        Assert.Null(SharpTSProxy.GetPropertyErrorInfo(foreign));
        Assert.Equal(["TypeError", "other"], Assert.IsType<string[]>(SharpTSProxy.GetPropertyErrorInfo(
            new SharpTS.Runtime.Exceptions.ThrowException(new SharpTSTypeError("other")))));
        Assert.Null(SharpTSProxy.GetPropertyErrorInfo(new SharpTS.Runtime.Exceptions.ThrowException(
            new SharpTSError("other") { Name = "RangeError" })));
        Assert.Equal(["RangeError", "owned"], Assert.IsType<string[]>(SharpTSProxy.GetPropertyErrorInfo(
            new SharpTS.Runtime.Exceptions.ThrowException(new SharpTSRangeError("owned")))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullNameLookalikesKeepTheLegacyGetOperation(bool derivesFromCore)
    {
        var statements = new Parser(new Lexer("const p = new Proxy({}, {});").ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker();
        var types = checker.Check(statements);
        var compiler = new ILCompiler("proxy_lookalike_" + Guid.NewGuid().ToString("N"), false, true,
            null, null, null, OutputTarget.Dll);
        compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
        var runtime = Assembly.Load(bytes).GetType("$Runtime")!;

        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("proxy_lookalike_owner_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var owner = assembly.DefineDynamicModule("main").DefineType(typeof(SharpTSProxy).FullName!,
            TypeAttributes.Public, derivesFromCore ? typeof(SharpTSProxy) : typeof(object));
        if (derivesFromCore)
        {
            var constructor = owner.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, []);
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Newobj, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Call, typeof(SharpTSProxy).GetConstructor([typeof(object), typeof(object)])!);
            il.Emit(OpCodes.Ret);
        }
        else owner.DefineDefaultConstructor(MethodAttributes.Public);
        var calls = owner.DefineField("Calls", typeof(int), FieldAttributes.Public);
        var key = owner.DefineField("Key", typeof(object), FieldAttributes.Public);
        var receiver = owner.DefineField("Receiver", typeof(object), FieldAttributes.Public);
        var failure = owner.DefineField("Failure", typeof(Exception), FieldAttributes.Public);
        Define("TrapGetCompiled", [typeof(object), typeof(object), typeof(Func<object, object, object, object?>),
            typeof(Func<object, string, object?>), typeof(Func<object, string, object?>),
            typeof(Func<object, object, object?>)], 2);
        Define("TrapGetIndexCompiled", [typeof(object), typeof(Func<object, object, object?>),
            typeof(Func<object, string, object?>), typeof(Func<object, object, object?>)], 0);
        var lookalikeType = owner.CreateType()!;
        var lookalike = Activator.CreateInstance(lookalikeType)!;
        var explicitReceiver = new object();
        var operations = new (string Name, object?[] Arguments, object Key, object Receiver)[]
        {
            ("ReflectGet", [lookalike, "value", explicitReceiver], "value", explicitReceiver),
            ("GetProperty", [lookalike, "value"], "value", lookalike),
            ("GetIndex", [lookalike, 1d], "1", lookalike)
        };
        foreach (var operation in operations)
        {
            Assert.Equal("legacy", runtime.GetMethod(operation.Name)!.Invoke(null, operation.Arguments));
            Assert.Equal(operation.Key, lookalikeType.GetField("Key")!.GetValue(lookalike));
            Assert.Same(operation.Receiver, lookalikeType.GetField("Receiver")!.GetValue(lookalike));
        }
        var marker = new InvalidOperationException("foreign");
        lookalikeType.GetField("Failure")!.SetValue(lookalike, marker);
        foreach (var operation in operations)
        {
            var error = Assert.Throws<TargetInvocationException>(() =>
                runtime.GetMethod(operation.Name)!.Invoke(null, operation.Arguments));
            Assert.Same(marker, error.InnerException);
        }
        Assert.Equal(6, lookalikeType.GetField("Calls")!.GetValue(lookalike));

        void Define(string name, Type[] parameters, int receiverArgument)
        {
            var method = owner.DefineMethod(name, MethodAttributes.Public | MethodAttributes.HideBySig, typeof(object), parameters);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldfld, calls);
            il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stfld, calls);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, key);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg, receiverArgument); il.Emit(OpCodes.Stfld, receiver);
            var success = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, failure); il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, success); il.Emit(OpCodes.Throw);
            il.MarkLabel(success); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldstr, "legacy"); il.Emit(OpCodes.Ret);
        }
    }

    private sealed class Getter(Action read) : ISharpTSCallable
    {
        public int Arity() => 0;
        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            read();
            return 7d;
        }
    }
}
