using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectUnaryCoercionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const events:string[]=[];const value={valueOf(){events.push('convert');return 3n;}};const minus=-(value as unknown as number),not=~(value as unknown as number);console.log(typeof minus,String(minus),typeof not,String(not),events.join(','));", "bigint -3 bigint -4 convert,convert\n"];
        yield return ["number-and-plus", "let calls=0;const value={valueOf(){calls++;return '5';}};console.log(-(value as any),~(value as any),+(value as any),calls);", "-5 -6 5 3\n"];
        yield return ["exotic-priority", "const events:string[]=[];const value={get [Symbol.toPrimitive](){events.push('get');return function(hint:string){events.push(hint);return 3n;};},valueOf(){throw new Error('wrong fallback');}};const a=-(value as any),b=~(value as any);let rejected=false;try{+(value as any);}catch(e){rejected=e instanceof TypeError;}console.log(typeof a,String(a),typeof b,String(b),rejected,events.join(','));", "bigint -3 bigint -4 true get,number,get,number,get,number\n"];
        yield return ["ordered-throw", "const events:string[]=[];const value={get valueOf(){events.push('get-conversion');return function(){events.push('convert');throw new Error('stop');};}};const provider={get value(){events.push('operand');return value;}};try{-(provider.value as any);}catch(e){console.log((e as any).message);}console.log(events.join(','));", "stop\noperand,get-conversion,convert\n"];
        yield return ["ordinary-fallback", "const events:string[]=[];const value={valueOf(){events.push('valueOf');return {};},toString(){events.push('toString');return '6';}};console.log(-(value as any),~(value as any),events.join(','));", "-6 -7 valueOf,toString,valueOf,toString\n"];
        yield return ["nonprimitive-rejected", "let calls=0;const value={[Symbol.toPrimitive](){calls++;return {};}};try{-(value as any);}catch(e){console.log(e instanceof TypeError,calls);}try{~(value as any);}catch(e){console.log(e instanceof TypeError,calls);}", "true 1\ntrue 2\n"];
        yield return ["symbol-rejected", "let calls=0;const value={valueOf(){calls++;return Symbol('x');}};try{-(value as any);}catch(e){console.log(e instanceof TypeError,calls);}try{~(value as any);}catch(e){console.log(e instanceof TypeError,calls);}try{+(value as any);}catch(e){console.log(e instanceof TypeError,calls);}", "true 1\ntrue 2\ntrue 3\n"];
        yield return ["primitive-controls", "console.log(Object.is(-(null as any),-0),String(-(undefined as any)),-(true as any),~('3' as any),+('4' as any));console.log(String(-3n),String(~3n));", "true NaN -1 -4 4\n-3 -4\n"];
        yield return ["boxed-override", "let calls=0;const value:any=new Number(7);value.valueOf=function(){calls++;return 2;};console.log(-value,~value,calls);", "-2 -3 2\n"];
        yield return ["source-class", "let calls=0;class C{valueOf(){calls++;return 8n;}}const value=new C();const a=-(value as unknown as number),b=~(value as unknown as number);console.log(typeof a,String(a),typeof b,String(b),calls);", "bigint -8 bigint -9 2\n"];
        yield return ["async-operand", "let calls=0;const value={valueOf(){calls++;return 9n;}};async function run(){const a=-(await Promise.resolve(value) as any);const b=~(await Promise.resolve(value) as any);console.log(typeof a,String(a),typeof b,String(b),calls);}run();", "bigint -9 bigint -10 2\n"];
        yield return ["generator", "let calls=0;const value={valueOf(){calls++;return 10n;}};function* values(){yield -(value as any);yield ~(value as any);}for(const result of values())console.log(typeof result,String(result));console.log(calls);", "bigint -10\nbigint -11\n2\n"];
        yield return ["async-generator", "let calls=0;const value={valueOf(){calls++;return 11n;}};async function* values(){yield -(await Promise.resolve(value) as any);yield ~(await Promise.resolve(value) as any);}async function run(){for await(const result of values())console.log(typeof result,String(result));console.log(calls);}run();", "bigint -11\nbigint -12\n2\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ObjectUnaryCoercionPreservesOrderingAndKind(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedObjectUnaryOutputVerifies(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var map = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("object_unary_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, map);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }
}