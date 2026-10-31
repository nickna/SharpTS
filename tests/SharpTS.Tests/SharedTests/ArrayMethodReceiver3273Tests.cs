using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayMethodReceiver3273Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["detached-and-borrowed", "const a:any=[],b:any=[];const f:any=a.push;try{f(1)}catch(e){console.log((e as Error).name)}console.log(f.call(b,7),a.length,b.join(','));", "TypeError\n1 0 7\n"];
        yield return ["generic-borrow", "const a:any=[];const f:any=a.push;const target:any={length:0};console.log(f.call(target,7),target[0],target.length);", "1 7 1\n"];
        yield return ["canonical-identity", "const a:any=[],b:any=[];const f:any=a.push;console.log(f===a.push,f===b.push,f===Array.prototype.push);", "true true true\n"];
        yield return ["call-array", "const a:any=[1],b:any=[2];const f:any=a.push;console.log(f.call(b,7,8),a.join(','),b.join(','));", "3 1 2,7,8\n"];
        yield return ["bound-array", "const a:any=[],b:any=[];const f:any=a.push;const g:any=f.bind(b,7);console.log(g(8),a.length,b.join(','));", "2 0 7,8\n"];
        yield return ["ordinary-member", "const a:any=[];console.log(a.push(1,2),a.pop(),a.join(','));", "2 2 1\n"];
        yield return ["generic-accessors", "const a:any=[];const f:any=a.push;let log='';const target:any={get length(){log+='L';return 1},set length(v:number){log+='N'+v},set 1(v:number){log+='I'+v}};console.log(f.call(target,7),log,a.length);", "2 LI7N2 0\n"];
        yield return ["argument-before-refusal", "const a:any=[];const f:any=a.push;let log='';try{f.call(null,(log+='arg',7))}catch(e){console.log((e as Error).name,log,a.length)}", "TypeError arg 0\n"];
        yield return ["method-metadata", "const a:any=[];const f:any=a.push;console.log(f.name,f.length,Object.prototype.hasOwnProperty.call(f,'prototype'));try{new f()}catch(e){console.log((e as Error).name)}", "push 1 false\nTypeError\n"];
        yield return ["computed-literal", "const a:any=[],b:any=[];const f:any=a['push'];console.log(f.call(b,7),a.length,b.join(','));", "1 0 7\n"];
        yield return ["computed-opaque", "const a:any=[],b:any=[];let key:any='push';const f:any=a[key];console.log(f.call(b,7),a.length,b.join(','));", "1 0 7\n"];
        yield return ["reflect-read", "const a:any=[],b:any=[];const f:any=Reflect.get(a,'push');console.log(f.call(b,7),a.length,b.join(','));", "1 0 7\n"];
        yield return ["other-method", "const a:any=[1],b:any=[2,3];const f:any=a.pop;console.log(f.call(b),a.join(','),b.join(','));", "3 1 2\n"];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MethodValuesUseTheActualCallReceiver(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
