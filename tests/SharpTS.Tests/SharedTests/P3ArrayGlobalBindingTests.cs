using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P3ArrayGlobalBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
        {
            yield return ["bare-write", """
                const intrinsic:any=Array;const replacement:any=function(){};
                Array=replacement;console.log(Array===replacement,globalThis.Array===replacement);
                const result:any=Object.getOwnPropertyNames({value:1});
                console.log(Object.prototype.toString.call(result),result[0]);
                Array=intrinsic;console.log(Array===intrinsic);
                """, "true true\n[object Array] value\ntrue\n", mode];
            yield return ["qualified-write", """
                const replacement:any=function(){};globalThis.Array=replacement;
                console.log(Array===replacement,globalThis.Array===replacement);
                """, "true true\n", mode];
            yield return ["strict-write", """
                'use strict';const replacement:any=function(){};Array=replacement;
                console.log(Array===replacement,globalThis.Array===replacement);
                """, "true true\n", mode];
            yield return ["async-write", """
                const replacement:any=function(){};
                async function update(){Array=await Promise.resolve(replacement);console.log(Array===replacement,globalThis.Array===replacement)}update();
                """, "true true\n", mode];
            yield return ["async-arrow-write", """
                const replacement:any=function(){};
                const update=async()=>{Array=await Promise.resolve(replacement);console.log(Array===replacement,globalThis.Array===replacement)};update();
                """, "true true\n", mode];
            yield return ["generator-write", """
                const replacement:any=function(){};
                function* update(){Array=replacement;yield Array===replacement;}
                console.log(update().next().value,globalThis.Array===replacement);
                """, "true true\n", mode];
            yield return ["lexical-shadow", """
                const intrinsic:any=Array;{let Array:any=1;Array=3;console.log(Array,globalThis.Array===intrinsic);}
                console.log(Array===intrinsic);
                """, "3 true\ntrue\n", mode];
            yield return ["replacement-calls-and-construction", """
                const replacement:any=function(value:any){this.marker=value;return 9;};Array=replacement;
                replacement.isArray=()=> 'replacement';console.log(Array(5),new Array(7).marker,Array.isArray([]));
                """, "9 7 replacement\n", mode];
            yield return ["strict-deleted-write", """
                const intrinsic:any=Array;delete globalThis.Array;
                function write(){'use strict';try{Array=intrinsic;}catch(error){console.log(error instanceof ReferenceError);}}
                write();console.log(typeof Array);globalThis.Array=intrinsic;
                """, "true\nundefined\n", mode];
            yield return ["accessor-write", """
                const intrinsic:any=Array;const replacement:any=function(){};let value:any=intrinsic;let reads=0;let writes=0;
                Object.defineProperty(globalThis,'Array',{configurable:true,get(){reads++;return value;},set(next:any){writes++;value=next;}});
                function write(){'use strict';Array=replacement;}write();console.log(reads,writes,Array===replacement,reads);
                """, "0 1 true 1\n", mode];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void MutableArrayGlobalUsesTheLiveBinding(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunUnchecked(source, mode));
    }
}
