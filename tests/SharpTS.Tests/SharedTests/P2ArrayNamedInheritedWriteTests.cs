using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2ArrayNamedInheritedWriteTests
{
    [Theory, ModeData]
    public void NamedWritesUseTheInheritedSetterAndOriginalReceiver(ExecutionMode mode)
    {
        const string source = """
            let stored:any='initial';
            let receiver:any;
            Object.defineProperty(Array.prototype,'named',{
                get(){return stored},set(value:any){stored=value;receiver=this},configurable:true
            });
            const array:any=[];
            array.named='dot';
            console.log(Object.hasOwn(array,'named'),array.named,stored,receiver===array);
            array['named']='bracket';
            console.log(Object.hasOwn(array,'named'),array.named,stored,receiver===array);
            Object.defineProperty(array,'named',{value:'own',writable:true,configurable:true});
            array.named='own write';
            console.log(array.named,stored);
            """;
        Assert.Equal("false dot dot true\nfalse bracket bracket true\nown write bracket\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MissingNamedWriteHonorsGetterOnlyAndReadonlyPrototypes(ExecutionMode mode)
    {
        const string source = """
            Object.defineProperty(Array.prototype,'getter',{get(){return 7},configurable:true});
            Object.defineProperty(Object.prototype,'readonly',{value:8,writable:false,configurable:true});
            const array:any=[];
            array.getter=1;
            array.readonly=2;
            console.log(array.getter,array.readonly,Object.hasOwn(array,'getter'),Object.hasOwn(array,'readonly'));
            """;
        Assert.Equal("7 8 false false\n", TestHarness.Run(source, mode));
    }
}
