using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BoundConstructorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "bound-original", """
            function Inner(this:any){this.y=2;}const receiver:any={y:90};const bound:any=Inner.bind(receiver);const b:any=new bound();console.log(b.y,receiver.y,b===receiver);
            """, "2 90 false\n" };
        yield return new object[] { "combined-original", """
            function Inner(this:any){this.y=2;}function Outer(this:any){this.x=1;const C:any=Inner;const child:any=new C();this.x+=child.y;}const C:any=Outer;console.log(new C().x);const bound:any=Inner.bind({y:90});const b:any=new bound();console.log(b.y);
            """, "3\n2\n" };
        yield return new object[] { "forwarding", """
            function Value(this:any,a:any,b:any){this.value=a*10+b;return 0;}
            const receiver:any={value:99};
            const other:any={value:88};
            const first:any=Value.bind(receiver,2);
            const second:any=first.bind(other,3);
            const instance:any=new second();
            console.log(instance.value,receiver.value,other.value,instance===receiver);
            console.log(instance instanceof Value,Object.getPrototypeOf(instance)===Value.prototype);
            first(4);console.log(receiver.value,other.value);
            const object:any={value:7};
            function Returned(this:any){this.value=1;return object;}
            const returns:any=Returned.bind(receiver);
            console.log(new returns()===object,receiver.value);
            """, "23 99 88 false\ntrue true\n24 88\ntrue 24\n" };
        yield return new object[] { "errors", """
            const token:any={failure:true};
            function Failed(this:any){this.value=1;throw token;}
            const receiver:any={value:90};
            const bound:any=Failed.bind(receiver);
            try{new bound();console.log(false);}catch(error){console.log(error===token,receiver.value);}
            function Later(this:any){this.value=2;}
            const next:any=Later.bind(receiver);
            console.log(new next().value,receiver.value);
            """, "true 90\n2 90\n" };
        yield return new object[] { "async-construction", """
            function Inner(this:any){this.y=2;}
            async function run(){
                const receiver:any={y:90};
                const bound:any=Inner.bind(receiver);
                await Promise.resolve();
                const instance:any=new bound();
                console.log(instance.y,receiver.y,instance===receiver);
            }
            run();
            """, "2 90 false\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void ConstructionUsesTargetPrototypeAndFreshReceiver(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
