using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class Uint8ClampedConversionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["constructor-rounding-and-bounds", """
            const source=[-Infinity,-1,-0,0.49,0.5,0.51,1.5,2.5,3.5,254.5,254.51,255,Infinity,NaN];
            const value=new Uint8ClampedArray(source);let output='';
            for(let i=0;i<value.length;i++){if(i)output+=',';output+=value[i];}console.log(output);
            console.log(new Uint8Array([1.5,3.5])[0],new Uint8Array([1.5,3.5])[1]);
            """, "0,0,0,0,0,1,2,2,4,254,255,255,255,0\n1 3\n"];
        yield return ["indexed-and-bulk-writes", """
            const value=new Uint8ClampedArray(4);value[0]=1.5;const dynamic:any=value;dynamic[1]=3.5;
            value.fill(2.5,2);console.log(value[0],value[1],value[2],value[3]);
            value.set([0.5,254.5]);const copy=new Uint8ClampedArray(value);const slice=value.slice(0,2);
            console.log(copy[0],copy[1],slice[0],slice[1]);
            const source:any={0:1.5,1:3.5,length:2};const fromObject=new Uint8ClampedArray(source);
            console.log(fromObject[0],fromObject[1]);
            """, "2 4 2 2\n0 254 0 254\n2 4\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void WritesRoundBeforeNarrowing(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunUnchecked(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedWritesUseTheSameClamp(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var compiler = new ILCompiler("uint8_clamp_" + Guid.NewGuid().ToString("N"), false, true,
            null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, new TypeMap());
        byte[] bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }
}
