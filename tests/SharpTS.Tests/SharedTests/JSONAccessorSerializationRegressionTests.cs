using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class JSONAccessorSerializationRegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["accessor-basic", "let n=0;const o={get x(){n++;return 1;}};console.log(JSON.stringify(o),n);", "{\"x\":1} 1\n"];
        yield return ["replacer-duplicates", "let n=0;const o={get x(){n++;return 1;}};console.log(JSON.stringify(o,[\"x\",\"x\"]),n);", "{\"x\":1} 1\n"];
        yield return ["getter-abrupt", "const marker={};try{JSON.stringify({get x(){throw marker;}});console.log(\"no\");}catch(error){console.log(error===marker);}", "true\n"];
        yield return ["getter-circular", "const o:any={get self(){return o;}};try{JSON.stringify(o);console.log(\"no\");}catch(error){console.log(error instanceof TypeError);}", "true\n"];
        yield return ["direct-circular", "const o:any={};o.self=o;try{JSON.stringify(o);console.log(\"no\");}catch(error){console.log(error instanceof TypeError);}", "true\n"];
        yield return ["excluded-getter", "const marker={};let calls=0;const o={get ignored(){calls++;throw marker;}};try{console.log(JSON.stringify(o,[]),calls);}catch(error){console.log(\"throw\",error===marker,calls);}", "{} 0\n"];
        yield return ["getter-receiver", "const o:any={get x(){return this===o?7:99;}};console.log(JSON.stringify(o));", "{\"x\":7}\n"];
        yield return ["array-nested-getter", "let calls=0;const o={get x(){calls++;return 1;}};console.log(JSON.stringify([o]),calls);", "[{\"x\":1}] 1\n"];
        yield return ["space-getter", "let calls=0;const o={get x(){calls++;return 1;}};console.log(JSON.stringify(o,null,2));console.log(calls);", "{\n  \"x\": 1\n}\n1\n"];
        yield return ["delete-future", "const o:any={get first(){delete o.last;return 1;},last:2};console.log(JSON.stringify(o));", "{\"first\":1}\n"];
        yield return ["hide-future", "const o:any={get first(){Object.defineProperty(o,\"last\",{enumerable:false});return 1;},last:2};console.log(JSON.stringify(o));", "{\"first\":1,\"last\":2}\n"];
        yield return ["replacer-receiver", "let same=false;const o:any={get x(){return 7;}};console.log(JSON.stringify(o,function(key:string,value:any){if(key===\"x\")same=this===o;return value;}),same);", "{\"x\":7} true\n"];
        yield return ["replacer-updates-future", "const log:string[]=[];const o:any={get first(){log.push(\"first\");return 1;},get last(){log.push(\"last\");return 2;}};console.log(JSON.stringify(o,function(key:string,value:any){if(key===\"first\")Object.defineProperty(o,\"last\",{value:7,writable:true,enumerable:true,configurable:true});return value;}),log.join(\",\"));", "{\"first\":1,\"last\":7} first\n"];
        yield return ["inherited-property-list", "const o:any={get own(){return 1;}};const proto:any={get inherited(){return this===o?7:99;}};Object.setPrototypeOf(o,proto);console.log(JSON.stringify(o,[\"inherited\",\"own\"]));", "{\"inherited\":7,\"own\":1}\n"];
        yield return ["symbol-excluded", "const key=Symbol(\"key\"),marker={};let calls=0;const o:any={get x(){return 1;},get [key](){calls++;throw marker;}};console.log(JSON.stringify(o),calls);", "{\"x\":1} 0\n"];
        yield return ["class-property-list", "class Owner{field=1;get accessor(){return this===owner?7:99;}}const owner=new Owner();console.log(JSON.stringify(owner));console.log(JSON.stringify(owner,[\"accessor\",\"field\"]));", "{\"field\":1}\n{\"accessor\":7,\"field\":1}\n"];
        yield return ["throw-undefined", "const o={get x():any{throw undefined;}};try{JSON.stringify(o);console.log(\"no\");}catch(error){console.log(error===undefined);}", "true\n"];
        yield return ["abrupt-pool-reset", "try{JSON.stringify({get x(){throw new Error(\"marker\");}});}catch{}console.log(JSON.stringify({ok:1}),JSON.stringify({get ok(){return 2;}}));", "{\"ok\":1} {\"ok\":2}\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReadsTheOriginalHolderInSerializationOrder(string name, string source, string expected, ExecutionMode mode)
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
