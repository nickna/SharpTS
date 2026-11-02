using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TopLevelUsingBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>resource;using resource=null;console.log(read()===null);", "true\n"];
        yield return ["hoisted-function", "/// <reference lib=\"esnext.disposable\" />\nfunction read(){return resource}using resource=null;console.log(read()===null);", "true\n"];
        yield return ["multiple-bindings", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>[first,second];using first=null,second=null;const values=read();console.log(values[0]===null,values[1]===null);", "true true\n"];
        yield return ["read-after-using-twice", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>resource;using resource=null;console.log(read()===null,read()===null);", "true true\n"];
        yield return ["class-source-order", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];class Before{static value=log.push('before')}const read=()=>resource;using resource=null;class After{static value=log.push('after')}console.log(read()===null,log.join(','));", "true before,after\n"];
        yield return ["async-call-tail", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>resource;using resource=null;async function done(){await Promise.resolve(0);console.log(read()===null)}done();", "true\n"];
        yield return ["inner-resource-does-not-replace-root", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>resource;using resource=null;{using resource={[Symbol.dispose](){}};console.log(resource!==read())}console.log(read()===null);", "true\ntrue\n"];
        yield return ["no-using-control", "/// <reference lib=\"esnext.disposable\" />\nconst resource=7;const read=()=>resource;console.log(read());", "7\n"];
        yield return ["root-resource-and-ordinary-shadow", "/// <reference lib=\"esnext.disposable\" />\nfunction read(){return resource}const readers:(()=>any)[]=[];{const resource=7;readers.push(()=>resource)}using resource=null;console.log(readers[0](),read()===null);", "7 true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void TopLevelResourcesUseTheirActualLexicalBinding(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Interpreted)
            Assert.Equal(expected, TestHarness.Run(source, mode));
        else
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
    }
}
