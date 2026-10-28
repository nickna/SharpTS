using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2RegExpInheritedWriteTests
{
    [Theory, ModeData]
    public void OrdinaryFlagWritesRespectInheritedGetterOnlyDescriptors(ExecutionMode mode)
    {
        const string source = """
            const regex:any=/a/gim;
            regex.global=false;regex['ignoreCase']=false;regex.multiline=false;
            console.log(regex.global,regex.ignoreCase,regex.multiline);
            """;
        Assert.Equal("true true true\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void InterpretedStrictFlagWritesThrowWithoutCreatingOwnOverrides()
    {
        const string source = """
            const regex:any=/a/gim;
            function strictWrites(){
                'use strict';
                try{regex.global=false}catch(error){console.log(error instanceof TypeError)}
                try{regex['ignoreCase']=false}catch(error){console.log(error instanceof TypeError)}
                try{regex.multiline=false}catch(error){console.log(error instanceof TypeError)}
            }
            strictWrites();
            console.log(regex.global,regex.ignoreCase,regex.multiline);
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue true true\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Theory, ModeData]
    public void InheritedSetterOwnDataOverrideAndLastIndexKeepTheirReceiversAndStorage(ExecutionMode mode)
    {
        const string source = """
            const regex:any=/a/gim;
            let value:any;
            let receiver:any;
            Object.defineProperty(RegExp.prototype,'global',{set(next:any){value=next;receiver=this},configurable:true});
            regex['global']=7;
            console.log(value,receiver===regex);
            Object.defineProperty(regex,'ignoreCase',{value:false,writable:true,configurable:true});
            regex.ignoreCase=true;
            console.log(regex.ignoreCase);
            regex.lastIndex='2';
            console.log(regex.lastIndex);
            """;
        Assert.Equal("7 true\ntrue\n2\n", TestHarness.Run(source, mode));
    }
}
