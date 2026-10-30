using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class VmContextGlobalWriteBackTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('var value=7;value',ctx),ctx.value);", "7 7\n"];
        yield return ["assignment", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('var value=7;value=8;value',ctx),ctx.value);", "8 8\n"];
        yield return ["global-object-alias", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('var value=7;globalThis.value=9;value',ctx),ctx.value);", "9 9\n"];
        yield return ["lexical-private", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('let hidden=1;const fixed=2;var visible=3;visible',ctx),ctx.visible,typeof ctx.hidden,typeof ctx.fixed,Object.prototype.hasOwnProperty.call(ctx,'Promise'));", "3 3 undefined undefined false\n"];
        yield return ["loop-head", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('for(var i=0;i<2;i++){}i',ctx),ctx.i);", "2 2\n"];
        yield return ["block-var", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('if(true){var value=11;}value',ctx),ctx.value);", "11 11\n"];
        yield return ["script", "import * as vm from 'vm';const ctx:any=vm.createContext({});const script=new vm.Script('var value=12;value');console.log(script.runInContext(ctx),ctx.value);", "12 12\n"];
        yield return ["new-context", "import * as vm from 'vm';const ctx:any={};console.log(vm.runInNewContext('var value=13;value',ctx),ctx.value);", "13 13\n"];
        yield return ["existing-var", "import * as vm from 'vm';const ctx:any=vm.createContext({value:5});console.log(vm.runInContext('var value;value+=2;value',ctx),ctx.value);", "7 7\n"];
        yield return ["uninitialized-var", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('var value;typeof value',ctx),Object.prototype.hasOwnProperty.call(ctx,'value'),typeof ctx.value);", "undefined false undefined\n"];
        yield return ["initialized-undefined", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('var value=undefined;typeof value',ctx),Object.prototype.hasOwnProperty.call(ctx,'value'),typeof ctx.value);", "undefined true undefined\n"];
        yield return ["repeat", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('var value=7;value',ctx),ctx.value);console.log(vm.runInContext('value+=2;value',ctx),ctx.value);", "7 7\n9 9\n"];
        yield return ["global-data", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('globalThis.extra=14;14',ctx),ctx.extra);", "14 14\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PublishesActualGlobalVariablesWithoutLexicalBindings(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
