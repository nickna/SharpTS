using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DescriptorSymbolKeyConversionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const symbol = Symbol('key'), target: any = {};Object.defineProperty(target, symbol, { value: 7 });const key: any = { [Symbol.toPrimitive](hint: string) { console.log(hint); return symbol; } };const descriptor: any = Reflect.getOwnPropertyDescriptor(target, key);console.log(descriptor.value);", "string\n7\n"];
        yield return ["object-and-reflect-once", "let calls=0;const symbol=Symbol('key'),target:any={};Object.defineProperty(target,symbol,{value:7,configurable:true});const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return symbol;}};console.log(Object.getOwnPropertyDescriptor(target,key)!.value,Reflect.getOwnPropertyDescriptor(target,key)!.value,calls);", "string\nstring\n7 7 2\n"];
        yield return ["accessor-without-value-read", "let calls=0,reads=0;const symbol=Symbol('key'),target:any={};Object.defineProperty(target,symbol,{get(){reads++;return 7;},configurable:true});const key:any={[Symbol.toPrimitive](){calls++;return symbol;}};const descriptor:any=Reflect.getOwnPropertyDescriptor(target,key);console.log(typeof descriptor.get,reads,descriptor.enumerable,descriptor.configurable,calls);", "function 0 false true 1\n"];
        yield return ["missing-symbol", "let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return Symbol('missing');}};console.log(Reflect.getOwnPropertyDescriptor({},key)===undefined,calls);", "true 1\n"];
        yield return ["abrupt-and-nonprimitive", "let calls=0;const throwing:any={[Symbol.toPrimitive](){calls++;throw new Error('stop');}};try{Reflect.getOwnPropertyDescriptor({},throwing);}catch(error){console.log((error as any).message,calls);}const bad:any={[Symbol.toPrimitive](){calls++;return {};}};try{Object.getOwnPropertyDescriptor({},bad);}catch(error){console.log(error instanceof TypeError,calls);}", "stop 1\ntrue 2\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PropertyKeyConversionRetainsSymbolIdentity(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedDescriptorsVerifyBeforeExecution(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = TestHarness.ParseOrThrow(source);
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("descriptor_key_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }

    [Theory, ModeData]
    public void ProxyReceivesTheOnceConvertedSymbol(ExecutionMode mode)
    {
        const string source = "const events:string[]=[];const symbol=Symbol('key'),target:any={};Object.defineProperty(target,symbol,{value:7,configurable:true});const proxy=new Proxy(target,{getOwnPropertyDescriptor(object:any,key:any){events.push(key===symbol?'trap-symbol':'wrong-key');return Reflect.getOwnPropertyDescriptor(object,key);}});const key:any={[Symbol.toPrimitive](hint:string){events.push(hint);return symbol;}};console.log(Reflect.getOwnPropertyDescriptor(proxy,key)!.value,events.join(','));";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal("7 string,trap-symbol\n", output);
        }
        else Assert.Equal("7 string,trap-symbol\n", TestHarness.Run(source, mode));
    }
}
