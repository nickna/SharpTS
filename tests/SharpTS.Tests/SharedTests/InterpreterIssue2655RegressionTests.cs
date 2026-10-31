using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2655RegressionTests
{
    [Theory, ModeData]
    public void OriginalIssueSourceRemainsCorrect(ExecutionMode mode)
    {
        const string source = "const resolve = Promise.resolve;\nconsole.log(Promise.resolve === resolve);\n";
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["stable-standard-methods", "const owner:any=Promise;for(const name of [\"all\",\"race\",\"resolve\",\"reject\",\"allSettled\",\"any\",\"withResolvers\"]){const method=owner[name];console.log(name,owner[name]===method);}const first=Promise.resolve;console.log(Reflect.get(Promise,\"resolve\")===first,first===Promise.resolve);", "all true\nrace true\nresolve true\nreject true\nallSettled true\nany true\nwithResolvers true\ntrue true\n"];
        yield return ["actual-receiver", "class Derived extends Promise<number>{}const resolve=Promise.resolve;const value=(resolve as any).call(Derived,7);console.log(value instanceof Derived);value.then((result:number)=>console.log(result));try{(resolve as any).call({},7);console.log(\"accepted\");}catch(error){console.log(error instanceof TypeError);}", "true\ntrue\n7\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OrderedProtocolControls(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void BuiltInFunctionMetadataRemainsRealmLocal()
    {
        Assert.Equal("local\n", TestHarness.RunInterpreted("Object.defineProperty(Promise.resolve, \"name\", {value:\"local\"});console.log(Promise.resolve.name);"));
        Assert.Equal("resolve\n", TestHarness.RunInterpreted("console.log(Promise.resolve.name);"));
    }
}
