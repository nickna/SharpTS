using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalSelfAlias3239Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-vm-override-and-delete", "import * as vm from 'node:vm';console.log(vm.runInNewContext('globalThis===7',{globalThis:7}));console.log(vm.runInNewContext('delete globalThis.globalThis;typeof globalThis'));", "true\nundefined\n"];
        yield return ["ordinary-delete", "const root:any=globalThis;console.log(root.globalThis===root);console.log(delete root.globalThis,typeof root.globalThis,typeof globalThis);", "true\ntrue undefined undefined\n"];
        yield return ["replacement-and-recreation", "const root:any=globalThis;root.globalThis=7;console.log(globalThis,root.globalThis);console.log(delete root.globalThis,typeof globalThis);root.globalThis=root;console.log(globalThis===root);", "7 7\ntrue undefined\ntrue\n"];
        yield return ["own-getter-once", "const root:any=globalThis;let reads=0;Object.defineProperty(root,'globalThis',{get(){reads++;return 7;},configurable:true});console.log(globalThis,reads);console.log(root.globalThis,reads);", "7 1\n7 2\n"];
        yield return ["deleted-reference-error", "const root:any=globalThis;delete root.globalThis;try{console.log(globalThis);}catch(error){console.log(error instanceof ReferenceError);}console.log(typeof globalThis,typeof root.globalThis);", "true\nundefined undefined\n"];
        yield return ["vm-escaped-delete", "import * as vm from 'node:vm';const read:any=vm.runInNewContext('()=>{const root=globalThis;delete root.globalThis;return typeof globalThis;}');console.log(read());", "undefined\n"];
        yield return ["undefined-own-binding", "const root:any=globalThis;root.globalThis=undefined;console.log(globalThis===undefined,root.globalThis===undefined);", "true true\n"];
        yield return ["deleted-namespace-shortcut", "const root:any=globalThis;delete root.globalThis;try{globalThis.Intl.NumberFormat('en-US');}catch(error){console.log(error instanceof ReferenceError);}", "true\n"];
        yield return ["replaced-namespace-shortcut", "const root:any=globalThis;root.globalThis={Intl:{NumberFormat:()=>7}};console.log(globalThis.Intl.NumberFormat('en-US') as any);", "7\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterUsesActualSelfPropertyState(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", ExecutionMode.Interpreted));
    }
}
