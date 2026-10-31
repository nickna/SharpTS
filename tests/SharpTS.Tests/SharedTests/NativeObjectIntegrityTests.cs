using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NativeObjectIntegrityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["native-method-descriptor-inventory", """
            const O:any=Object,read=Object.getOwnPropertyDescriptor,seal=Object.seal,freeze=Object.freeze,names=['assign','create','defineProperties','defineProperty','entries','freeze','fromEntries','getOwnPropertyDescriptor','getOwnPropertyDescriptors','getOwnPropertyNames','getOwnPropertySymbols','getPrototypeOf','hasOwn','is','isExtensible','isFrozen','isSealed','keys','preventExtensions','seal','setPrototypeOf','values'];const before=names.map(name=>O[name]);seal(O);console.log(names.length,names.every((name,index)=>{const d:any=read(O,name);return d.value===before[index]&&d.writable&&!d.enumerable&&!d.configurable;}));freeze(O);console.log(names.every((name,index)=>{const d:any=read(O,name);return d.value===before[index]&&!d.writable&&!d.enumerable&&!d.configurable;}));
            """, "22 true\ntrue\n"];
        yield return ["deleted-and-replaced-native-properties", """
            const O:any=Object,seal=Object.seal,freeze=Object.freeze,read=Object.getOwnPropertyDescriptor;delete O.values;const replacement=()=>7;O.keys=replacement;seal(O);console.log(Object.hasOwn(O,'values'),read(O,'values')===undefined,O.keys===replacement,read(O,'keys')?.enumerable,read(O,'keys')?.configurable);freeze(O);console.log(Object.hasOwn(O,'values'),O.keys===replacement,read(O,'keys')?.writable,delete O.values);
            """, "false true true false false\nfalse true false true\n"];
        yield return ["accessor-freeze-does-not-invoke-getters", """
            const O:any=Object,read=Object.getOwnPropertyDescriptor,freeze=Object.freeze;let reads=0,writes=0;const get=()=>{reads++;return 7;},set=(value:any)=>{writes+=value;};Object.defineProperty(O,'keys',{get,set,configurable:true});freeze(O);const d:any=read(O,'keys');console.log(reads,d.get===get,d.set===set,d.configurable);O.keys=3;console.log(reads,writes,O.keys,reads);
            """, "0 true true false\n0 3 7 1\n"];
        yield return ["existing-and-new-data-writes", """
            const O:any=Object;O.extra=1;Object.freeze(O);O.extra=2;O.newKey=3;console.log(O.extra,Object.hasOwn(O,'newKey'),Reflect.set(O,'extra',4),Reflect.set(O,'newKey',5));function strictWrite(){'use strict';try{O.extra=6;}catch(error){return error instanceof TypeError;}return false;}console.log(strictWrite(),O.extra);
            """, "1 false false false\ntrue 1\n"];
        yield return ["symbol-data-and-accessor-integrity", """
            const O:any=Object,key=Symbol('data'),accessor=Symbol('setter');let written=0;O[key]=1;Object.defineProperty(O,accessor,{get(){return written;},set(value){written=value;},configurable:true});Object.freeze(O);O[key]=2;O[accessor]=3;const data:any=Object.getOwnPropertyDescriptor(O,key),get:any=Object.getOwnPropertyDescriptor(O,accessor);console.log(O[key],data.writable,data.configurable,O[accessor],get.configurable,delete O[key]);
            """, "1 false false 3 false false\n"];
        yield return ["metadata-and-idempotent-integrity", """
            const O:any=Object,read=Object.getOwnPropertyDescriptor;Object.seal(O);console.log(read(O,'name')?.value,read(O,'name')?.writable,read(O,'name')?.configurable,read(O,'length')?.value,read(O,'length')?.configurable);Object.freeze(O);console.log(read(O,'name')?.writable,read(O,'name')?.configurable,read(O,'length')?.writable,Object.isFrozen(O),Object.isSealed(O),Object.isExtensible(O));console.log(Object.freeze(O)===O,Object.seal(O)===O);
            """, "Object false false 1 false\nfalse false false true true false\ntrue true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void IntegrityPreservesNativeOwnPropertyDescriptors(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
