using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class MathConstantValueLookupTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const math:any=Math;console.log(math.PI,Math.PI>3);(Math as any).extra=1;console.log(math.PI,Math.PI>3);", "3.141592653589793 true\n3.141592653589793 true\n"];
        yield return ["all-constant-values-and-flags", "const math:any=Math,names=['E','LN10','LN2','LOG10E','LOG2E','PI','SQRT1_2','SQRT2'];let valid=true;for(const name of names){const descriptor:any=Object.getOwnPropertyDescriptor(math,name);valid=valid&&typeof math[name]==='number'&&math[name]>0&&descriptor.value===math[name]&&!descriptor.writable&&!descriptor.enumerable&&!descriptor.configurable;}console.log(valid,Object.keys(math).includes('PI'));", "true false\n"];
        yield return ["mutation-and-method-identity", "const math:any=Math;const max=math.max;math.extra=7;console.log(math.PI>3,Math.PI>3,math.extra,math.max===Math.max,max===math.max,Object.keys(math).includes('PI'));console.log(delete math.extra,math.extra===undefined,math.PI>3);", "true true 7 true true false\ntrue true true\n"];
        yield return ["sloppy-readonly-write", "const math:any=Math;const initial=math.PI;math.PI=0;math['E']=0;console.log(math.PI===initial,math.PI>3,math.E>2);", "true true true\n"];
        yield return ["reflect-after-real-await", "const math:any=Math;async function run(){await new Promise<void>(resolve=>setTimeout(()=>resolve(),1));const descriptor:any=Object.getOwnPropertyDescriptor(math,'PI');console.log(Reflect.get(math,'PI')>3,descriptor.value===math.PI,descriptor.writable,descriptor.enumerable,descriptor.configurable);}run();", "true true false false false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { "interpreted", "compiled", "native-prepared" }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesMathConstantsOnOrdinaryValueLookups(string name, string source, string expected, string mode)
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
            var compiler = new ILCompiler("regression_3170_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
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
