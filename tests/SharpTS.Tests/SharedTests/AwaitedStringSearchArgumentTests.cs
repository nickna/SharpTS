using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AwaitedStringSearchArgumentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "async function argument() { await Promise.resolve(0); return \"b\"; }\nasync function run() { console.log(\"abc\".includes(await argument(), 0)); }\nrun().catch((error: any) => console.log(\"unexpected\", error && error.name));\n", "true\n"];
        yield return ["awaited-position", "async function position(){await Promise.resolve(0);return 1;}async function run(){console.log('abc'.includes('b',await position()));}run();", "true\n"];
        yield return ["all-search-helpers", "async function argument(){await Promise.resolve(0);return 'b';}async function position(){await Promise.resolve(0);return 1;}async function run(){console.log('abc'.includes(await argument(),await position()),'abc'.startsWith(await argument(),await position()),'abc'.endsWith(await argument(),(await position())+1));}run();", "true true true\n"];
        yield return ["actual-suspension-and-order", "const events:string[]=[];function receiver(){events.push('receiver');return 'abc';}async function argument(){events.push('argument-start');const result=await new Promise<string>(resolve=>setTimeout(()=>resolve('b'),1));events.push('argument-end');return result;}async function position(){events.push('position-start');const result=await new Promise<number>(resolve=>setTimeout(()=>resolve(1),1));events.push('position-end');return result;}async function run(){console.log(receiver().includes(await argument(),await position()),events.join(','));}run();", "true receiver,argument-start,argument-end,position-start,position-end\n"];
        yield return ["conversion-after-arguments", "const events:string[]=[];const search:any={toString(){events.push('search-conversion');return 'b';}};async function argument(){events.push('argument');await Promise.resolve(0);return search;}async function position(){events.push('position');await new Promise<void>(resolve=>setTimeout(()=>resolve(),1));return {valueOf(){events.push('position-conversion');return 1;}} as any;}async function run(){console.log('abc'.includes(await argument(),await position()),events.join(','));}run();", "true argument,position,search-conversion,position-conversion\n"];
        yield return ["abrupt-await", "const events:string[]=[];const search:any={toString(){events.push('search-conversion');return 'b';}};async function position():Promise<number>{events.push('position');await new Promise<void>(resolve=>setTimeout(()=>resolve(),1));throw new Error('stop');}async function run(){try{console.log('abc'.includes(search,await position()));}catch(error){console.log((error as any).message,events.join(','));}}run();", "stop position\n"];
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
            var compiler = new ILCompiler("regression_2700_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
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
