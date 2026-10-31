using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class LegacyPrototypeAccessorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const root:any={},leaf:any={};root.__proto__=leaf;console.log(Object.getPrototypeOf(root)===leaf,Object.hasOwn(root,'__proto__'));", "true false\n"];
        yield return ["proxy-cycle", "const root:any={};const intermediary=new Proxy(Object.create(root),{});const leaf=Object.create(intermediary);root.__proto__=leaf;console.log(Object.getPrototypeOf(root)===leaf,Object.hasOwn(root,'__proto__'));", "true false\n"];
        yield return ["computed-own-data", "const leaf={},value:any={['__proto__']:7};console.log(value.__proto__,Object.getPrototypeOf(value)===Object.prototype);value.__proto__=leaf;console.log(value.__proto__===leaf,Object.getPrototypeOf(value)===Object.prototype,Object.hasOwn(value,'__proto__'));", "7 true\ntrue true true\n"];
        yield return ["null-prototype", "const value:any=Object.create(null),leaf={};console.log(value.__proto__===undefined);value.__proto__=leaf;console.log(value.__proto__===leaf,Object.getPrototypeOf(value)===null,Object.hasOwn(value,'__proto__'));", "true\ntrue true true\n"];
        yield return ["inherited-data", "const prototype:any={};Object.defineProperty(prototype,'__proto__',{value:9,writable:true,configurable:true});const value:any=Object.create(prototype),leaf={};console.log(value.__proto__,Object.getPrototypeOf(value)===prototype);value.__proto__=leaf;console.log(value.__proto__===leaf,Object.getPrototypeOf(value)===prototype,Object.hasOwn(value,'__proto__'));", "9 true\ntrue true true\n"];
        yield return ["custom-accessor", "let seen:any,receiver:any;const prototype:any={};Object.defineProperty(prototype,'__proto__',{get(){return 11;},set(value:any){seen=value;receiver=this;},configurable:true});const target:any=Object.create(prototype),leaf={};target.__proto__=leaf;console.log(seen===leaf,receiver===target,target.__proto__,Object.getPrototypeOf(target)===prototype,Object.hasOwn(target,'__proto__'));", "true true 11 true false\n"];
        yield return ["metadata", "const descriptor:any=Object.getOwnPropertyDescriptor(Object.prototype,'__proto__');console.log(typeof descriptor.get,typeof descriptor.set,descriptor.enumerable,descriptor.configurable,'value' in descriptor,'writable' in descriptor);console.log(descriptor.get.name,descriptor.get.length,descriptor.set.name,descriptor.set.length,descriptor.get===Object.getOwnPropertyDescriptor(Object.prototype,'__proto__')!.get);console.log(Object.getOwnPropertyNames(Object.prototype).includes('__proto__'));", "function function false true false false\nget __proto__ 0 set __proto__ 1 true\ntrue\n"];
        yield return ["invalid-values", "const value:any={},original=Object.getPrototypeOf(value);for(const prototype of [undefined,7,'x',true,1n,Symbol()])value.__proto__=prototype;console.log(Object.getPrototypeOf(value)===original,Object.hasOwn(value,'__proto__'));const set:any=Object.getOwnPropertyDescriptor(Object.prototype,'__proto__')!.set;console.log(set.call(value,undefined)===undefined,set.call(3,{})===undefined);for(const receiver of [null,undefined]){try{set.call(receiver,7);console.log('bad');}catch(error){console.log(error instanceof TypeError);}}", "true false\ntrue true\ntrue\ntrue\n"];
        yield return ["nonextensible-and-cycle", "const value:any={},original=Object.getPrototypeOf(value);Object.preventExtensions(value);value.__proto__=original;try{value.__proto__={};console.log('bad');}catch(error){console.log(error instanceof TypeError,Object.getPrototypeOf(value)===original);}const cycle:any={};try{cycle.__proto__=cycle;console.log('bad');}catch(error){console.log(error instanceof TypeError,Object.getPrototypeOf(cycle)===Object.prototype,Object.hasOwn(cycle,'__proto__'));}", "true true\ntrue true false\n"];
        yield return ["delete-and-restore", "const descriptor:any=Object.getOwnPropertyDescriptor(Object.prototype,'__proto__');delete (Object.prototype as any).__proto__;const value:any={},leaf={};console.log(value.__proto__===undefined);value.__proto__=leaf;console.log(Object.getPrototypeOf(value)===Object.prototype,Object.hasOwn(value,'__proto__'),value.__proto__===leaf);Object.defineProperty(Object.prototype,'__proto__',descriptor);const other:any={};other.__proto__=leaf;console.log(Object.getPrototypeOf(other)===leaf,Object.hasOwn(other,'__proto__'));", "true\ntrue true true\ntrue false\n"];
        yield return ["reflect-receiver", "const receiver:any={},leaf={};console.log(Reflect.set(Object.prototype,'__proto__',leaf,receiver),Object.getPrototypeOf(receiver)===leaf,Object.hasOwn(receiver,'__proto__'));", "true true false\n"];
        yield return ["array-and-function", "const array:any=[1],fn:any=function(){},leaf={};array.__proto__=leaf;fn.__proto__=leaf;console.log(Object.getPrototypeOf(array)===leaf,Object.getPrototypeOf(fn)===leaf,Object.hasOwn(array,'__proto__'),Object.hasOwn(fn,'__proto__'),array.length);", "true true false false 1\n"];
        yield return ["literal-prototype", "const leaf={answer:42};const value:any={__proto__:leaf};console.log(Object.getPrototypeOf(value)===leaf,Object.hasOwn(value,'__proto__'),value.answer,value.__proto__===leaf);const nil:any={__proto__:null};console.log(Object.getPrototypeOf(nil)===null,nil.__proto__===undefined);", "true false 42 true\ntrue true\n"];
        yield return ["literal-shorthand-method-spread", "const __proto__=7;const short:any={__proto__},method:any={__proto__(){return 9;}},spread:any={...{['__proto__']:11}};console.log(short.__proto__,method.__proto__(),spread.__proto__,Object.hasOwn(short,'__proto__'),Object.hasOwn(method,'__proto__'),Object.hasOwn(spread,'__proto__'));console.log(Object.getPrototypeOf(short)===Object.prototype,Object.getPrototypeOf(method)===Object.prototype,Object.getPrototypeOf(spread)===Object.prototype);", "7 9 11 true true true\ntrue true true\n"];
        yield return ["literal-side-effects", "let count=0;const leaf={answer:5},key:string='__proto__';const value:any={first:++count,__proto__:(++count,leaf),[key]:++count,last:++count};console.log(count,value.first,value.__proto__,value.last,value.answer,Object.getPrototypeOf(value)===leaf,Object.hasOwn(value,'__proto__'));", "4 1 3 4 5 true true\n"];
        yield return ["literal-await", "async function run(){const leaf={answer:8};const value:any={__proto__:await Promise.resolve(leaf)};console.log(Object.getPrototypeOf(value)===leaf,value.answer,Object.hasOwn(value,'__proto__'));}run();", "true 8 false\n"];
        yield return ["computed-key-order", "const log:string[]=[],key:any={[Symbol.toPrimitive](){log.push('key');return '__proto__';}};const value:any={[key]:(log.push('value'),7)};console.log(log.join(','),value.__proto__,Object.getPrototypeOf(value)===Object.prototype,Object.hasOwn(value,'__proto__'));", "key,value 7 true true\n"];
        yield return ["borrowed-getter", "const get:any=Object.getOwnPropertyDescriptor(Object.prototype,'__proto__')!.get;console.log(get.call(3)===Number.prototype,get.call('x')===String.prototype,get.call(true)===Boolean.prototype);for(const receiver of [null,undefined]){try{get.call(receiver);console.log('bad');}catch(error){console.log(error instanceof TypeError);}}", "true true true\ntrue\ntrue\n"];
        yield return ["computed-data-then-spread-descriptor", "const key:string='x';const value:any={[key]:1,...{x:2}};console.log(value.x,Object.getOwnPropertyDescriptor(value,'x')!.value);", "2 2\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void LegacyAccessorUsesActualPrototypeSlots(string name, string source, string expected, ExecutionMode mode)
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
}
