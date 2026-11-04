using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3ReflectArrayLikeArgumentTests
{
    [Theory, ModeData]
    public void ReflectReadsArrayLikeLengthAndIndicesWithoutIterator(ExecutionMode mode)
    {
        const string source = """
            const trace=[];
            const args=Object.create({1:17});
            Object.defineProperty(args,'length',{get(){trace.push('length');return 3}});
            Object.defineProperty(args,'0',{get(){trace.push('zero');return 42}});
            args[Symbol.iterator]=function(){throw 'iterator'};
            function target(a,b,c){console.log(a,b,c===undefined);this.value=a;return a}
            console.log(Reflect.apply(target,{},args),trace.join(','));
            trace.length=0;
            console.log(Reflect.construct(target,args).value,trace.join(','));
            const array=[1,,3];
            Object.defineProperty(array,'1',{get(){return 7}});
            console.log(Reflect.apply(function(a,b,c){return a+b+c},null,array));
            """;
        Assert.Equal("42 17 true\n42 length,zero\n42 17 true\n42 length,zero\n11\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ValidationAndAbruptCompletionPrecedeLaterArgumentReads(ExecutionMode mode)
    {
        const string source = """
            let reads=0;
            const marker={};
            const args={get length(){reads++;throw marker}};
            function target(){}
            for(const invoke of [()=>Reflect.apply({},null,args),()=>Reflect.construct({},args),()=>Reflect.construct(target,args,{})]) {
                try{invoke()}catch(error){console.log(error instanceof TypeError,reads)}
            }
            for(const invoke of [()=>Reflect.apply(target,null,args),()=>Reflect.construct(target,args)]) {
                try{invoke()}catch(error){console.log(error===marker,reads)}
            }
            for(const value of [null,undefined,1,'text',true,Symbol()]) {
                try{Reflect.apply(target,null,value)}catch(error){console.log(error instanceof TypeError)}
                try{Reflect.construct(target,value)}catch(error){console.log(error instanceof TypeError)}
            }
            """;
        Assert.Equal("true 0\ntrue 0\ntrue 0\ntrue 1\ntrue 2\n"
            + string.Concat(Enumerable.Repeat("true\n", 12)), TestHarness.RunUnchecked(source, mode));
    }
}
