using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeParentLinkTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["number-inherited-setter", "'use strict';const p:any=Number.prototype,base:any={};let stored=1,receiver=false;Object.defineProperty(base,'probe',{get(){return stored},set(v:any){receiver=this===p;stored=v}});Object.setPrototypeOf(p,base);console.log(p.probe+=2,stored,receiver,Object.prototype.hasOwnProperty.call(p,'probe'));\n", "3 3 true false\n"];
        yield return ["boolean-inherited-setter", "'use strict';const p:any=Boolean.prototype,base:any={};let stored=1,receiver=false;Object.defineProperty(base,'probe',{get(){return stored},set(v:any){receiver=this===p;stored=v}});Object.setPrototypeOf(p,base);console.log(p.probe+=2,stored,receiver,Object.prototype.hasOwnProperty.call(p,'probe'));\n", "3 3 true false\n"];
        yield return ["number-parent-data", "const p:any=Number.prototype;const proto:any={probe:7};console.log(Reflect.setPrototypeOf(p,proto),Object.getPrototypeOf(p)===proto,p.probe,Reflect.get(p,'probe'));\n", "true true 7 7\n"];
        yield return ["number-null-and-restore", "const p:any=Number.prototype;Object.setPrototypeOf(p,null);console.log(Object.getPrototypeOf(p)===null,p.missing===undefined);console.log(Object.setPrototypeOf(p,Object.prototype)===p,Object.getPrototypeOf(p)===Object.prototype);\n", "true true\ntrue true\n"];
        yield return ["number-prevented-same-and-different", "const p:any=Number.prototype;const proto:any={};Object.setPrototypeOf(p,proto);Object.preventExtensions(p);console.log(Reflect.setPrototypeOf(p,proto),Reflect.setPrototypeOf(p,{}),Object.getPrototypeOf(p)===proto);\n", "true false true\n"];
        yield return ["number-prevented-implicit-same", "const p:any=Number.prototype;Object.preventExtensions(p);console.log(Reflect.setPrototypeOf(p,Object.prototype),Reflect.setPrototypeOf(p,null),Object.getPrototypeOf(p)===Object.prototype);\n", "true false true\n"];
        yield return ["number-ordinary-cycle", "const p:any=Number.prototype;const proto:any={};Object.setPrototypeOf(proto,p);console.log(Reflect.setPrototypeOf(p,p),Reflect.setPrototypeOf(p,proto),Object.getPrototypeOf(p)===Object.prototype);\n", "false false true\n"];
        yield return ["boolean-parent-data", "const p:any=Boolean.prototype;const proto:any={probe:7};console.log(Reflect.setPrototypeOf(p,proto),Object.getPrototypeOf(p)===proto,p.probe,Reflect.get(p,'probe'));\n", "true true 7 7\n"];
        yield return ["boolean-null-and-restore", "const p:any=Boolean.prototype;Object.setPrototypeOf(p,null);console.log(Object.getPrototypeOf(p)===null,p.missing===undefined);console.log(Object.setPrototypeOf(p,Object.prototype)===p,Object.getPrototypeOf(p)===Object.prototype);\n", "true true\ntrue true\n"];
        yield return ["boolean-prevented-same-and-different", "const p:any=Boolean.prototype;const proto:any={};Object.setPrototypeOf(p,proto);Object.preventExtensions(p);console.log(Reflect.setPrototypeOf(p,proto),Reflect.setPrototypeOf(p,{}),Object.getPrototypeOf(p)===proto);\n", "true false true\n"];
        yield return ["boolean-prevented-implicit-same", "const p:any=Boolean.prototype;Object.preventExtensions(p);console.log(Reflect.setPrototypeOf(p,Object.prototype),Reflect.setPrototypeOf(p,null),Object.getPrototypeOf(p)===Object.prototype);\n", "true false true\n"];
        yield return ["boolean-ordinary-cycle", "const p:any=Boolean.prototype;const proto:any={};Object.setPrototypeOf(proto,p);console.log(Reflect.setPrototypeOf(p,p),Reflect.setPrototypeOf(p,proto),Object.getPrototypeOf(p)===Object.prototype);\n", "false false true\n"];
        yield return ["cross-carrier-cycle", "const a:any=Number.prototype,b:any=Boolean.prototype;console.log(Reflect.setPrototypeOf(a,b),Reflect.setPrototypeOf(b,a),Object.getPrototypeOf(a)===b,Object.getPrototypeOf(b)===Object.prototype);\n", "true false true true\n"];
        yield return ["proxy-parent-no-trap", "const p:any=Number.prototype;let calls=0;const proto:any=new Proxy({},{getPrototypeOf(){calls++;return p}});console.log(Reflect.setPrototypeOf(p,proto),Object.getPrototypeOf(p)===proto,calls);\n", "true true 0\n"];
        yield return ["number-ordinary-inherited-string-write", "const p:any=Number.prototype,proto:any={};let stored=1,receiver=false;Object.defineProperty(proto,'probe',{get(){return stored},set(v:any){receiver=this===p;stored=v}});Object.setPrototypeOf(p,proto);p.probe=4;p['probe']=7;console.log(p.probe,stored,receiver,Object.prototype.hasOwnProperty.call(p,'probe'));\n", "7 7 true false\n"];
        yield return ["number-inherited-symbol-receiver", "'use strict';const p:any=Number.prototype,proto:any={},key=Symbol('probe');let stored=1,gets=0,sets=0,receiver=true;Object.defineProperty(proto,key,{get(){gets++;receiver=receiver&&this===p;return stored},set(v:any){sets++;receiver=receiver&&this===p;stored=v},configurable:true});Object.setPrototypeOf(p,proto);const first=Reflect.get(p,key,p);p[key]=3;const second=p[key]+=2;const third=p[key]++;console.log(first,second,third,stored,gets,sets,receiver,Reflect.ownKeys(p).includes(key));\n", "1 5 5 6 3 3 true false\n"];
        yield return ["number-own-symbol-mask-and-frozen", "'use strict';const p:any=Number.prototype,proto:any={},key=Symbol('own');let parentCalls=0;p[key]=9;Object.defineProperty(proto,key,{get(){parentCalls++;return 1},set(v:any){parentCalls++},configurable:true});Object.setPrototypeOf(p,proto);p[key]+=2;console.log(p[key],parentCalls,Reflect.ownKeys(p).includes(key));Object.freeze(p);let caught=false;try{p[key]+=1}catch(e){caught=e instanceof TypeError}console.log(caught,p[key],parentCalls);\n", "11 0 true\ntrue 11 0\n"];
        yield return ["boolean-ordinary-inherited-string-write", "const p:any=Boolean.prototype,proto:any={};let stored=1,receiver=false;Object.defineProperty(proto,'probe',{get(){return stored},set(v:any){receiver=this===p;stored=v}});Object.setPrototypeOf(p,proto);p.probe=4;p['probe']=7;console.log(p.probe,stored,receiver,Object.prototype.hasOwnProperty.call(p,'probe'));\n", "7 7 true false\n"];
        yield return ["boolean-inherited-symbol-receiver", "'use strict';const p:any=Boolean.prototype,proto:any={},key=Symbol('probe');let stored=1,gets=0,sets=0,receiver=true;Object.defineProperty(proto,key,{get(){gets++;receiver=receiver&&this===p;return stored},set(v:any){sets++;receiver=receiver&&this===p;stored=v},configurable:true});Object.setPrototypeOf(p,proto);const first=Reflect.get(p,key,p);p[key]=3;const second=p[key]+=2;const third=p[key]++;console.log(first,second,third,stored,gets,sets,receiver,Reflect.ownKeys(p).includes(key));\n", "1 5 5 6 3 3 true false\n"];
        yield return ["boolean-own-symbol-mask-and-frozen", "'use strict';const p:any=Boolean.prototype,proto:any={},key=Symbol('own');let parentCalls=0;p[key]=9;Object.defineProperty(proto,key,{get(){parentCalls++;return 1},set(v:any){parentCalls++},configurable:true});Object.setPrototypeOf(p,proto);p[key]+=2;console.log(p[key],parentCalls,Reflect.ownKeys(p).includes(key));Object.freeze(p);let caught=false;try{p[key]+=1}catch(e){caught=e instanceof TypeError}console.log(caught,p[key],parentCalls);\n", "11 0 true\ntrue 11 0\n"];
        yield return ["number-inherited-symbol-presence-no-getter", "const p:any=Number.prototype,proto:any={},key=Symbol('presence');let gets=0;Object.defineProperty(proto,key,{get(){gets++;return undefined},configurable:true});Object.setPrototypeOf(p,proto);console.log(key in p,Reflect.has(p,key),gets);\n", "true true 0\n"];
        yield return ["boolean-inherited-symbol-presence-no-getter", "const p:any=Boolean.prototype,proto:any={},key=Symbol('presence');let gets=0;Object.defineProperty(proto,key,{get(){gets++;return undefined},configurable:true});Object.setPrototypeOf(p,proto);console.log(key in p,Reflect.has(p,key),gets);\n", "true true 0\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ParentLinksPreserveOwnedSlotsAndReceivers(string name, string source, string expected, ExecutionMode mode)
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
