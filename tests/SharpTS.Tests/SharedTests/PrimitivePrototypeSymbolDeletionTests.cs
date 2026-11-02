using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeSymbolDeletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["number-reflect-configurable", "const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});console.log(Reflect.deleteProperty(p,s),p[s]===undefined,Object.getOwnPropertyDescriptor(p,s)===undefined);\n", "true true true\n"];
        yield return ["number-sloppy-configurable", "const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});console.log(delete p[s],p[s]===undefined,Object.getOwnPropertyDescriptor(p,s)===undefined);\n", "true true true\n"];
        yield return ["number-sloppy-nonconfigurable", "const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:false});console.log(delete p[s],p[s],Object.getOwnPropertyDescriptor(p,s)!.configurable);\n", "false 7 false\n"];
        yield return ["number-strict-nonconfigurable", "'use strict';const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:false});let outcome='none';try{delete p[s]}catch(e){outcome=e instanceof TypeError?'TypeError':'other'}console.log(outcome,p[s]);\n", "TypeError 7\n"];
        yield return ["number-reflect-nonconfigurable", "'use strict';const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:false});console.log(Reflect.deleteProperty(p,s),p[s]);\n", "false 7\n"];
        yield return ["boolean-reflect-configurable", "const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});console.log(Reflect.deleteProperty(p,s),p[s]===undefined,Object.getOwnPropertyDescriptor(p,s)===undefined);\n", "true true true\n"];
        yield return ["boolean-sloppy-configurable", "const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});console.log(delete p[s],p[s]===undefined,Object.getOwnPropertyDescriptor(p,s)===undefined);\n", "true true true\n"];
        yield return ["boolean-sloppy-nonconfigurable", "const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:false});console.log(delete p[s],p[s],Object.getOwnPropertyDescriptor(p,s)!.configurable);\n", "false 7 false\n"];
        yield return ["boolean-strict-nonconfigurable", "'use strict';const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:false});let outcome='none';try{delete p[s]}catch(e){outcome=e instanceof TypeError?'TypeError':'other'}console.log(outcome,p[s]);\n", "TypeError 7\n"];
        yield return ["boolean-reflect-nonconfigurable", "'use strict';const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:false});console.log(Reflect.deleteProperty(p,s),p[s]);\n", "false 7\n"];
        yield return ["number-assigned-symbol", "const p:any=Number.prototype,s=Symbol('probe');p[s]=7;console.log(delete p[s],p[s]===undefined,Object.getOwnPropertySymbols(p).includes(s));\n", "true true false\n"];
        yield return ["number-missing-strict", "'use strict';const p:any=Number.prototype,s=Symbol('probe');console.log(delete p[s],Reflect.deleteProperty(p,s));\n", "true true\n"];
        yield return ["number-accessor-no-read", "const p:any=Number.prototype,s=Symbol('probe');let gets=0;Object.defineProperty(p,s,{get(){gets++;return 7},configurable:true});console.log(Reflect.deleteProperty(p,s),gets,Object.getOwnPropertyDescriptor(p,s)===undefined);\n", "true 0 true\n"];
        yield return ["number-sealed", "const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});Object.seal(p);console.log(Reflect.deleteProperty(p,s),p[s],Object.getOwnPropertyDescriptor(p,s)!.configurable);\n", "false 7 false\n"];
        yield return ["number-frozen-strict", "'use strict';const p:any=Number.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});Object.freeze(p);let caught=false;try{delete p[s]}catch(e){caught=e instanceof TypeError}console.log(Reflect.deleteProperty(p,s),caught,p[s]);\n", "false true 7\n"];
        yield return ["number-identity-recreation", "const p:any=Number.prototype,s=Symbol('probe');const other=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});Object.defineProperty(p,other,{value:9,configurable:true});const deleted=Reflect.deleteProperty(p,s);Object.defineProperty(p,s,{value:8,configurable:true});const keys=Object.getOwnPropertySymbols(p);console.log(deleted,p[s],p[other],keys[0]===other,keys[1]===s);\n", "true 8 9 true true\n"];
        yield return ["number-inherited-own-miss", "const p:any=Number.prototype,s=Symbol('probe');const proto:any={[s]:7};Object.setPrototypeOf(p,proto);console.log(Reflect.deleteProperty(p,s),p[s],proto[s]);\n", "true 7 7\n"];
        yield return ["boolean-assigned-symbol", "const p:any=Boolean.prototype,s=Symbol('probe');p[s]=7;console.log(delete p[s],p[s]===undefined,Object.getOwnPropertySymbols(p).includes(s));\n", "true true false\n"];
        yield return ["boolean-missing-strict", "'use strict';const p:any=Boolean.prototype,s=Symbol('probe');console.log(delete p[s],Reflect.deleteProperty(p,s));\n", "true true\n"];
        yield return ["boolean-accessor-no-read", "const p:any=Boolean.prototype,s=Symbol('probe');let gets=0;Object.defineProperty(p,s,{get(){gets++;return 7},configurable:true});console.log(Reflect.deleteProperty(p,s),gets,Object.getOwnPropertyDescriptor(p,s)===undefined);\n", "true 0 true\n"];
        yield return ["boolean-sealed", "const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});Object.seal(p);console.log(Reflect.deleteProperty(p,s),p[s],Object.getOwnPropertyDescriptor(p,s)!.configurable);\n", "false 7 false\n"];
        yield return ["boolean-frozen-strict", "'use strict';const p:any=Boolean.prototype,s=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});Object.freeze(p);let caught=false;try{delete p[s]}catch(e){caught=e instanceof TypeError}console.log(Reflect.deleteProperty(p,s),caught,p[s]);\n", "false true 7\n"];
        yield return ["boolean-identity-recreation", "const p:any=Boolean.prototype,s=Symbol('probe');const other=Symbol('probe');Object.defineProperty(p,s,{value:7,configurable:true});Object.defineProperty(p,other,{value:9,configurable:true});const deleted=Reflect.deleteProperty(p,s);Object.defineProperty(p,s,{value:8,configurable:true});const keys=Object.getOwnPropertySymbols(p);console.log(deleted,p[s],p[other],keys[0]===other,keys[1]===s);\n", "true 8 9 true true\n"];
        yield return ["boolean-inherited-own-miss", "const p:any=Boolean.prototype,s=Symbol('probe');const proto:any={[s]:7};Object.setPrototypeOf(p,proto);console.log(Reflect.deleteProperty(p,s),p[s],proto[s]);\n", "true 7 7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OwnedSymbolDeletionPreservesConfigurabilityAndStrictness(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
