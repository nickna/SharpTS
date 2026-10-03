using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ProxyConstructorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "constructor-target", """
            class Value{value:number;constructor(n:number){this.value=n;}}const ProxyValue:any=new Proxy(Value,{});console.log(new ProxyValue(8).value);
            """, "8\n" };
        yield return new object[] { "aliased-constructor", """
            const make:any=Proxy;const value:any=new make({value:9},{});console.log(value.value);
            """, "9\n" };
        yield return new object[] { "class-control", """
            class Value{value:number;constructor(n:number){this.value=n;}}console.log(new Value(8).value);
            """, "8\n" };
        yield return new object[] { "dynamic-class-control", """
            class Value{value:number;constructor(n:number){this.value=n;}}const make:any=Value;console.log(new make(8).value);
            """, "8\n" };
        yield return new object[] { "class-forwarding", """
            class Value{value:number;constructor(a:number,b:number){this.value=a*10+b;}}
            const proxy:any=new Proxy(Value,{});
            const nested:any=new Proxy(proxy,{});
            const bound:any=proxy.bind(null,2);
            const first:any=new proxy(2,3);
            const second:any=new nested(4,5);
            const third:any=new bound(6);
            console.log(first.value,second.value,third.value);
            console.log(first instanceof Value,second instanceof Value,third instanceof Value);
            console.log(Object.getPrototypeOf(first)===Value.prototype,Object.getPrototypeOf(second)===Value.prototype);
            """, "23 45 26\ntrue true true\ntrue true\n" };
        yield return new object[] { "function-and-revocable", """
            function Value(this:any,a:number,b:number){this.value=a*10+b;}
            const proxy:any=new Proxy(Value,{});
            const first:any=new proxy(2,3);
            console.log(first.value,first instanceof Value,Object.getPrototypeOf(first)===Value.prototype);
            class Box{value:number;constructor(n:number){this.value=n;}}
            const pair:any=Proxy.revocable(Box,{});
            const second:any=new pair.proxy(7);
            console.log(second.value,second instanceof Box);
            pair.revoke();
            try{new pair.proxy(8);console.log(false);}catch(error){console.log(error instanceof TypeError);}
            """, "23 true true\n7 true\ntrue\n" };
        yield return new object[] { "trap-target-arguments-newtarget", """
            class Value{value:number;constructor(n:number){this.value=n;}}
            let expectedTarget:any;
            const proxy:any=new Proxy(Value,{construct(target:any,args:any[],newTarget:any){
                console.log(target===Value,args.join(','),newTarget===expectedTarget);
                return Reflect.construct(target,args);
            }});
            expectedTarget=proxy;
            console.log(new proxy(8).value);
            class Alternate{}
            expectedTarget=Alternate;
            const reflected:any=Reflect.construct(proxy,[9],Alternate);
            console.log(reflected.value);
            """, "true 8 true\n8\ntrue 9 true\n9\n" };
        yield return new object[] { "invalid-and-revoked-targets", """
            let calls=0;
            const handler:any={construct(){calls++;return {};}};
            const arrow:any=()=>1;
            for(const target of [{},arrow]){
                const proxy:any=new Proxy(target,handler);
                try{new proxy();console.log(false);}catch(error){console.log(error instanceof TypeError);}
            }
            console.log(calls);
            function Value(){}
            const pair:any=Proxy.revocable(Value,handler);
            pair.revoke();
            try{new pair.proxy();console.log(false);}catch(error){console.log(error instanceof TypeError);}
            const alias:any=Proxy;
            for(const target of [null,undefined,1]){
                try{new alias(target,{});console.log(false);}catch(error){console.log(error instanceof TypeError);}
            }
            """, "true\ntrue\n0\ntrue\ntrue\ntrue\ntrue\n" };
        yield return new object[] { "trap-errors-and-recovery", """
            function Value(){}
            const invalidHandler:any={construct(){return 1;}};
            const invalid:any=new Proxy(Value,invalidHandler);
            try{new invalid();console.log(false);}catch(error){console.log(error instanceof TypeError);}
            const token:any={failure:true};
            const failing:any=new Proxy(Value,{construct(){throw token;}});
            try{new failing();console.log(false);}catch(error){console.log(error===token);}
            const valid:any=new Proxy(Value,{construct(){return {value:9};}});
            console.log(new valid().value);
            """, "true\ntrue\n9\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void ProxyConstructorsForwardAndRejectInvalidOperations(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
