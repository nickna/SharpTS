using SharpTS.Tests.Infrastructure;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CallableProxyWrapperTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothInterpreterBoundCallPathsRetainProxyReceiverAndArguments(bool valuePath)
    {
        var receiver = new SharpTSObject([]);
        object? observedReceiver = null;
        List<object?>? observedArguments = null;
        var target = BuiltInMethod.CreateV2("target", 0, int.MaxValue, (_, _, _) => RuntimeValue.Undefined);
        var trap = BuiltInMethod.CreateV2("apply", 3, 3, (_, _, arguments) =>
        {
            observedReceiver = arguments[1].ToObject();
            observedArguments = (List<object?>)arguments[2].ToObject()!;
            return RuntimeValue.FromObject(receiver);
        });
        var proxy = new SharpTSProxy(target, new SharpTSObject(new Dictionary<string, object?> { ["apply"] = trap }));
        var bound = new BoundFunction(proxy, receiver, [1d]);
        var result = valuePath
            ? bound.CallV2(null!, [RuntimeValue.FromNumber(2d)]).ToObject()
            : bound.Call(null!, [2d]);
        Assert.Same(receiver, result);
        Assert.Same(receiver, observedReceiver);
        Assert.Equal(new object?[] { 1d, 2d }, observedArguments);
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "delegating-original", """
            function f(this:any,a:number,b:number){return this.x+a+b;}const p:any=new Proxy(f,{apply(target:any,receiver:any,args:any[]){return target.apply(receiver,args)*2;}});console.log(p.call({x:1},2,3),p.apply({x:4},[5,6]),p.bind({x:7},8)(9));
            """, "12 30 48\n" };
        yield return new object[] { "direct-original", """
            const p:any=new Proxy(function(a:number){return a;},{apply(target:any,receiver:any,args:any[]){return receiver.x+args[0];}});console.log(p.call({x:1},2),p.apply({x:4},[5]),p.bind({x:7},8)());
            """, "3 9 15\n" };
        yield return new object[] { "controls", """
            function f(this:any,a:number,b:number){return this.x+a+b;}
            const direct:any=f;
            console.log(direct.call({x:1},2,3),direct.apply({x:4},[5,6]),direct.bind({x:7},8)(9));
            const inner:any=new Proxy(f,{apply(target:any,receiver:any,args:any[]){return target.apply(receiver,args)*2;}});
            const outer:any=new Proxy(inner,{apply(target:any,receiver:any,args:any[]){return target.apply(receiver,args)+1;}});
            console.log(outer.call({x:1},2,3),outer.apply({x:4},[5,6]),outer.bind({x:7},8)(9));
            const marker:any={same:true};
            const throwing:any=new Proxy(f,{apply(){throw marker;}});
            try{throwing.call(null,1,2);}catch(error){console.log(error===marker);}
            try{throwing.apply(null,[1,2]);}catch(error){console.log(error===marker);}
            try{throwing.bind(null,1)(2);}catch(error){console.log(error===marker);}
            const bind:any=f.bind;
            try{bind.call(new Proxy({},{}),null);console.log('accepted');}catch(error){console.log(error instanceof TypeError);}
            """, "6 15 24\n13 31 49\ntrue\ntrue\ntrue\ntrue\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void CallableProxyWrappersPreserveReceiversArgumentsAndErrors(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
