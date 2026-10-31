using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectKeyWriteCoercionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-strict-key", "'use strict';let calls=0;const target:any={x:1,get marker(){return 0}},key:any={[Symbol.toPrimitive](){calls++;return 'x'}};target[key]=7;console.log(target.x,Object.getOwnPropertyDescriptor(target,'x')!.value,calls);", "7 7 1\n"];
        yield return ["sloppy-receiver-hint-order", "let log='';const target:any={x:1};const key:any={[Symbol.toPrimitive](hint:string){log+='K'+hint+(this===key);return 'x'}};target[key]=(log+='R',7);console.log(target.x,log);", "7 RKstringtrue\n"];
        yield return ["symbol-result", "'use strict';let calls=0;const slot=Symbol('slot'),target:any={},key:any={[Symbol.toPrimitive](){calls++;return slot}};target[key]=7;console.log(target[slot],calls,Object.getOwnPropertySymbols(target)[0]===slot);", "7 1 true\n"];
        yield return ["numeric-result", "'use strict';let calls=0;const target:any={0:1},key:any={[Symbol.toPrimitive](){calls++;return 0}};target[key]=7;console.log(target[0],calls);", "7 1\n"];
        yield return ["null-result", "let calls=0;const target:any={},key:any={[Symbol.toPrimitive](){calls++;return null}};target[key]=7;console.log(target.null,calls);", "7 1\n"];
        yield return ["throwing-key", "let log='';const marker:any={},target:any={x:1},key:any={[Symbol.toPrimitive](){log+='K';throw marker}};try{target[key]=(log+='R',7)}catch(error){console.log(error===marker,target.x,log)}", "true 1 RK\n"];
        yield return ["noncallable-hook", "let calls=0;const target:any={x:1},key:any={[Symbol.toPrimitive]:7};try{target[key]=(++calls,7)}catch(error){console.log(error instanceof TypeError,target.x,calls)}", "true 1 1\n"];
        yield return ["object-result-refusal", "let calls=0;const target:any={x:1},key:any={[Symbol.toPrimitive](){calls++;return {}}};try{target[key]=7}catch(error){console.log(error instanceof TypeError,target.x,calls)}", "true 1 1\n"];
        yield return ["async-key-and-value", "let log='';const target:any={x:1},key:any={[Symbol.toPrimitive](hint:string){log+='K'+hint;return 'x'}};async function run(){target[await Promise.resolve(key)]=await Promise.resolve((log+='R',7));console.log(target.x,log)}run();", "7 RKstring\n"];
        yield return ["accessor-receiver", "'use strict';let log='';let target:any;target={set x(value:number){log+=(this===target)+':'+value}};const key:any={[Symbol.toPrimitive](){log+='K';return 'x'}};target[key]=7;console.log(log);", "Ktrue:7\n"];
        yield return ["ordinary-fallback", "let log='';const target:any={x:1},key:any={[Symbol.toPrimitive]:undefined,toString(){log+='S';return 'x'},valueOf(){log+='V';return 0}};target[key]=7;console.log(target.x,log);", "7 S\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void SimpleWritesNormalizeTheirObjectKeyOnce(string name, string source, string expected, ExecutionMode mode)
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
