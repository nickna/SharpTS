using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectNamespaceKeyAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const O:any=Object,replacement=()=>9;let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return 'keys';}};O[key]=replacement;const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(O.keys===replacement,d.value===replacement,calls);", "string\ntrue true 1\n"];
        yield return ["receiver-key-rhs-conversion-order", "const events:string[]=[];const O:any=Object,replacement=()=>7;const key:any={[Symbol.toPrimitive](hint:string){events.push('convert:'+hint);return 'keys';}};function receiver(){events.push('receiver');return O;}function property(){events.push('key');return key;}function value(){events.push('value');return replacement;}receiver()[property()]=value();console.log(events.join(','),O.keys===replacement);", "receiver,key,value,convert:string true\n"];
        yield return ["actual-await", "const events:string[]=[];const O:any=Object,replacement=()=>7;const key:any={[Symbol.toPrimitive](hint:string){events.push('convert:'+hint);return 'keys';}};async function receiver(){events.push('receiver');await new Promise<void>(resolve=>setTimeout(()=>resolve(),1));return O;}async function property(){events.push('key');await new Promise<void>(resolve=>setTimeout(()=>resolve(),1));return key;}function value(){events.push('value');return replacement;}async function run(){(await receiver())[await property()]=value();console.log(events.join(','),O.keys===replacement);}run();", "receiver,key,value,convert:string true\n"];
        yield return ["abrupt-rhs", "const O:any=Object;let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return 'keys';}};function value():any{throw new Error('rhs-stop');}try{O[key]=value();}catch(error){console.log((error as any).message,calls,typeof O.keys);}", "rhs-stop 0 function\n"];
        yield return ["abrupt-key", "const O:any=Object,events:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){events.push('key:'+hint);throw new Error('key-stop');}};function value(){events.push('value');return 7;}try{O[key]=value();}catch(error){console.log((error as any).message,events.join(','),typeof O.keys);}", "key-stop value,key:string function\n"];
        yield return ["nonprimitive-key-result", "const O:any=Object;let calls=0,values=0;const key:any={[Symbol.toPrimitive](){calls++;return {};}};function value(){values++;return 7;}try{O[key]=value();}catch(error){console.log(error instanceof TypeError,calls,values,typeof O.keys);}", "true 1 1 function\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ConvertsNamespaceAssignmentKeysOnceAfterTheRhs(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
