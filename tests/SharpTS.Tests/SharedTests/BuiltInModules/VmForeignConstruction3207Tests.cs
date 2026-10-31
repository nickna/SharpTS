using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmForeignConstruction3207Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["new-foreign-promise", "import * as vm from 'node:vm';const C:any=vm.runInNewContext('Promise');const p:any=new C((resolve:any)=>resolve(7));console.log(p.constructor===C);p.then((v:any)=>console.log(v));", "true\n7\n"];
        yield return ["reflect-foreign-promise", "import * as vm from 'node:vm';const C:any=vm.runInNewContext('Promise');const p:any=Reflect.construct(C,[(resolve:any)=>resolve(7)]);console.log(p.constructor===C);p.then((v:any)=>console.log(v));", "true\n7\n"];
        yield return ["bound-foreign-promise", "import * as vm from 'node:vm';const C:any=vm.runInNewContext('Promise');const B:any=C.bind(null);const p:any=new B((resolve:any)=>resolve(7));console.log(p.constructor===C);p.then((v:any)=>console.log(v));", "true\n7\n"];
        yield return ["ordinary-call-is-not-construction", "import * as vm from 'node:vm';const C:any=vm.runInNewContext('Promise');let calls=0;try{C(()=>calls++);}catch(error){console.log('threw',calls);}", "threw 0\n"];
        yield return ["foreign-arrow-is-not-constructor", "import * as vm from 'node:vm';const C:any=vm.runInNewContext('()=>7');try{new C();}catch(error){console.log(error instanceof TypeError);}", "true\n"];
        yield return ["foreign-promise-is-not-constructor", "import * as vm from 'node:vm';const p:any=vm.runInNewContext('Promise.resolve(7)');try{new p();}catch(error){console.log(error instanceof TypeError);}", "true\n"];
        yield return ["reflect-validates-newtarget-before-executor", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const C:any=vm.runInContext('Promise',ctx);const nt:any=vm.runInContext('()=>7',ctx);let calls=0;try{Reflect.construct(C,[()=>calls++],nt);}catch(error){console.log(error instanceof TypeError,calls);}", "true 0\n"];
        yield return ["actual-class-alternate-newtarget", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const C:any=vm.runInContext('(class Box{constructor(value){this.value=value;}})',ctx);const nt:any=vm.runInContext('(class Alternate{})',ctx);const p:any=Reflect.construct(C,[8],nt);console.log(p.value,Object.getPrototypeOf(p)===nt.prototype);", "8 true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesExplicitConstructionIntent(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
