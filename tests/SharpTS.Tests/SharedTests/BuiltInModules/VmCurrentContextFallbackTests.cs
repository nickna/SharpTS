using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmCurrentContextFallbackTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "import * as vm from \"node:vm\";console.log(vm.runInThisContext(\"1+2\"));", "3\n"];
        yield return ["filename-option", "import * as vm from \"node:vm\";console.log(vm.runInThisContext(\"40+2\",{filename:\"unit-vm.js\"}));", "42\n"];
        yield return ["declaration-and-completion", "import * as vm from \"node:vm\";console.log(vm.runInThisContext(\"var currentValue=7;currentValue\"));", "7\n"];
        yield return ["guest-error", "import * as vm from \"node:vm\";try{vm.runInThisContext(\"throw new Error('boom')\");}catch(error){console.log(String(error).includes(\"boom\"));}", "true\n"];
        yield return ["timeout", "import * as vm from \"node:vm\";try{vm.runInThisContext(\"while(true){}\",{timeout:20});console.log(\"returned\");}catch(error){console.log(String(error).includes(\"timed out\"));}", "true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void UsesTheExistingManagedFallbackWhenThereIsNoCallerInterpreter(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", mode));
    }
}
