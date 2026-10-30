using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class SpecializedLiteralOverloadRuntimeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "function pick(x:string,y:number):'broad';function pick(x:'x',y:1):'literal';function pick(x:string,y:number):any{return 'literal';}const result:'literal'=pick('x',1);console.log(result);", "literal\n"];
        yield return ["spread-and-retained", "function pick(x:string,y:number):'broad';function pick(x:'x',y:1):'literal';function pick(x:string,y:number):any{return 'literal';}const args=['x',1] as const;const retained=pick;const result:'literal'=retained(...args);console.log(result,retained===pick);", "literal true\n"];
        yield return ["class-method", "class C{pick(x:string):'broad';pick(x:'x'):'literal';pick(x:string):any{return 'literal';}}const result:'literal'=new C().pick('x');console.log(result);", "literal\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] { "interpreted", "compiled", "native-prepared" }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void SelectedCheckedSignaturesKeepTheOriginalRuntimeCallable(string name, string source, string expected, string mode)
    {
        Assert.NotEmpty(name);
        if (mode == "interpreted") Assert.Equal(expected, TestHarness.RunInterpreted(source));
        else if (mode == "compiled")
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var statements = TestHarness.ParseOrThrow(source);
            var types = new TypeChecker().Check(statements);
            var compiler = new ILCompiler("specialized_literal_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
            compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
            var bytes = compiler.SaveToBytes();
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
            Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
        }
    }
}
