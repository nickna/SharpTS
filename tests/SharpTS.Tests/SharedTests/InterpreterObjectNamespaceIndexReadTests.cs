using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterObjectNamespaceIndexReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const O:any=Object;O[7]=7;console.log(O[7]);", "7\n"];
        yield return ["canonical-numeric-aliases", "const O:any=Object,big:any=0n;O[0]=1;console.log(O[-0],O[big],O['0']);", "1 1 1\n"];
        yield return ["object-key-number", "const O:any=Object,events:string[]=[];Object.defineProperty(O,'7',{get:function(this:any){events.push('get:'+String(this===O));return 7;}});const key:any={[Symbol.toPrimitive](hint:string){events.push(hint);return 7;}};console.log(O[key],events.join(','));", "7 string,get:true\n"];
        yield return ["object-key-symbol", "const O:any=Object,key=Symbol('7'),events:string[]=[];O[key]=11;O[7]=7;const converted:any={[Symbol.toPrimitive](hint:string){events.push(hint);return key;}};console.log(O[converted],O[7],events.join(','));", "11 7 string\n"];
        yield return ["inherited-accessor", "const O:any=Object,F:any=Function.prototype;Object.defineProperty(F,'7',{get:function(this:any){return this===O?8:0;},configurable:true});console.log(O[7],Object.hasOwn(O,'7'));", "8 false\n"];
        yield return ["receiver-and-key-order", "const O:any=Object,events:string[]=[];Object.defineProperty(O,'7',{get:function(this:any){events.push('get:'+String(this===O));return 7;}});function receiver(){events.push('receiver');return O;}function key(){events.push('key');return {[Symbol.toPrimitive](hint:string){events.push(hint);return 7;}};}console.log(receiver()[key() as any],events.join(','));", "7 receiver,key,string,get:true\n"];
        yield return ["abrupt-conversion", "const O:any=Object,marker=new Error('stop');let calls=0;Object.defineProperty(O,'7',{get(){calls++;return 7;}});const key:any={[Symbol.toPrimitive](){throw marker;}};try{O[key];console.log('returned');}catch(error){console.log(error===marker,calls);}", "true 0\n"];
        yield return ["missing-and-nullish-keys", "const O:any=Object,empty:any=null,absent:any=undefined;console.log(O[123],O[empty],O[absent]);", "undefined undefined undefined\n"];
        yield return ["native-member-key", "const O:any=Object,events:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){events.push(hint);return 'keys';}};console.log(typeof O[key],events.join(','));", "function string\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReadsCanonicalKeysThroughTheExistingNamespaceProtocol(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Interpreted)
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        else
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
    }
}
