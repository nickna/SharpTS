using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmThrownValueIdentityTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var expression in new[] { "({x:1})", "new Error('outside')", "Symbol('key')", "'guest-string'", "7", "true", "null", "undefined" })
            yield return [expression, "import * as vm from 'node:vm';const value:any=" + expression + ";try{vm.runInNewContext('throw value',{value},{displayErrors:false});}catch(error){console.log(error===value);}", "true\n"];
        yield return ["internal-undefined", "import * as vm from 'node:vm';try{vm.runInNewContext('throw undefined',{},{displayErrors:false});}catch(error){console.log(typeof error);}", "undefined\n"];
        yield return ["rethrow", "import * as vm from 'node:vm';const value:any={cause:{x:1}};try{vm.runInNewContext('try{throw value}catch(e){throw e}',{value},{displayErrors:false});}catch(error){console.log(error===value,(error as any).cause===value.cause);}", "true true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesTheActualGuestAbruptValue(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
