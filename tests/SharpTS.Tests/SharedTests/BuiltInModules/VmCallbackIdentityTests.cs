using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmCallbackIdentityTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void ReturningTheActualSuppliedCallbackKeepsItsIdentity(ExecutionMode mode)
    {
        const string source = "import * as vm from 'node:vm';const f:any=()=>7;console.log(vm.runInNewContext('f',{f})===f);";
        Assert.Equal("true\n", TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
