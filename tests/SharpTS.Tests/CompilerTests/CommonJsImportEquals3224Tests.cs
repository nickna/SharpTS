using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[Collection("CompilationService")]
public sealed class CommonJsImportEquals3224Tests
{
    [Theory]
    [InlineData("module.exports=function(){return 7;};", "console.log(typeof value);", "function\n")]
    [InlineData("module.exports=function(){return 7;};", "console.log(value());", "7\n")]
    [InlineData("module.exports={value:7};", "console.log(value.value);", "7\n")]
    [InlineData("module.exports=7;", "console.log(value);", "7\n")]
    public void ImportsTheActualCommonJsExport(string producer, string consumer, string expected)
    {
        var files = new Dictionary<string, string>
        {
            ["value.cjs"] = producer,
            ["main.ts"] = "import value=require('./value.cjs');" + consumer,
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }

    [Fact]
    public void ReexportedImportEqualsKeepsTheActualValue()
    {
        var files = new Dictionary<string, string>
        {
            ["value.cjs"] = "module.exports=function(){return 7;};",
            ["barrel.ts"] = "export import value=require('./value.cjs');",
            ["main.ts"] = "import {value} from './barrel';console.log(value());",
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("7\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }
}
