using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class HistoricalRuntimeRegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue1958-original.ts", "\"use strict\";class Base<T>{static count=4;}class Derived<T> extends Base<T>{}const derived:any=Derived;Object.defineProperty(Base,'count',{value:4,writable:false});try{derived['count']=10;}catch(e){console.log(e instanceof TypeError);}console.log((Base as any).count,derived.count,Object.hasOwn(Derived,'count'));", "true\n4 4 false\n"];
        yield return ["issue1958-controls.ts", "class Base{static count=4;static writable=5;}class Derived extends Base{}const b:any=Base;const d:any=Derived;Object.defineProperty(Base,'count',{value:4,writable:false});d.count=10;d['count']=11;d.writable=12;console.log(b.count,d.count,Object.hasOwn(Derived,'count'),b.writable,d.writable);function strictWrite(){'use strict';try{d.count=9;}catch(e){console.log(e instanceof TypeError);}}strictWrite();console.log(b.count,d.count);", "4 4 false 5 12\ntrue\n4 4\n"];
        yield return ["issue1954-original.ts", "const value:any='ab';console.log(Object.prototype.propertyIsEnumerable.call(value,'length'),Object.keys(value).join(','));", "false 0,1\n"];
        yield return ["issue1954-controls.ts", "const pie:any=Object.prototype.propertyIsEnumerable;for(const value of ['', 'ab', new String('ab')]){console.log(pie.call(value,'length'),pie.call(value,'missing'),Object.keys(value).join(','));}console.log(pie.call(new String('ab'),'0'));", "false false \nfalse false 0,1\nfalse false 0,1\ntrue\n"];
        yield return ["issue1939-original.ts", "async function run(){const n=await Promise.resolve(7n);console.log(BigInt.asIntN(3,n),n+2n);}run().catch((e:any)=>console.log(e.name,e.message));", "-1n 9n\n"];
        yield return ["issue1939-call-control.ts", "async function run(){const n=await Promise.resolve(7);console.log(BigInt(n));}run().catch((e:any)=>console.log(e.name,e.message));", "7n\n"];
        yield return ["issue1939-controls.ts", "async function run(){console.log(BigInt('8'));await Promise.resolve(1);const B:any=BigInt;console.log(B===BigInt,B('9'),B.asIntN(3,7n),B.asUintN(3,-1n));}run().catch((e:any)=>console.log(e.name,e.message));", "8n\ntrue 9n -1n 7n\n"];
        yield return ["issue1940-original.ts", "function* values():Generator<bigint,void,any>{yield 7n;yield 9n;}try{for(const n of values()){console.log(n*2n);}}catch(e:any){console.log('literal generator failed');}", "14n\n18n\n"];
        yield return ["issue1940-controls.ts", "function* values():Generator<bigint,void,any>{yield BigInt('7');yield BigInt('9');yield 9223372036854775808n;yield -9223372036854775809n;}for(const n of values()){console.log(n);}", "7n\n9n\n9223372036854775808n\n-9223372036854775809n\n"];
        yield return ["issue1938-original.ts", "const m:any=Math;console.log(m.max(),m.min(),m.max(1,9,3),m.min(1,-2,3),m.max(1,NaN));console.log(Object.is(m.max(-0,0),-0),Object.is(m.min(0,-0),-0));", "-Infinity Infinity 9 -2 NaN\nfalse true\n"];
        yield return ["issue1938-controls.ts", "const min:any=Math.min;const max:any=Math.max;console.log(Object.is(Math.max(-0,0),0),Object.is(Math.max(0,-0),0),Object.is(Math.min(-0,0),-0),Object.is(Math.min(0,-0),-0));console.log(Object.is(max(-0,0),0),Object.is(max(0,-0),0),Object.is(min(-0,0),-0),Object.is(min(0,-0),-0));console.log(min(),max(),min(1,NaN),max(NaN,1),min(7,2,3),max(7,2,3));", "true true true true\ntrue true true true\nInfinity -Infinity NaN NaN 2 7\n"];
        yield return ["issue1937-original.ts", "const sign:any=Math.sign;const pow:any=Math.pow;console.log(sign(-2),sign(0),sign(3),sign(NaN),Object.is(sign(-0),-0));console.log(pow('2','3'),pow(NaN,0),pow(-1,0.5));", "-1 0 1 NaN true\n8 1 NaN\n"];
        yield return ["issue1937-controls.ts", "const sign:any=Math.sign;console.log(Object.is(Math.sign(-0),-0),Object.is(sign(-0),-0),Object.is(Math.sign(0),0),Object.is(sign(0),0));console.log(sign('-0'),Object.is(sign('-0'),-0),sign(Infinity),sign(-Infinity),sign(NaN));", "true true true true\n0 true 1 -1 NaN\n"];
        yield return ["issue1936-original.ts", "const sum:any=Math.sumPrecise;console.log(sum([Infinity,1]),sum([-Infinity,1]),sum([Infinity,-Infinity]),sum([NaN,1]),sum([Number.MAX_VALUE,Number.MAX_VALUE]));", "Infinity -Infinity NaN NaN Infinity\n"];
        yield return ["issue1936-controls.ts", "const math:any=Math;const sum:any=math.sumPrecise;console.log(math.sumPrecise([Infinity,1]),math.sumPrecise([NaN,1]),math.sumPrecise([1,2,3]),sum([1,2,3]));console.log(sum([1e16,1,-1e16]),Object.is(sum([]),-0),Object.is(sum([-0,-0]),-0));", "Infinity NaN 6 6\n1 true true\n"];
        yield return ["issue1935-original.ts", "const m:any=Math;console.log(m===globalThis.Math,m.floor===Math.floor,m.random===Math.random,m.sumPrecise===Math.sumPrecise);console.log(m.floor.name,m.floor.length,m.random.length,m.pow.length,m.hypot.length);", "true true true true\nfloor 1 0 2 2\n"];
        yield return ["issue1935-controls.ts", "const root:any=globalThis;const m:any=Math;console.log(m===root.Math,m===globalThis['Math'],root.Math.floor===Math.floor);m.extra=7;console.log(root.Math.extra,globalThis.Math===m);", "true true true\n7 true\n"];
        yield return ["issue1934-original.ts", "function parse(value:string,radix:number){return parseInt(value,radix);}console.log(parse('0xff',16),parse('10101',2),parse('zz',36),parse('-11',8),parse('10',1),parse('0X10',0));", "255 21 1295 -9 NaN 16\n"];
        yield return ["issue1934-controls.ts", "function parse(value:string,radix:number){return parseInt(value,radix);}const alias:any=parseInt;console.log(parse('+0X10',16),parse('-0xff',16),parse('ff',16),alias('0xff',16),Number.parseInt('0X10',16),parse('0xff',10));console.log(parse('0x',16),parse('-0X',0));", "16 -255 255 255 16 0\nNaN NaN\n"];
        yield return ["issue1933-original.ts", "let trace='';const value:any={[Symbol.toPrimitive](hint:any){trace+=hint;return '9';}};console.log(Number(value),+value,trace);", "9 9 numbernumber\n"];
        yield return ["issue1933-controls.ts", "let trace='';const value:any={[Symbol.toPrimitive](hint:any){trace+=hint;return '9';}};console.log(Number(value),trace);console.log(Number(42n));const bad:any={[Symbol.toPrimitive](){return {};}};try{Number(bad);}catch(e:any){console.log(e.name);}", "9 number\n42\nTypeError\n"];
        yield return ["issue1932-original.ts", "let hints='';const box:any=new Number(1);box[Symbol.toPrimitive]=function(hint:any){hints+=hint+';';return 4;};console.log(box+1,box==4,String(box),hints);", "5 true 4 default;default;string;\n"];
        yield return ["issue1932-controls.ts", "const box:any=new Number(2);console.log(String(box));box.toString=function(){return 'own';};console.log(String(box));box[Symbol.toPrimitive]=function(hint:any){console.log(hint,this===box);return 5;};console.log(String(box));box[Symbol.toPrimitive]=function(){return {};};try{String(box);}catch(e:any){console.log(e.name);}", "2\nown\nstring true\n5\nTypeError\n"];
        yield return ["issue1931-original.ts", "const obj:any={prefix:'P',tag(strings:any,...values:any[]){return this.prefix+':'+strings.join('|')+':'+values.join(',');}};console.log(obj.tag`a${7}b`);console.log(obj['tag']`c${8}d`);", "P:a|b:7\nP:c|d:8\n"];
        yield return ["issue1931-controls.ts", "let trace='';const key='tag';const obj:any={prefix:'P',[key](strings:any,...values:any[]){trace+='call;';return this.prefix+':'+strings.join('|')+':'+values.join(',');}};function receiver(){trace+='receiver;';return obj;}function index(){trace+='index;';return key;}function value(n:number){trace+='value'+n+';';return n;}console.log(receiver()[index()]`a${value(1)}b${value(2)}c`);console.log(trace);", "P:a|b|c:1,2\nreceiver;index;value1;value2;call;\n"];
        yield return ["issue1953-original.ts", "const value:any='ab';const d:any=Object.getOwnPropertyDescriptor(value,'length');console.log(d.value,d.writable,d.enumerable,d.configurable);console.log(Object.prototype.propertyIsEnumerable.call(value,'length'),Object.keys(value).join(','));", "2 false false false\nfalse 0,1\n"];
        yield return ["issue1953-controls.ts", "for(const value of ['', 'ab',new String('ab')]){const d:any=Object.getOwnPropertyDescriptor(value,'length');console.log(d.value,d.writable,d.enumerable,d.configurable);}console.log(Object.getOwnPropertyDescriptor('ab','missing')===undefined,Object.keys('ab').join(','));", "0 false false false\n2 false false false\n2 false false false\ntrue 0,1\n"];
        yield return ["issue1952-original.ts", "new Promise((resolve:any,reject:any)=>{for(const fn of [resolve,reject]){const d:any=Object.getOwnPropertyDescriptor(fn,'name');console.log(d.value,d.writable,d.enumerable,d.configurable);console.log(Object.prototype.propertyIsEnumerable.call(fn,'name'),Object.keys(fn).join(','));}resolve(1);});", " false false true\nfalse \n false false true\nfalse \n"];
        yield return ["issue1952-controls.ts", "new Promise((resolve:any,reject:any)=>{console.log(resolve.length,reject.length,Object.prototype.propertyIsEnumerable.call(resolve,'length'),Object.prototype.propertyIsEnumerable.call(reject,'length'));resolve(7);}).then((value:any)=>console.log(value));new Promise((resolve:any,reject:any)=>{reject('rejected');}).catch((value:any)=>console.log(value));", "1 1 false false\n7\nrejected\n"];
        yield return ["issue1951-original.ts", "const value:any=[1,2];const d:any=Object.getOwnPropertyDescriptor(value,'length');console.log(d.value,d.writable,d.enumerable,d.configurable);console.log(Object.prototype.propertyIsEnumerable.call(value,'length'),Object.keys(value).join(','));", "2 true false false\nfalse 0,1\n"];
        yield return ["issue1951-controls.ts", "const a:any=[1,2];console.log(Object.prototype.propertyIsEnumerable.call(a,'0'),Object.prototype.propertyIsEnumerable.call(a,'missing'));Object.defineProperty(a,'length',{writable:false});console.log(Object.prototype.propertyIsEnumerable.call(a,'length'),Object.getOwnPropertyDescriptor(a,'length')!.writable);const s:any=new String('ab');const d:any=Object.getOwnPropertyDescriptor(s,'length');console.log(d.value,d.enumerable,Object.keys(s).join(','),Object.keys('ab').join(','));", "true false\nfalse false\n2 false 0,1 0,1\n"];
        yield return ["issue1950-original.ts", """
            let first = Symbol("first");
            let second = Symbol("second");
            let obj: any = {};
            obj[first] = 1;
            obj[second] = 2;
            Object.defineProperty(obj, first, { get: () => 3 });
            let objectKeys = Object.getOwnPropertySymbols(obj);
            console.log(objectKeys[0] === first, objectKeys[1] === second);
            delete obj[first];
            obj[first] = 4;
            objectKeys = Object.getOwnPropertySymbols(obj);
            console.log(objectKeys[0] === second, objectKeys[1] === first);
            let array: any = [];
            array[first] = 1;
            array[second] = 2;
            Object.defineProperty(array, first, { writable: false });
            let arrayKeys = Object.getOwnPropertySymbols(array);
            console.log(arrayKeys[0] === first, arrayKeys[1] === second);
            console.log(Object.getOwnPropertyDescriptor(array, first)!.writable);
            """, "true true\ntrue true\ntrue true\nfalse\n"];
        yield return ["issue1950-controls.ts", "const a=Symbol('a'),b=Symbol('b'),c=Symbol('c'),d=Symbol('d');const value:any={};value[a]=1;value[b]=2;value[c]=3;delete value[b];value[d]=4;value[b]=5;let keys=Object.getOwnPropertySymbols(value);console.log(keys[0]===a,keys[1]===c,keys[2]===d,keys[3]===b);delete value[a];value[a]=6;keys=Object.getOwnPropertySymbols(value);console.log(keys[0]===c,keys[1]===d,keys[2]===b,keys[3]===a,value[c],value[d],value[b],value[a]);", "true true true true\ntrue true true true 3 4 5 6\n"];
        yield return ["issue1949-original.ts", "const value:any={[Symbol.toStringTag]:'Widget'};console.log(Object.prototype.toString.call(value));console.log(value[Symbol.toStringTag]);", "[object Widget]\nWidget\n"];
        yield return ["issue1949-controls.ts", """
            const value:any={};console.log(Object.prototype.toString.call(value));value[Symbol.toStringTag]=42;console.log(Object.prototype.toString.call(value));value[Symbol.toStringTag]='';console.log(Object.prototype.toString.call(value));delete value[Symbol.toStringTag];console.log(Object.prototype.toString.call(value),Object.prototype.toString.call(null),Object.prototype.toString.call(undefined));
            let ownReads=0;
            const boxed=Object(1n);
            Object.defineProperty(boxed,Symbol.toStringTag,{get(){ownReads++;return 123;}});
            console.log(Object.prototype.toString.call(boxed),ownReads);
            let prototypeReads=0;
            Object.defineProperty(BigInt.prototype,Symbol.toStringTag,{configurable:true,get(){prototypeReads++;return 123;}});
            console.log(Object.prototype.toString.call(Object(2n)),prototypeReads);
            """, "[object Object]\n[object Object]\n[object ]\n[object Object] [object Null] [object Undefined]\n[object Object] 1\n[object Object] 1\n"];
        yield return ["issue1948-original.ts", "'use strict';const value:any={};Object.defineProperty(value,'a',{value:1,writable:false});try{value.a=2;}catch(e:any){console.log(e.name);}console.log(value.a);", "TypeError\n1\n"];
        yield return ["issue1948-controls.ts", "const value:any={};Object.defineProperty(value,'a',{value:1,writable:false});value.a=2;console.log(value.a);function strictWrite(){'use strict';try{value.a=3;}catch(e:any){console.log(e.name);}}strictWrite();console.log(value.a);", "1\nTypeError\n1\n"];
        yield return ["issue1947-original.ts", "const proto:any={set value(v:number){(this as any).own=v;}};const value:any=Object.create(proto);value.value=9;console.log(value.own,Object.hasOwn(value,'own'),Object.hasOwn(proto,'own'));", "9 true false\n"];
        yield return ["issue1947-controls.ts", """
            const proto:any={get value(){return (this as any).own;},set value(v:number){(this as any).own=v;}};const middle:any=Object.create(proto);const value:any=Object.create(middle);value['value']=6;console.log(value.value,Object.hasOwn(value,'value'),Object.hasOwn(proto,'own'));
            Object.defineProperty(value,'value',{value:20,writable:true});value.value=21;console.log(value.value,value.own);
            const defined:any={};Object.defineProperty(defined,'value',{set(v:number){(this as any).own=v;}});const child:any=Object.create(defined);child['value']=8;console.log(child.own,Object.hasOwn(defined,'own'));
            """, "6 false false\n21 6\n8 false\n"];
        yield return ["issue1946-original.ts", "const proto:any={base:3,get value(){return (this as any).own+this.base;}};const value:any=Object.create(proto);value.own=4;console.log(value.value,'base' in value,Object.hasOwn(value,'base'),Object.getPrototypeOf(value)===proto);", "7 true false true\n"];
        yield return ["issue1946-controls.ts", """
            let calls=0;const proto:any={base:3,get value(){calls++;return (this as any).own+this.base;}};const middle:any=Object.create(proto);const value:any=Object.create(middle);value.own=4;console.log(value['value'],calls,Object.hasOwn(proto,'own'));
            Object.defineProperty(value,'value',{value:20});console.log(value.value,calls);
            const defined:any={};Object.defineProperty(defined,'value',{get(){return (this as any).own;}});const child:any=Object.create(defined);child.own=8;console.log(child.value);
            """, "7 1 false\n20 1\n8\n"];
        yield return ["issue1945-original.cjs", "const N=Number;console.log(Number('2'),N('2'),N===Number);", "2 2 true\n"];
        yield return ["issue1945-controls.cjs", "const N=Number;console.log(N(),N(null),N(true),N('0x10'),N(42n),Number.isNaN(N(undefined)));const value={valueOf(){return 7;}};console.log(N(value),N===Number);", "0 0 1 16 42 true\n7 true\n"];
        yield return ["issue1944-original.cjs", "const B=Boolean;console.log(Boolean(0),Boolean('x'),B(0),B('x'),B===Boolean);", "false true false true true\n"];
        yield return ["issue1944-controls.cjs", "const B=Boolean;console.log(B(),B(undefined),B(null),B(false),B(0),B(''),B('false'),B({}));", "false false false false false false true true\n"];
        yield return ["issue1942-original.cjs", "const B=BigInt;console.log(BigInt('123'),B('123'),B.asUintN(8,-1n),(255n).toString(16));", "123n 123n 255n ff\n"];
        yield return ["issue1942-controls.cjs", "const B=BigInt;console.log(B===BigInt,B(42),B(true),B('0xff'));try{B(1.5);}catch(e){console.log(e.name);}console.log(B.asIntN(8,255n));", "true 42n 1n 255n\nRangeError\n-1n\n"];
        yield return ["issue1941-original.ts", """
            const buffer=new ArrayBuffer(16);const view=new DataView(buffer);view.setBigInt64(0,-1n,true);view.setBigInt64(8,123n,true);const a=new BigInt64Array(buffer);const b=new BigUint64Array(buffer);console.log(a[0],a[1],a.length,b[0],b[1]);const value:any=a;try{value[0]=1n;}catch(e:any){console.log('assignment failed');}
            """, "-1n 123n 2 18446744073709551615n 123n\n"];
        yield return ["issue1941-controls.ts", """
            const buffer=new ArrayBuffer(8);const signed:any=new BigInt64Array(buffer);const unsigned:any=new BigUint64Array(buffer);
            signed[0]=-1n;console.log(signed[0],unsigned[0]);
            unsigned[0]=18446744073709551617n;console.log(signed[0],unsigned[0]);
            signed[0]=9223372036854775808n;console.log(signed[0],unsigned[0]);
            for(const value of [1,null,undefined]){try{signed[0]=value;}catch(e:any){console.log(e.name);}}
            console.log(signed[0],unsigned[0]);
            """, "-1n 18446744073709551615n\n1n 1n\n-9223372036854775808n 9223372036854775808n\nTypeError\nTypeError\nTypeError\n-9223372036854775808n 9223372036854775808n\n"];
    }

    public static IEnumerable<object[]> ApiCases()
    {
        foreach (var item in Cases())
            foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            {
                // #1933 explicitly separates the interpreter's unary-plus discrepancy.
                // Its Number(value) control remains dual-mode; preserve the complete
                // original source and reference for compiled API/deployment execution.
                // #1934 similarly separates the interpreter's radix-36 discrepancy.
                if (item[0] is "issue1933-original.ts" or "issue1934-original.ts" && mode == ExecutionMode.Interpreted)
                    continue;
                yield return [..item, mode];
            }
    }

    [Theory, MemberData(nameof(ApiCases))]
    public void PreservedSourcesMatchReference(string file, string source, string expected, ExecutionMode mode)
    {
        string actual = file.EndsWith(".cjs", StringComparison.Ordinal)
            ? TestHarness.RunModules(new() { [file] = source }, file, mode)
            : TestHarness.Run(source, mode);
        AssertReferenceOutput(file, expected, actual);
    }

    internal static void AssertReferenceOutput(string file, string expected, string actual)
    {
        Assert.Equal(expected, actual);
    }
}
