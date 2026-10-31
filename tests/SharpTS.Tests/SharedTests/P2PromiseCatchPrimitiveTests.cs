using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2PromiseCatchPrimitiveTests
{
    [Theory, ModeData]
    public void GenericCatchFindsThenOnAllPrimitivePrototypesAndKeepsTheOriginalReceiver(ExecutionMode mode)
    {
        const string source = """
            const method:any=function(){'use strict';console.log(typeof this,arguments.length);return 7};
            const booleanPrototype:any=Boolean.prototype;
            const numberPrototype:any=Number.prototype;
            const stringPrototype:any=String.prototype;
            const symbolPrototype:any=Symbol.prototype;
            const bigintPrototype:any=BigInt.prototype;
            booleanPrototype.then=method;numberPrototype.then=method;stringPrototype.then=method;
            symbolPrototype.then=method;bigintPrototype.then=method;
            const catcher:any=Promise.prototype.catch;
            for(const value of [true,34,'',Symbol('value'),1n])console.log(catcher.call(value));
            """;
        Assert.Equal("boolean 2\n7\nnumber 2\n7\nstring 2\n7\nsymbol 2\n7\nbigint 2\n7\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void InterpretedSymbolThenGetterIsReadOnceAndTheThenCallKeepsReceiverAndArguments()
    {
        const string source = """
            const value=Symbol('value');
            const marker:any={};
            let reads=0;
            const then:any=function(fulfilled:any,rejected:any){'use strict';console.log(this===value,fulfilled===undefined,rejected===marker);return 9};
            Object.defineProperty(Symbol.prototype,'then',{get:function(){'use strict';reads++;console.log(this===value);return then},configurable:true});
            const catcher:any=Promise.prototype.catch;
            console.log(catcher.call(value,marker),reads);
            try{catcher.call(null)}catch(error){console.log(error instanceof TypeError)}
            try{catcher.call(undefined)}catch(error){console.log(error instanceof TypeError)}
            """;
        Assert.Equal("true\ntrue true true\n9 1\ntrue\ntrue\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
