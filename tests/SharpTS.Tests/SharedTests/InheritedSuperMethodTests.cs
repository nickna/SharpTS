using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InheritedSuperMethodTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            class A { value(){return "grandparent";} } class B extends A {} class C extends B { read(){return super.value();} } console.log(new C().read());
            """, "grandparent\n" };
        yield return new object[] { "declared-parent-control", """
            class A {value(){return "grandparent";}}class B extends A {value(){return super.value();}}class C extends B {read(){return super.value();}}console.log(new C().read());
            """, "grandparent\n" };
        yield return new object[] { "deep-chain", """
            class A {value(x:number){return x+2;}}class B extends A {}class C extends B {}class D extends C {read(){return super.value(3);}}console.log(new D().read());
            """, "5\n" };
        yield return new object[] { "nearest-override", """
            class A {value(){return 1;}}class B extends A {value(){return 2;}}class C extends B {}class D extends C {read(){return super.value();}}console.log(new D().read());
            """, "2\n" };
        yield return new object[] { "receiver", """
            class A {label="parent";value(){return this.label;}}class B extends A {}class C extends B {label="child";read(){return super.value();}}console.log(new C().read());
            """, "child\n" };
        yield return new object[] { "protected", """
            class A {protected value(x:number){return x+2;}}class B extends A {}class C extends B {read(){return super.value(3);}}console.log(new C().read());
            """, "5\n" };
        yield return new object[] { "generic-grandparent", """
            class A<T> {value(x:T):T{return x;}}class B extends A<number> {}class C extends B {read():number{return super.value(3);}}console.log(new C().read());
            """, "3\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void SuperMethodsResolveThroughTheSuperclassChain(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
