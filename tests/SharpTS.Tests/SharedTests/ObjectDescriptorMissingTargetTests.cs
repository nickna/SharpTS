using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectDescriptorMissingTargetTests
{
    [Theory,ModeData]
    public void MissingTargetHasTheSameTypeErrorAcrossCallForms(ExecutionMode mode)
    {
        const string source="""
            const own:any=Object.getOwnPropertyDescriptor,bound=own.bind(null);
            for(const invoke of [()=>own(),()=>own.call(null),()=>own.apply(null,[]),()=>bound(),()=>(Object.getOwnPropertyDescriptor as any)()]){
                try{invoke();}catch(error){console.log(error instanceof TypeError);}
            }
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n",TestHarness.Run(source,mode));
    }
}
