using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3ClassInitializerPropertyOrderTests
{
    [Theory, ModeData]
    public void InitializerExpandosKeepTheirActualCreationPosition(ExecutionMode mode)
    {
        const string source = """
            class Order{first:number=((this as any).extra=1);later:number=7}
            const value:any=new Order();
            console.log(Object.keys(value).join(','),Object.getOwnPropertyNames(value).join(','));
            console.log(JSON.stringify(Object.values(value)),JSON.stringify(Object.entries(value)));
            console.log(JSON.stringify(Object.assign({},value)));
            value.after=9;
            Object.defineProperty(value,'first',{value:3});
            console.log(Object.keys(value).join(','));
            delete value.first;value.first=4;
            console.log(Object.keys(value).join(','));
            """;
        Assert.Equal("extra,first,later extra,first,later\n[1,1,7] [[\"extra\",1],[\"first\",1],[\"later\",7]]\n"
            + "{\"extra\":1,\"first\":1,\"later\":7}\nextra,first,later,after\nextra,later,after,first\n",
            TestHarness.Run(source, mode));
    }

    // Interpreted class integer-index normalization is independently tracked in #3478.
    [Theory, CompiledOnlyData]
    public void ComputedIntegerAndSymbolKeysKeepTheirRespectiveOrderingRules(ExecutionMode mode)
    {
        const string source = """
            const symbol=Symbol('key');
            class Order{
                first:number=((this as any).extra=1);
                [symbol]:number=4;
                ['middle']:number=5;
                later:number=((this as any)[2]=7);
            }
            const value:any=new Order();
            console.log(Object.keys(value).join(','));
            console.log(Reflect.ownKeys(value).map((key:any)=>typeof key==='symbol'?'symbol':key).join(','));
            console.log(value[symbol],value.middle);
            """;
        Assert.Equal("2,extra,first,middle,later\n2,extra,first,middle,later,symbol\n4 5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DerivedExpandosAndBaseFieldsShareActualCreationOrder(ExecutionMode mode)
    {
        const string source = """
            class Base{before:number=2}
            class Derived extends Base{first:number=((this as any).extra=1);later:number=7}
            const value:any=new Derived();
            console.log(Object.keys(value).join(','),JSON.stringify(Object.values(value)));
            """;
        Assert.Equal("before,extra,first,later [2,1,1,7]\n", TestHarness.Run(source, mode));
    }

    // Interpreted descriptor-defined class key chronology is separately tracked in #3480.
    [Theory, CompiledOnlyData]
    public void InitializerDescriptorDefinitionsKeepTheirCreationPosition(ExecutionMode mode)
    {
        const string source = """
            const getter=()=>9;
            class Descriptor{first:number=(Object.defineProperty(this,'extra',{get:getter,enumerable:true}),1);later:number=7}
            const descriptor:any=new Descriptor();
            console.log(Object.keys(descriptor).join(','),JSON.stringify(Object.values(descriptor)));
            """;
        Assert.Equal("extra,first,later [9,1,7]\n", TestHarness.Run(source, mode));
    }
}
