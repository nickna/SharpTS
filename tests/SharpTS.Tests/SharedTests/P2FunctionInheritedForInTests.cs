using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2FunctionInheritedForInTests
{
    [Theory, ModeData]
    public void FunctionEnumerationIncludesInheritedDataAndAccessorsWithoutCallingGetters(ExecutionMode mode)
    {
        const string source = """
            let reads=0;
            Object.defineProperty(Function.prototype,'inherited',{value:1,enumerable:true,configurable:true});
            Object.defineProperty(Function.prototype,'accessor',{get(){reads++;return 2},enumerable:true,configurable:true});
            const functionValue:any=function(){};
            const bound:any=functionValue.bind({});
            bound.own=3;
            const keys:any[]=[];
            for(const key in bound)keys.push(key);
            console.log(keys.join(','),reads,Object.hasOwn(bound,'inherited'));
            Object.defineProperty(bound,'inherited',{value:4,enumerable:false});
            const shadowed:any[]=[];
            for(const key in bound)shadowed.push(key);
            console.log(shadowed.join(','));
            const ordinary:any[]=[];
            for(const key in functionValue)ordinary.push(key);
            console.log(ordinary.join(','));
            """;
        Assert.Equal("own,inherited,accessor 0 false\nown,accessor\ninherited,accessor\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionEnumerationUsesTheSameLivePrototypeChain(ExecutionMode mode)
    {
        const string source = """
            Object.defineProperty(Function.prototype,'inherited',{value:1,enumerable:true,configurable:true});
            const bound:any=(function(){}).bind({});
            async function inspect(){
                const keys:any[]=[];
                for(const key in bound){await Promise.resolve();keys.push(key)}
                console.log(keys.join(','));
            }
            inspect();
            """;
        Assert.Equal("inherited\n", TestHarness.Run(source, mode));
    }
}
