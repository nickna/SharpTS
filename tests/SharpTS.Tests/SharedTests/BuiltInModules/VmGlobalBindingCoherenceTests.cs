using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmGlobalBindingCoherenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["virtual-standard-globals", "import * as vm from 'node:vm';console.log(vm.runInNewContext('typeof Function'),vm.runInNewContext('typeof Map'),vm.runInNewContext('undefined===undefined'));", "function function true\n"];
        yield return ["escaped-context-read", "import * as vm from 'node:vm';const ctx:any={value:7};const f:any=vm.runInNewContext('()=>value',ctx);console.log(f());", "7\n"];
        yield return ["escaped-global-alias", "import * as vm from 'node:vm';const ctx:any={value:7};const f:any=vm.runInNewContext('()=>globalThis.value',ctx);console.log(f());", "7\n"];
        yield return ["escaped-later-property", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const f:any=vm.runInContext('()=>value',ctx);vm.runInContext('globalThis.value=7',ctx);console.log(f());", "7\n"];
        yield return ["lexical-tdz", "import * as vm from 'node:vm';try{vm.runInNewContext('value;let value=9',{value:1});}catch(e){console.log((e as Error).name);}", "ReferenceError\n"];
        yield return ["new-property", "import * as vm from 'node:vm';const ctx:any={};console.log(vm.runInNewContext('globalThis.value=7;typeof value;',ctx),ctx.value);", "number 7\n"];
        yield return ["seeded-alias", "import * as vm from 'node:vm';const ctx:any={value:1};console.log(vm.runInNewContext('globalThis.value=7;value;',ctx),ctx.value);", "7 7\n"];
        yield return ["lexical-shadow", "import * as vm from 'node:vm';const ctx:any={value:1};console.log(vm.runInNewContext('let value=9;globalThis.value;',ctx),ctx.value);", "1 1\n"];
        yield return ["nullish-properties", "import * as vm from 'node:vm';const ctx:any={x:null,y:undefined};console.log(vm.runInNewContext('x===null && y===undefined && globalThis.x===x && globalThis.y===y;',ctx));", "true\n"];
        yield return ["var-alias", "import * as vm from 'node:vm';const ctx:any={value:1};console.log(vm.runInNewContext('var value=3;globalThis.value=7;value;',ctx),ctx.value);", "7 7\n"];
        yield return ["assignment", "import * as vm from 'node:vm';const ctx:any={};console.log(vm.runInNewContext('globalThis.value=1;value=9;globalThis.value;',ctx),ctx.value);", "9 9\n"];
        yield return ["deletion", "import * as vm from 'node:vm';const ctx:any=vm.createContext({value:1});console.log(vm.runInContext('delete globalThis.value;typeof value;',ctx),'value' in ctx);console.log(vm.runInContext('globalThis.value=9;value;',ctx),ctx.value);", "undefined false\n9 9\n"];
        yield return ["strict-unresolved", "import * as vm from 'node:vm';const ctx:any={};try{vm.runInNewContext('\"use strict\";missingValue=7;',ctx);}catch(e){console.log((e as Error).name,'missingValue' in ctx);}", "ReferenceError false\n"];
        yield return ["builtin-shadow", "import * as vm from 'node:vm';const ctx:any={Promise:7};console.log(vm.runInNewContext('Promise===7 && globalThis.Promise===7;',ctx));", "true\n"];
        yield return ["lexical-private", "import * as vm from 'node:vm';const ctx:any={};console.log(vm.runInNewContext('const local=9;let other=8;globalThis.value=7;typeof local;',ctx),'local' in ctx,'other' in ctx,ctx.value);", "number false false 7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void UsesTheActualGlobalObjectWithoutPublishingLexicalShadows(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
