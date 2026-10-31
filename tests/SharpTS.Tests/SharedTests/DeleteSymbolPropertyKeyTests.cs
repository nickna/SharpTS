using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DeleteSymbolPropertyKeyTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const symbol = Symbol('extra');\nconst target: any = {};\ntarget[symbol] = 7;\nconst key: any = { [Symbol.toPrimitive]() { return symbol; } };\nconsole.log(delete target[key], Object.hasOwn(target, symbol));\n", "true false\n"];
        yield return ["record-ordinary", "function* values(){}let calls=0;const key=Symbol('key'),target:any={};target[key]=7;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(delete target[property],Object.hasOwn(target,key),calls);\n", "string\ntrue false 1\n"];
        yield return ["record-strict", "\"use strict\";function* values(){}let calls=0;const key=Symbol('key'),target:any={};target[key]=7;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(delete target[property],Object.hasOwn(target,key),calls);\n", "string\ntrue false 1\n"];
        yield return ["generator-ordinary", "function* values(){}let calls=0;const key=Symbol('key'),target:any=values();target[key]=7;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(delete target[property],Object.hasOwn(target,key),calls);\n", "string\ntrue false 1\n"];
        yield return ["generator-strict", "\"use strict\";function* values(){}let calls=0;const key=Symbol('key'),target:any=values();target[key]=7;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(delete target[property],Object.hasOwn(target,key),calls);\n", "string\ntrue false 1\n"];
        yield return ["abrupt-key", "let calls=0;const target:any={};const key:any={[Symbol.toPrimitive](){calls++;throw new Error('stop');}};try{delete target[key];}catch(error){console.log((error as any).message,calls);}\n", "stop 1\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { "compiled", "native-prepared" }
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
            var compiler = new ILCompiler("regression_2579_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
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
