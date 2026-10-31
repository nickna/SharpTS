using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmConsoleGlobalTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original","import * as vm from \"node:vm\";console.log(vm.runInNewContext(\"var console;typeof console\"));","object\n"];
        yield return ["presence","import * as vm from \"node:vm\";console.log(vm.runInNewContext(\"typeof console+\\\":\\\"+typeof globalThis.console\"));","object:object\n"];
        yield return ["identity","import * as vm from \"node:vm\";console.log(vm.runInNewContext(\"var console;console===globalThis.console\"));","true\n"];
        yield return ["method-value","import * as vm from \"node:vm\";console.log(vm.runInNewContext(\"var console;typeof console[\\\"log\\\"]+\\\":\\\"+typeof console[\\\"error\\\"]\"));","function:function\n"];
        yield return ["descriptor","import * as vm from \"node:vm\";console.log(vm.runInNewContext(\"var console;const d=Object.getOwnPropertyDescriptor(globalThis,\\\"console\\\");[d.writable,d.enumerable,d.configurable].join(\\\":\\\")\"));","true:false:true\n"];
        yield return ["seeded","import * as vm from \"node:vm\";const ctx:any={console:{marker:7}};console.log(vm.runInNewContext(\"var console;console.marker+\\\":\\\"+(console===globalThis.console)\",ctx));","7:true\n"];
        yield return ["seeded-undefined","import * as vm from \"node:vm\";const ctx:any={console:undefined};console.log(vm.runInNewContext(\"var console;typeof console\",ctx));","undefined\n"];
        yield return ["lexical","import * as vm from \"node:vm\";console.log(vm.runInNewContext(\"let console=7;String(console)+\\\":\\\"+typeof globalThis.console\"));","7:object\n"];
        yield return ["unknown-name","import * as vm from \"node:vm\";console.log(vm.runInNewContext(\"var neverExistingNamespace;typeof neverExistingNamespace\"));","undefined\n"];
        yield return ["delete","import * as vm from \"node:vm\";console.log(vm.runInNewContext(\"delete globalThis.console;typeof console\"));","undefined\n"];
        yield return ["replace","import * as vm from \"node:vm\";console.log(vm.runInNewContext(\"globalThis.console=7;String(console)\"));","7\n"];
        yield return ["same-context-data","import * as vm from \"node:vm\";const ctx:any=vm.createContext({});vm.runInContext(\"console.marker=7\",ctx);console.log(vm.runInContext(\"var console;String(console.marker)\",ctx),Object.prototype.hasOwnProperty.call(ctx,\"console\"));","7 false\n"];
        yield return ["different-contexts","import * as vm from \"node:vm\";vm.runInNewContext(\"console.marker=7\");console.log(vm.runInNewContext(\"typeof console.marker\"));","undefined\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesTheActualConsoleObjectAcrossVarInstantiation(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
