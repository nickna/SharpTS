using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StrictDictionaryInheritedSetter3310Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["push-inherited-length-setter","const f:any=Array.prototype.push;let log='';let target:any;const proto:any={get length(){log+='L';return 1},set length(v:number){log+=(this===target)+'N'+v}};target={0:3};Object.setPrototypeOf(target,proto);console.log(f.call(target,7),log,target[0],target[1],Object.prototype.hasOwnProperty.call(target,'length'));","2 LtrueN2 3 7 false\n"];
        yield return ["shift-inherited-length-setter","const f:any=Array.prototype.shift;let log='';let target:any;const proto:any={get length(){log+='L';return 1},set length(v:number){log+=(this===target)+'N'+v}};target={0:3};Object.setPrototypeOf(target,proto);console.log(f.call(target),log,target[0],target[1],Object.prototype.hasOwnProperty.call(target,'length'));","3 LtrueN0 undefined undefined false\n"];
        yield return ["unshift-inherited-length-setter","const f:any=Array.prototype.unshift;let log='';let target:any;const proto:any={get length(){log+='L';return 1},set length(v:number){log+=(this===target)+'N'+v}};target={0:3};Object.setPrototypeOf(target,proto);console.log(f.call(target,7),log,target[0],target[1],Object.prototype.hasOwnProperty.call(target,'length'));","2 LtrueN2 7 3 false\n"];
        yield return ["original-strict-inherited-setter","let log='';const proto:any={set length(v:number){log+='N'+v}};const target:any={};Object.setPrototypeOf(target,proto);function write(){'use strict';target.length=2}write();console.log(log,Object.prototype.hasOwnProperty.call(target,'length'));","N2 false\n"];
        yield return ["nearest-getter-only-masks-setter","'use strict';let calls=0;const far:any={set value(v:number){calls++}},near:any={get value(){calls+=100;return 1}};Object.setPrototypeOf(near,far);const target:any={};Object.setPrototypeOf(target,near);try{target.value=7}catch(e){console.log(e instanceof TypeError,calls,Object.prototype.hasOwnProperty.call(target,'value'))}","true 0 false\n"];
        yield return ["inherited-readonly-data","'use strict';const proto:any={};Object.defineProperty(proto,'value',{value:1,writable:false});const target:any={};Object.setPrototypeOf(target,proto);try{target.value=7}catch(e){console.log(e instanceof TypeError,target.value,Object.prototype.hasOwnProperty.call(target,'value'))}","true 1 false\n"];
        yield return ["own-data-masks-inherited-setter","'use strict';let calls=0;const proto:any={set value(v:number){calls++}};const target:any={value:1};Object.setPrototypeOf(target,proto);target.value=7;console.log(target.value,calls,Object.prototype.hasOwnProperty.call(target,'value'));","7 0 true\n"];
        yield return ["own-getter-masks-inherited-setter","'use strict';let calls=0;const proto:any={set value(v:number){calls++}},target:any={};Object.defineProperty(target,'value',{get(){return 3}});Object.setPrototypeOf(target,proto);try{target.value=7}catch(e){console.log(e instanceof TypeError,calls,target.value)}","true 0 3\n"];
        yield return ["own-setter-masks-inherited-readonly","'use strict';let log='';const proto:any={};Object.defineProperty(proto,'value',{value:1,writable:false});const target:any={};Object.defineProperty(target,'value',{set(v:number){log+=(this===target)+':'+v}});Object.setPrototypeOf(target,proto);target.value=7;console.log(log);","true:7\n"];
        yield return ["nonextensible-inherited-setter","'use strict';let log='';const proto:any={set value(v:number){log+=(this===target)+':'+v}};const target:any={};Object.setPrototypeOf(target,proto);Object.preventExtensions(target);target.value=7;console.log(log,Object.prototype.hasOwnProperty.call(target,'value'));","true:7 false\n"];
        yield return ["throwing-setter-key-rhs-order","'use strict';let log='';const token:any={},proto:any={set value(v:number){log+='S'+v;throw token}},target:any={};Object.setPrototypeOf(target,proto);try{target[(log+='K','value')]=(log+='R',7)}catch(e){console.log(e===token,log,Object.prototype.hasOwnProperty.call(target,'value'))}","true KRS7 false\n"];
        yield return ["nearest-writable-data-masks-setter","'use strict';let calls=0;const far:any={set value(v:number){calls++}},near:any={value:1},target:any={};Object.setPrototypeOf(near,far);Object.setPrototypeOf(target,near);target.value=7;console.log(target.value,near.value,calls,Object.prototype.hasOwnProperty.call(target,'value'));","7 1 0 true\n"];
        yield return ["nonextensible-writable-prototype-data-refuses","'use strict';const proto:any={value:1},target:any={};Object.setPrototypeOf(target,proto);Object.preventExtensions(target);try{target.value=7}catch(e){console.log(e instanceof TypeError,target.value,Object.prototype.hasOwnProperty.call(target,'value'))}","true 1 false\n"];
        yield return ["proxy-prototype-setter-operation","'use strict';let log='';let target:any;const proto:any=new Proxy({},{set(_t:any,key:PropertyKey,value:any,receiver:any){log+=String(key)+':'+value+':'+(receiver===target);return true},getOwnPropertyDescriptor(){throw 1}});target={};Object.setPrototypeOf(target,proto);Object.preventExtensions(target);target.value=7;console.log(log,Object.prototype.hasOwnProperty.call(target,'value'));","value:7:true false\n"];
        yield return ["proxy-prototype-false-refusal","'use strict';let calls=0;const proto:any=new Proxy({},{set(){calls++;return false}}),target:any={};Object.setPrototypeOf(target,proto);try{target.value=7}catch(e){console.log(e instanceof TypeError,calls,Object.prototype.hasOwnProperty.call(target,'value'))}","true 1 false\n"];
        yield return ["strict-legacy-prototype-setter","'use strict';const proto:any={value:7},target:any={};target.__proto__=proto;console.log(Object.getPrototypeOf(target)===proto,target.value,Object.prototype.hasOwnProperty.call(target,'__proto__'));","true 7 false\n"];
        yield return ["inherited-undefined-setter-refuses","'use strict';const proto:any={},target:any={};Object.defineProperty(proto,'value',{set:undefined});Object.setPrototypeOf(target,proto);try{target.value=7}catch(e){console.log(e instanceof TypeError,Object.prototype.hasOwnProperty.call(target,'value'))}","true false\n"];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void WritesRespectTheNearestInheritedDescriptor(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
