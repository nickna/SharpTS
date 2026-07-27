using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// An instance's <c>constructor</c> property resolves to its class in both modes.
/// In compiled mode a class in value position is its <see cref="System.Type"/>, so
/// <c>x.constructor</c> must return that same value: identity (<c>=== MyClass</c>),
/// static-member reads (<c>x.constructor.staticProp</c>), and <c>.name</c> all work.
///
/// <para>Compiled mode previously returned <c>undefined</c> for <c>instance.constructor</c>.
/// That was benign only while member reads were lenient; once #701 made a read on
/// <c>undefined</c> throw, the gap surfaced (e.g. the <c>yaml</c> package reads
/// <c>coll.constructor.tagName</c>). Fixed by emitting a <c>constructor</c> branch in
/// each class's GetProperty body.</para>
/// </summary>
public class InstanceConstructorTests
{
    [Theory, ModeData]
    public void InstanceConstructor_IsTheClass(ExecutionMode mode)
    {
        var source = """
            class Animal {}
            const a = new Animal();
            console.log(a.constructor === Animal);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InstanceConstructor_Name(ExecutionMode mode)
    {
        var source = """
            class Animal {}
            const a = new Animal();
            console.log((a.constructor as any).name);
            """;
        Assert.Equal("Animal\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InstanceConstructor_ReadsStaticMember(ExecutionMode mode)
    {
        var source = """
            class Animal { static kind = "mammal"; }
            const a = new Animal();
            console.log((a.constructor as any).kind);
            """;
        Assert.Equal("mammal\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InstanceConstructor_SubclassResolvesToSubclass(ExecutionMode mode)
    {
        // The most-derived class is reported — each class's GetProperty returns its
        // own Type, mirroring `(new Sub()).constructor === Sub` in JS.
        var source = """
            class Base {}
            class Sub extends Base {}
            const s = new Sub();
            console.log(s.constructor === Sub, s.constructor === Base);
            """;
        Assert.Equal("true false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InstanceConstructor_OwnDataPropertyShadows(ExecutionMode mode)
    {
        // An own data property named `constructor` shadows the class (JS semantics):
        // the own-field lookup runs before the constructor branch.
        var source = """
            class Animal {}
            const a: any = new Animal();
            a.constructor = "shadowed";
            console.log(a.constructor);
            """;
        Assert.Equal("shadowed\n", TestHarness.Run(source, mode));
    }
}
