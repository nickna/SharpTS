using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalAbortControllerDescriptorTests
{
    [Theory, ModeData]
    public void OwnDescriptorUsesTheLiveCanonicalConstructor(ExecutionMode mode)
    {
        const string source = """
            function check(name:string,constructor:any){
                const descriptor:any=Object.getOwnPropertyDescriptor(globalThis,name);
                console.log(name,descriptor===undefined?'absent':'own',
                    descriptor!==undefined&&descriptor.value===constructor,
                    Reflect.get(globalThis,name)===constructor,(globalThis as any)[name]===constructor);
            }
            check('AbortController',AbortController);
            check('Map',Map);
            check('Date',Date);
            const descriptor:any=Object.getOwnPropertyDescriptor(globalThis,'AbortController');
            console.log(descriptor.writable,descriptor.enumerable,descriptor.configurable);
            """;
        Assert.Equal("AbortController own true true true\nMap own true true true\nDate own true true true\ntrue false true\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExplicitDescriptorOverridesIntrinsicSynthesis(ExecutionMode mode)
    {
        const string source = """
            const constructor:any=AbortController;
            Object.defineProperty(globalThis,'AbortController',{value:17,writable:false,enumerable:true,configurable:true});
            const descriptor:any=Object.getOwnPropertyDescriptor(globalThis,'AbortController');
            console.log(descriptor.value,descriptor.writable,descriptor.enumerable,descriptor.configurable,
                Reflect.get(globalThis,'AbortController'),globalThis.AbortController as any);
            Object.defineProperty(globalThis,'AbortController',{value:constructor,writable:true,enumerable:false,configurable:true});
            """;
        Assert.Equal("17 false true true 17 17\n", TestHarness.Run(source, mode));
    }
}
