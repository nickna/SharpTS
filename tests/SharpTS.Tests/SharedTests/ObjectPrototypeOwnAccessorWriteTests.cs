using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectPrototypeOwnAccessorWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["immutable-dot", "const p:any=Object.prototype;p.__proto__=null;let ok=false;try{p.__proto__={}}catch(e){ok=e instanceof TypeError}console.log(ok,Object.getPrototypeOf(p)===null,Object.isExtensible(p));", "true true true\n"];
        yield return ["immutable-bracket", "const p:any=Object.prototype;p['__proto__']=null;let ok=false;try{p['__proto__']={}}catch(e){ok=e instanceof TypeError}console.log(ok,Object.getPrototypeOf(p)===null);", "true true\n"];
        yield return ["setter-receiver-result", "const p:any=Object.prototype;const log:string[]=[];Object.defineProperty(p,'custom',{set(v:any){log.push(String(this===p)+':'+v);return 99},configurable:true});console.log(p.custom=7,p['custom']=8,log.join(','));delete p.custom;", "7 8 true:7,true:8\n"];
        yield return ["setter-abrupt", "const p:any=Object.prototype,marker={};let calls=0;Object.defineProperty(p,'custom',{set(v:any){calls++;throw marker},configurable:true});try{p.custom=7}catch(e){console.log(e===marker,calls)}try{p['custom']=8}catch(e){console.log(e===marker,calls)}delete p.custom;", "true 1\ntrue 2\n"];
        yield return ["getter-only-sloppy", "const p:any=Object.prototype;let gets=0;Object.defineProperty(p,'custom',{get(){gets++;return 9},configurable:true});console.log(p.custom=7,p['custom']=8,p.custom,gets);delete p.custom;", "7 8 9 1\n"];
        yield return ["getter-only-strict", "const p:any=Object.prototype;Object.defineProperty(p,'custom',{get(){return 9},configurable:true});function put(){'use strict';p.custom=7}try{put()}catch(e){console.log(e instanceof TypeError,p.custom)}delete p.custom;", "true 9\n"];
        yield return ["readonly-sloppy", "const p:any=Object.prototype;Object.defineProperty(p,'custom',{value:1,writable:false,enumerable:false,configurable:true});console.log(p.custom=7,p['custom']=8,p.custom);const d:any=Object.getOwnPropertyDescriptor(p,'custom');console.log(d.writable,d.enumerable,d.configurable);delete p.custom;", "7 8 1\nfalse false true\n"];
        yield return ["readonly-strict", "const p:any=Object.prototype;Object.defineProperty(p,'custom',{value:1,writable:false,configurable:true});function put(){'use strict';p['custom']=7}try{put()}catch(e){console.log(e instanceof TypeError,p.custom)}delete p.custom;", "true 1\n"];
        yield return ["writable-flags", "const p:any=Object.prototype;Object.defineProperty(p,'custom',{value:1,writable:true,enumerable:false,configurable:true});p.custom=7;p['custom']=8;const d:any=Object.getOwnPropertyDescriptor(p,'custom');console.log(d.value,d.writable,d.enumerable,d.configurable);delete p.custom;", "8 true false true\n"];
        yield return ["deleted-legacy-data", "const p:any=Object.prototype;console.log(delete p.__proto__);p.__proto__=7;const d:any=Object.getOwnPropertyDescriptor(p,'__proto__');console.log(p.__proto__,d.value,d.writable,d.enumerable,d.configurable,Object.getPrototypeOf(p)===null);", "true\n7 7 true true true true\n"];
        yield return ["redefined-legacy-setter", "const p:any=Object.prototype;let result=0;Object.defineProperty(p,'__proto__',{set(v:any){result=this===p?v:99},configurable:true});p.__proto__=7;console.log(result,Object.getPrototypeOf(p)===null);", "7 true\n"];
        yield return ["postfix-prefix-dot", "const p:any=Object.prototype;let value=1;const log:string[]=[];Object.defineProperty(p,'custom',{get(){log.push('g');return value},set(v:any){log.push('s'+v);value=v},configurable:true});console.log(p.custom++,++p.custom,value,log.join(','));delete p.custom;", "1 3 3 g,s2,g,s3\n"];
        yield return ["postfix-prefix-index", "const p:any=Object.prototype;let value=1;const log:string[]=[];Object.defineProperty(p,'custom',{get(){log.push('g');return value},set(v:any){log.push('s'+v);value=v},configurable:true});console.log(p['custom']++,++p['custom'],value,log.join(','));delete p.custom;", "1 3 3 g,s2,g,s3\n"];
        yield return ["compound", "const p:any=Object.prototype;let value=1;const log:string[]=[];Object.defineProperty(p,'custom',{get(){log.push('g');return value},set(v:any){log.push('s'+v);value=v},configurable:true});console.log(p.custom+=4,p['custom']*=2,value,log.join(','));delete p.custom;", "5 10 10 g,s5,g,s10\n"];
        yield return ["logical", "const p:any=Object.prototype;let value:any=1;const log:string[]=[];Object.defineProperty(p,'custom',{get(){log.push('g');return value},set(v:any){log.push('s'+v);value=v},configurable:true});p.custom||=99;p['custom']&&=7;value=0;p.custom??=8;value=undefined;p['custom']??=9;console.log(value,log.join(','));delete p.custom;", "9 g,g,s7,g,g,s9\n"];
        yield return ["simple-key-order", "const p:any=Object.prototype;const log:string[]=[];Object.defineProperty(p,'custom',{set(v:any){log.push('set'+v)},configurable:true});const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return 'custom'}};function target(){log.push('target');return p}function rhs(){log.push('rhs');return 7}target()[key]=rhs();console.log(log.join(','));delete p.custom;", "target,rhs,string,set7\n"];
        yield return ["numeric-setter", "const p:any=Object.prototype;let value=0;Object.defineProperty(p,'7',{get(){return value},set(v:any){value=this===p?v:99},configurable:true});p[7]=8;console.log(p[7]++,value);delete p[7];", "8 9\n"];
        yield return ["descriptor-live-after-rhs", "const p:any=Object.prototype;let calls=0;Object.defineProperty(p,'custom',{set(v:any){calls++},configurable:true});function rhs(){delete p.custom;return 7}p.custom=rhs();const d:any=Object.getOwnPropertyDescriptor(p,'custom');console.log(p.custom,calls,d.value,d.writable,d.enumerable,d.configurable);delete p.custom;", "7 0 7 true true true\n"];
        yield return ["reflect-receiver", "const p:any=Object.prototype,receiver:any={};let seen:any;Object.defineProperty(p,'custom',{set(v:any){seen=this;this.value=v},configurable:true});console.log(Reflect.set(p,'custom',7,receiver),seen===receiver,receiver.value);delete p.custom;", "true true 7\n"];
        yield return ["public-prototype-controls", "const p:any=Object.prototype;console.log(Object.setPrototypeOf(p,null)===p,Reflect.setPrototypeOf(p,null),Reflect.setPrototypeOf(p,{}));let ok=false;try{Object.setPrototypeOf(p,{})}catch(e){ok=e instanceof TypeError}console.log(ok,Object.getPrototypeOf(p)===null);", "true true false\ntrue true\n"];
        yield return ["retained-setter", "const p:any=Object.prototype;const set:any=Object.getOwnPropertyDescriptor(p,'__proto__')!.set;console.log(set.call(p,null)===undefined);try{set.call(p,{})}catch(e){console.log(e instanceof TypeError,Object.getPrototypeOf(p)===null)}", "true\ntrue true\n"];
        yield return ["ordinary-chain-controls", "const ancestor:any={value:7},o:any={};o.__proto__=ancestor;console.log(o.value,Object.getPrototypeOf(o)===ancestor);o.__proto__=null;console.log(Object.getPrototypeOf(o)===null);const a:any={},b:any={};Object.setPrototypeOf(a,b);try{Object.setPrototypeOf(b,a)}catch(e){console.log(e instanceof TypeError)}", "7 true\ntrue\ntrue\n"];
        yield return ["virtual-constructor-replacement", "const p:any=Object.prototype,original:any=Object.getOwnPropertyDescriptor(p,'constructor'),first={},second={};try{p.constructor=first;let d:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(d.value===first,d.writable,d.enumerable,d.configurable);p['constructor']=second;d=Object.getOwnPropertyDescriptor(p,'constructor');console.log(d.value===second,d.writable,d.enumerable,d.configurable)}finally{Object.defineProperty(p,'constructor',original)}", "true true false true\ntrue true false true\n"];
        yield return ["virtual-method-replacement", "const p:any=Object.prototype,original:any=Object.getOwnPropertyDescriptor(p,'toString'),first=()=>7,second=()=>8;try{p.toString=first;let d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(p.toString===first,d.value===first,d.writable,d.enumerable,d.configurable);p['toString']=second;d=Object.getOwnPropertyDescriptor(p,'toString');console.log(p.toString===second,d.value===second,d.writable,d.enumerable,d.configurable)}finally{Object.defineProperty(p,'toString',original)}", "true true true false true\ntrue true true false true\n"];
        yield return ["deleted-virtual-method-recreation", "const p:any=Object.prototype,original:any=Object.getOwnPropertyDescriptor(p,'toString'),replacement=()=>7;try{console.log(delete p.toString);p.toString=replacement;const d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(d.value===replacement,d.writable,d.enumerable,d.configurable)}finally{Object.defineProperty(p,'toString',original)}", "true\ntrue true true true\n"];
        yield return ["nonextensible-existing-virtual-data", "const p:any=Object.prototype,constructorValue={},methodValue=()=>7;Object.preventExtensions(p);p.constructor=constructorValue;p['toString']=methodValue;const c:any=Object.getOwnPropertyDescriptor(p,'constructor'),m:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(p.constructor===constructorValue,c.writable,c.enumerable,c.configurable);console.log(p.toString===methodValue,m.writable,m.enumerable,m.configurable,Object.isExtensible(p));", "true true false true\ntrue true false true false\n"];
        yield return ["reflect-set-virtual-data", "const p:any=Object.prototype,original:any=Object.getOwnPropertyDescriptor(p,'constructor'),replacement={};try{console.log(Reflect.set(p,'constructor',replacement));const d:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(d.value===replacement,d.writable,d.enumerable,d.configurable)}finally{Object.defineProperty(p,'constructor',original)}", "true\ntrue true false true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void WritesUseActualIntrinsicOwnDescriptors(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    // The identical compiled phase-order regression is independently tracked by #3318.
    // Keep the interpreter GetValue/PutValue expectation intact while that prerequisite is repaired.
    [Fact]
    public void IndexedCompoundWriteConvertsRetainedKeyAtBothReadAndWrite()
    {
        const string source = "const p:any=Object.prototype;let value=1;const log:string[]=[];Object.defineProperty(p,'custom',{get(){log.push('get');return value},set(v:any){log.push('set'+v);value=v},configurable:true});const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return 'custom'}};function target(){log.push('target');return p}function rhs(){log.push('rhs');return 7}target()[key]+=rhs();console.log(value,log.join(','));delete p.custom;";
        const string expected = "8 target,string,get,rhs,string,set8\n";
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // The exact interpreter companion is independently tracked by #3322.
    [Fact]
    public void CompiledControlCanRedefineExistingRootDataAfterPreventExtensions()
    {
        const string source = "const p:any=Object.prototype;let calls=0;const get=function(this:any){calls++;return this===p?7:99};Object.preventExtensions(p);console.log(Reflect.defineProperty(p,'toString',{get}));const d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(d.get===get,d.enumerable,d.configurable,calls,p.toString,calls);";
        const string expected = "true\ntrue false true 0 7 1\n";
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}