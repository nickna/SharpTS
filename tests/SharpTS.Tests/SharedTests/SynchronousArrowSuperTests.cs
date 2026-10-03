using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class SynchronousArrowSuperTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "super_arrow", """
            class A {value(x:number){return x+2;}} class B extends A {read(){const outer=()=>()=>super.value(3);return outer()();}} console.log(new B().read());
            """, "5\n" };
        yield return new object[] { "super_arrow_value_control", """
            class A {value(x:number){return x+2;}}class B extends A {read(){const f=()=>{const method=super.value;return method(3);};return f();}}console.log(new B().read());
            """, "5\n" };
        yield return new object[] { "super_nested_arrow_value_control", """
            class A {value(x:number){return x+2;}}class B extends A {read(){const outer=()=>()=>{const method=super.value;return method(3);};return outer()();}}console.log(new B().read());
            """, "5\n" };
        yield return new object[] { "super_arrow_capture_control", """
            class A {label="parent";value(x:number){return x+2;}}class B extends A {label="child";read(){const f=()=>{const marker=this.label;const method=super.value;return marker+":"+method(3);};return f();}}console.log(new B().read());
            """, "child:5\n" };
        yield return new object[] { "super_nested_arrow_capture_control", """
            class A {label="parent";value(x:number){return x+2;}}class B extends A {label="child";read(){const outer=()=>()=>{const marker=this.label;const method=super.value;return marker+":"+method(3);};return outer()();}}console.log(new B().read());
            """, "child:5\n" };
        yield return new object[] { "direct-captured-receiver", """
            class A {label="parent";value(){return this.label;}}class B extends A {label="child";read(){return (()=>super.value())();}}console.log(new B().read());
            """, "child\n" };
        yield return new object[] { "deep-arrow-capture", """
            class A {value(x:number){return x+2;}}class B extends A {read(){return (()=>()=>()=>{const method=super.value;return method(3);})()()();}}console.log(new B().read());
            """, "5\n" };
        yield return new object[] { "lexical-owner-and-call-receiver", """
            class A {label="parent";value(){return this.label;}}class B extends A {label="child";value(){return "override";}read(){return ()=>super.value();}}class C extends B {label="grandchild";value(){return "later override";}}const f=new C().read();console.log(f.call({label:"fake"}));
            """, "grandchild\n" };
        yield return new object[] { "generic-owner", """
            class A<T> {value(x:T):T{return x;}}class B<T> extends A<T> {read(x:T){return (()=>super.value(x))();}}console.log(new B<string>().read("ok"));
            """, "ok\n" };
        yield return new object[] { "string-arguments-and-order", """
            let log="";function arg(s:string){log+=s;return s;}class A {join(a:string,b:string){return a+b;}}class B extends A {read(){return (()=>super.join(arg("a"),arg("b")))();}}console.log(new B().read(),log);
            """, "ab ab\n" };
        yield return new object[] { "omitted-default", """
            class A {value(x:number=9){return x;}}class B extends A {read(){return (()=>super.value())();}}console.log(new B().read());
            """, "9\n" };
        yield return new object[] { "value-in-later-callback", """
            class A {value(x:number){return x+2;}}class B extends A {read(){return [1,2].map(x=>{const method=super.value;return method(x);}).join(",");}}console.log(new B().read());
            """, "3,4\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void SuperAccessesUseTheLexicalReceiverInSynchronousArrows(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
