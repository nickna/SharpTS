using SharpTS.Tests.Infrastructure;
using Xunit;
namespace SharpTS.Tests.SharedTests;
public sealed class ArrayObjectPropertyKeyTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const value:any=[0];const log:string[]=[];const key:any={[Symbol.toPrimitive](hint:any){log.push(hint);return '0';}};function rhs(){log.push('rhs');return 7;}value[key]=rhs();console.log(value[0],log.join(','));", "7 rhs,string\n"];
        yield return ["simple", "const value:any=[1,2];const log:string[]=[];let n=0;const key:any={[Symbol.toPrimitive](hint:any){log.push(hint+(++n));return n===1?'0':'1'}};function rhs(){log.push('rhs');return 3}const result=(value[key]=rhs());console.log([result,value[0],value[1],log.join(',')].join('|'));", "3|3|2|rhs,string1\n"];
        yield return ["compound", "const value:any=[1,2];const log:string[]=[];let n=0;const key:any={[Symbol.toPrimitive](hint:any){log.push(hint+(++n));return n===1?'0':'1'}};function rhs(){log.push('rhs');return 3}const result=(value[key]+=rhs());console.log([result,value[0],value[1],log.join(',')].join('|'));", "4|1|4|string1,rhs,string2\n"];
        yield return ["logical-write", "const value:any=[1,2];const log:string[]=[];let n=0;const key:any={[Symbol.toPrimitive](hint:any){log.push(hint+(++n));return n===1?'0':'1'}};function rhs(){log.push('rhs');return 3}const result=(value[key]&&=rhs());console.log([result,value[0],value[1],log.join(',')].join('|'));", "3|1|3|string1,rhs,string2\n"];
        yield return ["logical-skip", "const value:any=[1,2];const log:string[]=[];let n=0;const key:any={[Symbol.toPrimitive](hint:any){log.push(hint+(++n));return n===1?'0':'1'}};function rhs(){log.push('rhs');return 3}const result=(value[key]||=rhs());console.log([result,value[0],value[1],log.join(',')].join('|'));", "1|1|2|string1\n"];
        yield return ["nullish-skip", "const value:any=[1,2];const log:string[]=[];let n=0;const key:any={[Symbol.toPrimitive](hint:any){log.push(hint+(++n));return n===1?'0':'1'}};function rhs(){log.push('rhs');return 3}const result=(value[key]??=rhs());console.log([result,value[0],value[1],log.join(',')].join('|'));", "1|1|2|string1\n"];
        yield return ["postfix", "const value:any=[1,2];const log:string[]=[];let n=0;const key:any={[Symbol.toPrimitive](hint:any){log.push(hint+(++n));return n===1?'0':'1'}};function rhs(){log.push('rhs');return 3}const result=(value[key]++);console.log([result,value[0],value[1],log.join(',')].join('|'));", "1|1|2|string1,string2\n"];
        yield return ["prefix", "const value:any=[1,2];const log:string[]=[];let n=0;const key:any={[Symbol.toPrimitive](hint:any){log.push(hint+(++n));return n===1?'0':'1'}};function rhs(){log.push('rhs');return 3}const result=(++value[key]);console.log([result,value[0],value[1],log.join(',')].join('|'));", "2|1|2|string1,string2\n"];
        yield return ["read", "const value:any=[1,2];const log:string[]=[];let n=0;const key:any={[Symbol.toPrimitive](hint:any){log.push(hint+(++n));return n===1?'0':'1'}};function rhs(){log.push('rhs');return 3}const result=(value[key]);console.log([result,value[0],value[1],log.join(',')].join('|'));", "1|1|2|string1\n"];
        yield return ["symbol", "const value:any=[];const log:string[]=[];const symbol=Symbol('key');const key:any={[Symbol.toPrimitive](hint:any){log.push(hint);return symbol}};value[key]=7;console.log(value[symbol],log.join(','));", "7 string\n"];
        yield return ["named", "const value:any=[];const key:any={[Symbol.toPrimitive](){return 'named'}};value[key]=7;console.log(value[key],value.named);", "7 7\n"];
        yield return ["fractional-result", "const value:any=[1,2];const key:any={[Symbol.toPrimitive](){return 1.5}};value[key]=7;console.log(value[key],value[1],value.length);", "7 2 2\n"];
        yield return ["fallback-order", "const value:any=[0];const log:string[]=[];const key:any={toString(){log.push('string');return {}},valueOf(){log.push('value');return '0'}};value[key]=7;console.log(value[0],log.join(','));", "7 string,value\n"];
        yield return ["abrupt-after-rhs", "const value:any=[1];const log:string[]=[];const marker={};const key:any={[Symbol.toPrimitive](hint:any){log.push(hint);throw marker}};function rhs(){log.push('rhs');return 7}try{value[key]=rhs()}catch(error){console.log(error===marker,value[0],log.join(','))}", "true 1 rhs,string\n"];
        yield return ["rhs-replaces-converter", "const value:any=[1,2];const log:string[]=[];const key:any={[Symbol.toPrimitive](hint:any){log.push('old');return '0'}};function rhs(){log.push('rhs');key[Symbol.toPrimitive]=function(hint:any){log.push(hint);return '1'};return 7}value[key]=rhs();console.log(value[0],value[1],log.join(','));", "1 7 rhs,string\n"];
        yield return ["own-accessor", "const value:any=[0];const log:string[]=[];let stored=1;Object.defineProperty(value,'0',{get(){log.push('get');return stored},set(v){log.push('set');stored=v},configurable:true});const key:any={[Symbol.toPrimitive](hint:any){log.push(hint);return '0'}};function rhs(){log.push('rhs');return 3}value[key]+=rhs();console.log(stored,log.join(','));", "4 string,get,rhs,string,set\n"];
        yield return ["strict-readonly", "'use strict';const value:any=[1];Object.defineProperty(value,'0',{writable:false});const key:any={[Symbol.toPrimitive](){return '0'}};try{value[key]=7}catch(error){console.log(error instanceof TypeError,value[0])}", "true 1\n"];
        yield return ["sloppy-readonly", "const value:any=[1];Object.defineProperty(value,'0',{writable:false});const key:any={[Symbol.toPrimitive](){return '0'}};const result=value[key]=7;console.log(result,value[0]);", "7 1\n"];
        yield return ["async-rhs", "const value:any=[0];const log:string[]=[];const key:any={[Symbol.toPrimitive](hint:any){log.push(hint);return '0'}};async function run(){value[key]=await Promise.resolve(7);console.log(value[0],log.join(','))}run();", "7 string\n"];
        yield return ["generator-rhs", "const value:any=[0];const log:string[]=[];const key:any={[Symbol.toPrimitive](hint:any){log.push(hint);return '0'}};function* run():Generator<number,void,number>{value[key]=yield 9}const iterator=run();console.log(iterator.next().value);iterator.next(7);console.log(value[0],log.join(','));", "9\n7 string\n"];
        yield return ["unchanged-vm-3262", "import * as vm from 'node:vm';const value:any=[0];const log:string[]=[];console.log(vm.runInNewContext(\"const key={[Symbol.toPrimitive](hint){note(hint);return '0';}};function rhs(){note('rhs');return 7;}value[key]=rhs();value[0]\",{value,note:(v:any)=>log.push(v)}),log.join(','));", "7 rhs,string\n"];
    }
    [Theory,MemberData(nameof(Cases))]
    public void InterpreterUsesGuestArrayKeys(string name,string source,string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected,TestHarness.RunModules(new Dictionary<string,string>{["main.ts"]=source},"main.ts",ExecutionMode.Interpreted));
    }
}
