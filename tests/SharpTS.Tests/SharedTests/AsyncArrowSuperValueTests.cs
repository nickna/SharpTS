using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncArrowSuperValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "super_async_arrow_value_control", """
            class A {value(x:number){return x+2;}}class B extends A {read(){const f=async()=>{await Promise.resolve(0);const method=super.value;return method(5);};return f();}}new B().read().then(v=>console.log(v));
            """, "7\n" };
        yield return new object[] { "super_async_arrow_capture_control", """
            class A {label="parent";value(x:number){return x+2;}}class B extends A {label="child";read(){const f=async()=>{await Promise.resolve(0);const marker=this.label;const method=super.value;return marker+":"+method(5);};return f();}}new B().read().then(v=>console.log(v));
            """, "child:7\n" };
        yield return new object[] { "super_async_arrow_rejection_control", """
            class A {value(x:number){return x+2;}}class B extends A {read(){const f=async()=>{await Promise.resolve(0);const method=super.value;return method(5);};return f();}}new B().read().then(v=>console.log(v)).catch(e=>console.log("rejected",e.name,e.message));
            """, "7\n" };
        yield return new object[] { "before-and-after-await", """
            class A {value(x:number){return x+2;}}class B extends A {read(){const f=async()=>{const first=super.value;await Promise.resolve(0);const second=super.value;return first(3)+second(4);};return f();}}new B().read().then(v=>console.log(v));
            """, "11\n" };
        yield return new object[] { "arrow-in-async-method", """
            class A {value(x:number){return x+2;}}class B extends A {async read(){const f=async()=>{await Promise.resolve(0);const method=super.value;return method(5);};await Promise.resolve(0);return await f();}}new B().read().then(v=>console.log(v));
            """, "7\n" };
        yield return new object[] { "generic-owner", """
            class A<T> {value(x:T):T{return x;}}class B<T> extends A<T> {read(x:T){const f=async()=>{await Promise.resolve(0);const method=super.value;return method(x);};return f();}}new B<string>().read("ok").then(v=>console.log(v));
            """, "ok\n" };
        yield return new object[] { "ordinary-async-method-control", """
            class A {value(x:number){return x+2;}}class B extends A {async read(){const first=super.value;await Promise.resolve(0);const second=super.value;return first(3)+second(4);}}new B().read().then(v=>console.log(v));
            """, "11\n" };
        yield return new object[] { "ordinary-async-method-rejection-control", """
            class A {value(x:number){return x+2;}}class B extends A {async read(){await Promise.resolve(0);const method=super.value;return method(5);}}new B().read().then(v=>console.log(v)).catch(e=>console.log("rejected",e.name,e.message));
            """, "7\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void AsyncArrowSuperValuesUseTheCapturedReceiver(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
