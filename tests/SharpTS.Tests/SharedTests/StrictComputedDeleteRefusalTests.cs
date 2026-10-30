using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StrictComputedDeleteRefusalTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "'use strict';const target:any={};Object.defineProperty(target,'x',{value:7,configurable:false});try{console.log(delete target['x']);}catch(error){console.log(error instanceof TypeError,target.x);}try{console.log(delete (Math as any)['PI']);}catch(error){console.log(error instanceof TypeError,Math.PI>3);}", "true 7\ntrue true\n"];
        yield return ["sloppy-refusal", "const target:any={};Object.defineProperty(target,'x',{value:7,configurable:false});console.log(delete target['x'],target.x,delete(Math as any)['PI'],Math.PI>3);", "false 7 false true\n"];
        yield return ["object-key-once", "'use strict';const target:any={};Object.defineProperty(target,'x',{value:7,configurable:false});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return 'x';}};try{delete target[key];}catch(error){console.log(error instanceof TypeError,target.x,calls);}", "string\ntrue 7 1\n"];
        yield return ["numeric-key", "'use strict';const target:any={};Object.defineProperty(target,'1',{value:7,configurable:false});try{delete target[1];}catch(error){console.log(error instanceof TypeError,target[1]);}", "true 7\n"];
        yield return ["function-local-strict", "const target:any={};Object.defineProperty(target,'x',{value:7,configurable:false});function remove(){'use strict';try{delete target['x'];}catch(error){console.log(error instanceof TypeError,target.x);}}remove();", "true 7\n"];
        yield return ["configurable-and-missing", "'use strict';const target:any={};Object.defineProperty(target,'x',{value:7,configurable:true});console.log(delete target['x'],Object.hasOwn(target,'x'));Object.seal(target);console.log(delete target['missing']);", "true false\ntrue\n"];
        yield return ["class-control", "'use strict';class Box{}const target:any=new Box();Object.defineProperty(target,'x',{value:7,configurable:false});try{delete target['x'];}catch(error){console.log(error instanceof TypeError,target.x);}", "true 7\n"];
        yield return ["math-object-key", "'use strict';let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return 'PI';}};try{delete(Math as any)[key];}catch(error){console.log(error instanceof TypeError,Math.PI>3,calls);}", "string\ntrue true 1\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { "interpreted", "compiled", "native-prepared" }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void AppliesStrictnessToNonconfigurableStringKeys(string name, string source, string expected, string mode)
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
            var compiler = new ILCompiler("regression_3166_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
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
