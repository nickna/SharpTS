using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class IndexedUpdateSymbolPropertyKeyTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const method: any = Math.max, key = Symbol('count');\nmethod[key] = 5;\nconst propertyKey: any = { [Symbol.toPrimitive]() { return key; } };\nconsole.log(method[propertyKey]++, method[key]);\n", "5 6\n"];
        yield return ["record-postfix", "let calls=0;const key=Symbol('key'),target:any={};target[key]=5;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(target[property]++,target[key],calls);\n", "string\nstring\n5 6 2\n"];
        yield return ["record-prefix", "let calls=0;const key=Symbol('key'),target:any={};target[key]=5;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(++target[property],target[key],calls);\n", "string\nstring\n6 6 2\n"];
        yield return ["native-postfix", "let calls=0;const key=Symbol('key'),target:any=Math.max;target[key]=5;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(target[property]++,target[key],calls);\n", "string\nstring\n5 6 2\n"];
        yield return ["native-prefix", "let calls=0;const key=Symbol('key'),target:any=Math.max;target[key]=5;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(++target[property],target[key],calls);\n", "string\nstring\n6 6 2\n"];
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
            var compiler = new ILCompiler("regression_2595_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
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

