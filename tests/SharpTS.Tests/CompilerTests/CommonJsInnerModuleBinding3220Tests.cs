using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[Collection("CompilationService")]
public sealed class CommonJsInnerModuleBinding3220Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "let value=7;function outer(){function inner(){return value;}return inner;}const read=outer();console.log(read());module.exports={read};\n", "7\n"];
        yield return ["reassignment", "let value=7;function outer(){function inner(){return value;}return inner;}const read=outer();value=9;console.log(read());module.exports={read};", "9\n"];
        yield return ["nested", "let value=7;function outer(){function middle(){function inner(){return value;}return inner;}return middle();}console.log(outer()());module.exports={};", "7\n"];
        yield return ["local-shadow", "let value=7;function outer(){let value=8;function inner(){return value;}return inner;}console.log(outer()());module.exports={};", "8\n"];
        yield return ["class-method", "let value=7;class Reader{read(){function inner(){return value;}return inner();}}console.log(new Reader().read());module.exports={Reader};", "7\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void UsesTheActualCommonJsModuleStorage(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string> { ["main.cjs"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.cjs"));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.cjs", ExecutionMode.Compiled));
    }

    [Fact]
    public void SameNamedVariablesInDifferentModulesStayIndependent()
    {
        var files = new Dictionary<string, string>
        {
            ["a.cjs"] = "let value=7;function outer(){function inner(){return value;}return inner;}module.exports=outer();",
            ["b.cjs"] = "let value=9;function outer(){function inner(){return value;}return inner;}module.exports=outer();",
            ["main.ts"] = "import first=require('./a.cjs');import second=require('./b.cjs');console.log(first(),second());",
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("7 9\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }
}
