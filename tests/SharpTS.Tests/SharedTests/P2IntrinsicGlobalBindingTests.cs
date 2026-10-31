using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2IntrinsicGlobalBindingTests
{
    [Theory, ModeData]
    public void GlobalObjectReplacementUpdatesBareObjectWithoutChangingIntrinsicAlgorithms(ExecutionMode mode)
    {
        const string source = """
            const intrinsic:any=Object;
            const replacement:any=function(){throw new Error('replacement called')};
            replacement.entries=Object.entries;
            replacement.values=Object.values;
            const global:any=Function('return this')();
            global.Object=replacement;
            console.log(Object===replacement,global.Object===replacement);
            console.log(Object.entries(1).length,Object.values(1).length);
            global.Object=intrinsic;
            console.log(Object===intrinsic);
            """;
        Assert.Equal("true true\n0 0\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    [Fact]
    public void InterpretedBareArrayAssignmentUpdatesTheGlobalPropertyAndReflectionKeepsIntrinsicArrayCreation()
    {
        const string source = """
            const intrinsic:any=Array;
            const replacement:any=function(){throw new Error('replacement called')};
            Array=replacement;
            console.log(Array===replacement,globalThis.Array===replacement);
            const result:any=Object.getOwnPropertyNames({value:1});
            console.log(Object.prototype.toString.call(result),result[0]);
            Array=intrinsic;
            console.log(Array===intrinsic);
            """;
        Assert.Equal("true true\n[object Array] value\ntrue\n", TestHarness.RunUnchecked(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void InterpretedLexicalShadowsWinAndAsyncAssignmentUsesTheGlobalBinding()
    {
        const string source = """
            const intrinsic:any=Array;
            const replacement:any=function(){};
            async function update(){Array=await Promise.resolve(replacement);console.log(Array===replacement,globalThis.Array===replacement)}
            async function run(){
                await update();
                {let Array:any=3;Array++;Array+=2;console.log(Array,globalThis.Array===replacement)}
                Array=intrinsic;
                console.log(Array===intrinsic);
            }
            run();
            """;
        Assert.Equal("true true\n6 true\ntrue\n", TestHarness.RunUnchecked(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void InterpretedConstructionUsesTheReplacementAndDeletedStrictGlobalThrows()
    {
        const string source = """
            const intrinsic:any=Array;
            const replacement:any=function(){this.marker=7};
            Array=replacement;
            const instance:any=new Array();
            console.log(instance.marker);
            delete globalThis.Array;
            function strictWrite(){'use strict';try{Array=intrinsic}catch(error){console.log(error instanceof ReferenceError)}}
            strictWrite();
            globalThis.Array=intrinsic;
            """;
        Assert.Equal("7\ntrue\n", TestHarness.RunUnchecked(source, ExecutionMode.Interpreted));
    }
}
