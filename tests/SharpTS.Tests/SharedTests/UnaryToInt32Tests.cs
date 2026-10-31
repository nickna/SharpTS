using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UnaryToInt32Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "console.log(~Infinity,~NaN,~4294967296);", "-1 -1 -1\n"];
        yield return ["boundaries", "const values:number[]=[0,-0,1.9,-1.9,2147483647,2147483648,4294967295,4294967296,4294967297,-4294967297,-4294967297.75,9007199254740991,-9007199254740991];console.log(values.map(value=>String(~value)).join(','));", "-1,-1,-2,0,-2147483648,2147483647,0,-1,-2,0,0,0,-2\n"];
        yield return ["nonfinite-and-fraction", "const values:number[]=[NaN,Infinity,-Infinity,0.9,-0.9];console.log(values.map(value=>String(~value)).join(','));", "-1,-1,-1,-1,-1\n"];
        yield return ["numeric-formals", "function invert(value:number):number{return ~value;}console.log(invert(Infinity),invert(4294967297),invert(-4294967297.75));", "-1 -2 0\n"];
        yield return ["coerced-primitives", "console.log(~('4294967297' as any),~(true as any),~(null as any),~(undefined as any));", "-2 -2 -1 -1\n"];
        yield return ["converted-object", "const events:string[]=[];const value={[Symbol.toPrimitive](hint:string){events.push(hint);return 4294967297;}};const result=~(value as any);console.log(result,typeof result,events.join(','));", "-2 number number\n"];
        yield return ["bigint-control", "const value=18446744073709551616n;console.log(String(~value),typeof (~value),String(~(-1n)));", "-18446744073709551617 bigint 0\n"];
        yield return ["async", "async function run(){await Promise.resolve(0);console.log(~Infinity,~4294967297,~(-4294967297.75));}run();", "-1 -2 0\n"];
        yield return ["generator", "function* values(){yield ~Infinity;yield ~4294967297;yield ~(-4294967297.75);}console.log([...values()].join(','));", "-1,-2,0\n"];
        yield return ["async-generator", "async function* values(){yield ~(await Promise.resolve(Infinity));yield ~(await Promise.resolve(4294967297));}async function run(){for await(const value of values())console.log(value);}run();", "-1\n-2\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void UnaryNumbersUseEcmaToInt32(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedUnaryNumbersVerify(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var map = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("unary_to_int32_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, map);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }
}