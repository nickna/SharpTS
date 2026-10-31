using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReflectPrimitiveTargetValidationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "let keyCalls = 0;\nconst key: any = {\n    [Symbol.toPrimitive](hint: string) {\n        keyCalls++;\n        return \"marker\";\n    }\n};\nfunction check(label: string, target: any) {\n    try {\n        Reflect.get(target, key);\n        console.log(\"get\", label, \"returned\", keyCalls);\n    } catch (error) {\n        console.log(\"get\", label, error instanceof TypeError, keyCalls);\n    }\n    try {\n        Reflect.set(target, key, 7);\n        console.log(\"set\", label, \"returned\", keyCalls);\n    } catch (error) {\n        console.log(\"set\", label, error instanceof TypeError, keyCalls);\n    }\n}\ncheck(\"null\", null);\ncheck(\"undefined\", undefined);\ncheck(\"boolean\", true);\ncheck(\"number\", 1);\ncheck(\"string\", \"text\");\ncheck(\"bigint\", 1n);\ncheck(\"symbol\", Symbol(\"target\"));\nconsole.log(\"key calls\", keyCalls);\n", "get null true 0\nset null true 0\nget undefined true 0\nset undefined true 0\nget boolean true 0\nset boolean true 0\nget number true 0\nset number true 0\nget string true 0\nset string true 0\nget bigint true 0\nset bigint true 0\nget symbol true 0\nset symbol true 0\nkey calls 0\n", true];
        yield return ["retained-call-apply-bind", "const get:any=Reflect.get,set:any=Reflect.set;for(const target of [null,undefined,false,0,'x',1n,Symbol('s')]){let calls=0;const key:any={[Symbol.toPrimitive](){calls++;throw new Error('key');}};for(const invoke of [()=>get.call(null,target,key),()=>get.apply(null,[target,key]),()=>get.bind(null,target)(key),()=>set.call(null,target,key,1),()=>set.apply(null,[target,key,1]),()=>set.bind(null,target)(key,1)]){try{invoke();console.log('returned',calls);}catch(error){console.log(error instanceof TypeError,calls);}}}", "true 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\n", true];
        yield return ["argument-expression-order", "const events:string[]=[];function key(){events.push('expression');return {[Symbol.toPrimitive](){events.push('conversion');return 'x';}};}try{Reflect.get(0 as any,key() as any);}catch(error){console.log(error instanceof TypeError,events.join(','));}events.length=0;try{Reflect.set(0 as any,key() as any,(()=>{events.push('value');return 1;})());}catch(error){console.log(error instanceof TypeError,events.join(','));}", "true expression\ntrue expression,value\n", true];
        yield return ["valid-receiver-and-symbol", "const events:string[]=[];const target:any={},receiver:any={value:7};Object.defineProperty(target,'slot',{get:function(this:any){events.push('get');return this.value;},set:function(this:any,value:any){events.push('set');this.value=value;}});const key:any={[Symbol.toPrimitive](){events.push('key');return 'slot';}};console.log(Reflect.get(target,key,receiver),Reflect.set(target,key,8,receiver),receiver.value,events.join(','));function value(){}const fn:any=value,symbol=Symbol('x');fn[symbol]=9;const symbolKey:any={[Symbol.toPrimitive](){return symbol;}};console.log(Reflect.get(fn,symbolKey),Reflect.set(fn,symbolKey,10),fn[symbol]);", "7 true 8 key,get,key,set\n9 true 10\n", true];
        yield return ["awaited-and-spread-arguments", "async function key(){await new Promise<void>(resolve=>setTimeout(()=>resolve(),1));return 'slot';}async function value(){await new Promise<void>(resolve=>setTimeout(()=>resolve(),1));return 8;}async function run(){const target:any={slot:7};console.log(Reflect.get(target,await key()),Reflect.set(target,await key(),await value()),target.slot);const getArgs:[any,any]=[target,'slot'],setArgs:[any,any,any]=[target,'slot',9];console.log(Reflect.get(...getArgs),Reflect.set(...setArgs),target.slot);}run();", "7 true 8\n8 true 9\n", true];
        yield return ["proxy-key-order", "const events:string[]=[];const symbol=Symbol('x');const target:any={[symbol]:7};const proxy=new Proxy(target,{get(object:any,key:any,receiver:any){events.push('get');return Reflect.get(object,key,receiver);},set(object:any,key:any,value:any){events.push('set');object[key]=value;return true;}});const key:any={[Symbol.toPrimitive](){events.push('key');return symbol;}};console.log(Reflect.get(proxy,key),Reflect.set(proxy,key,8),target[symbol],events.join(','));", "7 true 8 key,get,key,set\n", false];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        ((bool)row[3] ? new[] { "interpreted", "compiled", "native-prepared" } : new[] { "interpreted", "compiled" })
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesThePublishedProgramAndReportedVariants(string name, string source, string expected, string mode)
    {
        Assert.NotEmpty(name);
        string actual;
        if (mode == "interpreted") actual = TestHarness.RunInterpreted(source);
        else if (mode == "compiled")
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            actual = output;
        }
        else
        {
            Assert.Equal("native-prepared", mode);
            var statements = TestHarness.ParseOrThrow(source);
            var types = new TypeChecker().Check(statements);
            var compiler = new ILCompiler("public_reflect_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
            compiler.EnableNativeAotProjectOutput();
            compiler.Compile(statements, types);
            var bytes = compiler.SaveToBytes();
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
            actual = TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20));
        }
        Assert.Equal(expected, actual);
    }
}
