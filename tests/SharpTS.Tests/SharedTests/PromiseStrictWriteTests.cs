using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseStrictWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["inherited-readonly", "const p:any=Promise.resolve(7),proto:any={};Object.defineProperty(proto,\"value\",{value:7,writable:false});Object.setPrototypeOf(p,proto);console.log(Reflect.set(p,\"value\",9),p.value);try{(()=>{\"use strict\";p.value=9;})();}catch(e){console.log(e instanceof TypeError);}console.log(Object.hasOwn(p,\"value\"));", "false 7\ntrue\nfalse\n"];
        yield return ["inherited-setter", "const p:any=Promise.resolve(1);let seen=0,correct=false;Object.setPrototypeOf(p,{set value(v:any){seen=v;correct=this===p}});(()=>{'use strict';p.value=9})();console.log(seen,correct,Object.hasOwn(p,'value'));", "9 true false\n"];
        yield return ["inherited-getter-only", "const p:any=Promise.resolve(1);let reads=0;Object.setPrototypeOf(p,{get value(){reads++;return 7}});try{(()=>{'use strict';p.value=9})();}catch(e){console.log(e instanceof TypeError)}console.log(reads,Object.hasOwn(p,'value'));", "true\n0 false\n"];
        yield return ["inherited-writable", "const p:any=Promise.resolve(1),proto:any={value:7};Object.setPrototypeOf(p,proto);(()=>{'use strict';p.value=9})();const d:any=Object.getOwnPropertyDescriptor(p,'value');console.log(p.value,proto.value,d.writable,d.enumerable,d.configurable);", "9 7 true true true\n"];
        yield return ["own-data-shadows-readonly-parent", "const p:any=Promise.resolve(1),proto:any={};Object.defineProperty(proto,'value',{value:7,writable:false});Object.defineProperty(p,'value',{value:8,writable:true});Object.setPrototypeOf(p,proto);(()=>{'use strict';p.value=9})();console.log(p.value,proto.value);", "9 7\n"];
        yield return ["own-readonly", "const p:any=Promise.resolve(1);Object.defineProperty(p,'value',{value:7,writable:false});try{(()=>{'use strict';p.value=9})();}catch(e){console.log(e instanceof TypeError)}console.log(p.value);", "true\n7\n"];
        yield return ["null-prototype", "const p:any=Promise.resolve(1);Object.setPrototypeOf(p,null);(()=>{'use strict';p.value=9})();console.log(p.value,Object.hasOwn(p,'value'));", "9 true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void StrictWritesHonorOwnAndInheritedDescriptors(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
