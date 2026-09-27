using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class GenericStaticFieldDescriptorTests
{
    [Fact]
    public void NonWritableDescriptorWinsOverFieldStorage()
    {
        const string source = """
            class Box<T> { static count: number = 4; }
            const Alias: any = Box;
            Object.defineProperty(Box, "count", { value: 12, writable: false, configurable: true });
            Alias.count = 9;
            console.log(Alias.count);
            console.log(Alias === Box);
            """;
        Assert.Equal("12\ntrue\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void FrozenAliasCannotMutateStaticField()
    {
        const string source = """
            class Box<T> { static count: number = 4; }
            const Alias: any = Box;
            Object.freeze(Box);
            try { Alias.count = 9; } catch (e) {}
            console.log(Alias.count);
            console.log(Box.count);
            """;
        Assert.Equal("4\n4\n", TestHarness.RunCompiled(source));
    }

    [Theory]
    [InlineData("Object.freeze(Box)")]
    [InlineData("Object.defineProperty(Box, \"count\", { value: 4, writable: false })")]
    public void StrictAliasRejectsReadOnlyField(string prepare)
    {
        string source = """
            "use strict";
            class Box<T> { static count: number = 4; }
            const Alias: any = Box;
            """ + prepare + ";" + """
            try { Alias.count = 9; } catch (e) { console.log("rejected"); }
            console.log(Alias.count);
            """;
        Assert.Equal("rejected\n4\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void StrictSealedOwnFieldRemainsWritableButInheritedFieldCannotShadow()
    {
        const string source = """
            "use strict";
            class Base<T> { static count: number = 4; }
            class Derived<T> extends Base<T> {}
            const a: any = Base;
            const b: any = Derived;
            Object.seal(Base);
            Object.seal(Derived);
            a.count = 6;
            try { b.count = 9; } catch (e) { console.log("rejected"); }
            console.log(a.count);
            console.log(b.count);
            """;
        Assert.Equal("rejected\n6\n6\n", TestHarness.RunCompiled(source));
    }

}
