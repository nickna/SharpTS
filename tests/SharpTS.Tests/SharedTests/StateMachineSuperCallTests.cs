using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StateMachineSuperCallTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "super_async", """
            class A {value(x:number){return x+2;}} class B extends A {async read(){const a=super.value(3);await Promise.resolve(0);return a+super.value(4);}} new B().read().then(v=>console.log(v));
            """, "11\n" };
        yield return new object[] { "super_async_arrow", """
            class A {value(x:number){return x+2;}} class B extends A {read(){const f=async()=>{await Promise.resolve(0);return super.value(5);};return f();}} new B().read().then(v=>console.log(v));
            """, "7\n" };
        yield return new object[] { "super_generator", """
            class A {value(x:number){return x+2;}} class B extends A {*read(){yield super.value(1);yield super.value(2);return super.value(3);}} const g=new B().read();console.log(g.next().value,g.next().value,g.next().value);
            """, "3 4 5\n" };
        yield return new object[] { "super_async_generator", """
            class A {value(x:number){return x+2;}} class B extends A {async *read(){await Promise.resolve(0);yield super.value(1);yield super.value(2);}} (async()=>{for await(const v of new B().read())console.log(v);})();
            """, "3\n4\n" };
        yield return new object[] { "receiver-and-lexical-owner", """
            class A {label="parent";value(){return this.label;}}class B extends A {label="child";value(){return "override";}async read(){await Promise.resolve(0);return super.value();}}class C extends B {label="grandchild";value(){return "later override";}}new C().read().then(v=>console.log(v));
            """, "grandchild\n" };
        yield return new object[] { "string-arguments-and-order", """
            let log="";function arg(s:string){log+=s;return s;}class A {join(a:string,b:string){return a+b;}}class B extends A {async read(){await Promise.resolve(0);return super.join(arg("a"),arg("b"));}}new B().read().then(v=>console.log(v,log));
            """, "ab ab\n" };
        yield return new object[] { "omitted-default", """
            class A {value(x:number=9){return x;}}class B extends A {*read(){yield super.value();}}console.log(new B().read().next().value);
            """, "9\n" };
        yield return new object[] { "inherited-parent", """
            class A {value(x:number){return x+2;}}class B extends A {}class C extends B {async read(){await Promise.resolve(0);return super.value(3);}}new C().read().then(v=>console.log(v));
            """, "5\n" };
        yield return new object[] { "generic-parent", """
            class A<T> {value(x:T):T{return x;}}class B extends A<number> {async read(){await Promise.resolve(0);return super.value(3);}}new B().read().then(v=>console.log(v));
            """, "3\n" };
        yield return new object[] { "generic-owner", """
            class A<T> {value(x:T):T{return x;}}class B<T> extends A<T> {async read(x:T){await Promise.resolve(0);return super.value(x);}}new B<string>().read("ok").then(v=>console.log(v));
            """, "ok\n" };
        yield return new object[] { "async-arrow-in-async-method", """
            class A {label="parent";value(){return this.label;}}class B extends A {label="child";async read(){const f=async()=>{await Promise.resolve(0);return super.value();};await Promise.resolve(0);return await f();}}new B().read().then(v=>console.log(v));
            """, "child\n" };
        yield return new object[] { "extra-arguments", """
            let log="";function arg(n:number){log+=n;return n;}class A {value(x:number){return x;}}class B extends A {async read(){await Promise.resolve(0);return (super.value as any)(arg(1),arg(2));}}new B().read().then(v=>console.log(v,log));
            """, "1 12\n" };
        yield return new object[] { "await-in-later-argument", """
            let log="";function arg(n:number){log+=n;return n;}class A {sum(a:number,b:number){return a+b;}}class B extends A {async read(){return super.sum(arg(1),await Promise.resolve(arg(2)));}}new B().read().then(v=>console.log(v,log));
            """, "3 12\n" };
        yield return new object[] { "yield-in-later-argument", """
            class A {sum(a:number,b:number){return a+b;}}class B extends A {*read(){return super.sum(1,yield 2);}}const g=new B().read();console.log(g.next().value,g.next(3).value);
            """, "2 4\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void DirectSuperCallsPreserveLexicalDispatchAcrossSuspension(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
