using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ExistingIdentifierLoopHeadRegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "async function run() {\n    let key: any = \"outer\";\n    const read = () => key;\n    for await (key of [\"one\", \"two\"]) {}\n    console.log(key, read());\n}\nrun();\n", "two two\n"];
        yield return ["sync-of", "function run(){let key:any=\"outer\";const read=()=>key;for (key of [1,2]){}console.log(key,read());}run();\n", "2 2\n"];
        yield return ["sync-in", "function run(){let key:any=\"outer\";const read=()=>key;for (key in {a:1,b:2}){}console.log(key,read());}run();\n", "b b\n"];
        yield return ["sync-of-no-capture", "function run(){let key:any=\"outer\";for (key of [1,2]){}console.log(key);}run();\n", "2\n"];
        yield return ["sync-of-uninitialized", "function run(){let key:any;for (key of [1,2]){}console.log(key);}run();\n", "2\n"];
        yield return ["async-uninitialized", "async function run(){let key:any;const read=()=>key;for await (key of [\"one\",\"two\"]){}console.log(key,read());}run();\n", "two two\n"];
        yield return ["async-no-capture", "async function run(){let key:any=\"outer\";for await (key of [\"one\",\"two\"]){}console.log(key);}run();\n", "two\n"];
    }

    public static IEnumerable<object[]> Modes()
    {
        foreach (var row in Cases())
        {
            yield return [row[0], row[1], row[2], "interpreted"];
            yield return [row[0], row[1], row[2], "compiled"];
            // #3159 separately tracks the native profile's async Array iterator
            // refusal. Every source retains its ordinary Node expectation above.
            if (((string)row[0]).StartsWith("sync", StringComparison.Ordinal))
                yield return [row[0], row[1], row[2], "native-prepared"];
        }
    }

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
            var compiler = new ILCompiler("regression_2586_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
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

