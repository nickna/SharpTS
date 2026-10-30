using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NullishRequiredIteratorMethodTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "for (const method of [null, undefined]) {\n    const input: any = { 0: 7, length: 1, [Symbol.iterator]: method };\n    console.log(Array.from(input).join(','));\n    try { for (const value of input) {} }\n    catch (error) { console.log(error instanceof TypeError); }\n    try { const values = [...input]; }\n    catch (error) { console.log(error instanceof TypeError); }\n}\n", "7\ntrue\ntrue\n7\ntrue\ntrue\n"];
        yield return ["inherited-nullish", "for(const method of [null,undefined]){const input:any=Object.create({[Symbol.iterator]:method});input[0]=7;input.length=1;console.log(Array.from(input).join(','));try{for(const value of input){}}catch(error){console.log(error instanceof TypeError);}try{const values=[...input];}catch(error){console.log(error instanceof TypeError);}}\n", "7\ntrue\ntrue\n7\ntrue\ntrue\n"];
        yield return ["getter-nullish-once", "for(const method of [null,undefined]){let calls=0;const input:any={0:7,length:1,get [Symbol.iterator](){calls++;return method;}};console.log(Array.from(input).join(','),calls);try{for(const value of input){}}catch(error){console.log(error instanceof TypeError,calls);}try{const values=[...input];}catch(error){console.log(error instanceof TypeError,calls);}}\n", "7 1\ntrue 2\ntrue 3\n7 1\ntrue 2\ntrue 3\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { "interpreted", "compiled", "native-prepared" }
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
            var compiler = new ILCompiler("regression_2574_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
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

