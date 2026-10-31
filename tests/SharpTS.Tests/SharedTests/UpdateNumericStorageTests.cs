using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UpdateNumericStorageTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["prefix-variable-bigint", "let calls=0,value:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return 4n;}};const next=++value;console.log(String(next),typeof next,String(value),calls);", "number\n5 bigint 5 1\n"];
        yield return ["array-bigint", "const values:any[]=[{valueOf(){return 4n;}}];const old=values[0]++;console.log(String(old),typeof old,String(values[0]));", "4 bigint 5\n"];
        yield return ["async-variable-and-member", "async function main(){await new Promise<void>(resolve=>setTimeout(resolve,0));let value:any={valueOf(){return 4n;}};const obj:any={value:{valueOf(){return 7n;}}};const old=value++,next=--obj['value'];console.log(String(old),typeof old,String(value),String(next),typeof next,String(obj.value));}main();", "4 bigint 5 6 bigint 6\n"];
        yield return ["generator-variable", "let calls=0;function* values(){let value:any={valueOf(){calls++;return 4n;}};yield value++;yield ++value;yield typeof value;}console.log([...values()].map(String).join(','),calls);", "4,6,bigint 1\n"];
        yield return ["static-own-and-inherited", "class Base{static value:any={valueOf(){return 4n;}}}class Child extends Base{}const old=Child.value++;console.log(String(old),typeof old,String(Child.value),typeof Base.value,Object.hasOwn(Child,'value'));const next=++Base.value;console.log(String(next),typeof next,String(Base.value),String(Child.value));", "4 bigint 5 object true\n5 bigint 5 5\n"];
        yield return ["static-this", "class Box{static value:any={valueOf(){return 4n;}};static increment(){return this.value++;}static decrement(){return --this.value;}}const old=Box.increment(),next=Box.decrement();console.log(String(old),typeof old,String(next),typeof next,String(Box.value));", "4 bigint 4 bigint 4\n"];
        yield return ["generator-static-field", "class Box{static value:any={valueOf(){return 4n;}}}function* values(){yield Box.value++;yield ++Box.value;yield typeof Box.value;}console.log([...values()].map(String).join(','),String(Box.value));", "4,6,bigint 6\n"];
        yield return ["setter-throw-after-once-conversion", "const events:string[]=[],marker={};const obj:any={get value(){events.push('get');return {valueOf(){events.push('convert');return 4n;}};},set value(value:any){events.push('set:'+typeof value);throw marker;}};try{obj.value++;}catch(error){console.log(error===marker,events.join(','));}", "true get,convert,set:bigint\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesConvertedNumericKindAcrossStorageAndSuspension(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Interpreted) Assert.Equal(expected, TestHarness.RunInterpreted(source));
        else
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
    }
}
