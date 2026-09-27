using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericClassStaticFieldValueTests
{
    [Theory, ModeData]
    public void AliasReadsAndWritesTheSameStaticField(ExecutionMode mode)
    {
        const string source = """
            class Box<T> { static count: number = 4; }
            const Alias = Box;
            console.log((Alias as any).count);
            (Alias as any).count = 9;
            console.log(Box.count);
            console.log((Alias as any).count);
            """;
        Assert.Equal("4\n9\n9\n", TestHarness.Run(source, mode));
    }
    [Theory, ModeData]
    public void DirectWritesAreVisibleThroughAliases(ExecutionMode mode)
    {
        const string source = """
            class Box<T, U> { static count: number = 4; }
            const Alias = Box;
            Box.count = 6;
            console.log((Alias as any).count);
            (Alias as any).count = 8;
            Box.count++;
            console.log((Alias as any).count);
            """;
        Assert.Equal("6\n9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedWriteShadowsWithoutMutatingBase(ExecutionMode mode)
    {
        const string source = """
            class Base<T> { static count: number = 4; }
            class Derived<T> extends Base<T> {}
            const Alias = Derived;
            console.log((Alias as any).count);
            (Alias as any).count = 9;
            console.log(Derived.count);
            console.log(Base.count);
            console.log((Alias as any).count);
            """;
        Assert.Equal("4\n9\n4\n9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DistinctExpressionsKeepIndependentStaticFields(ExecutionMode mode)
    {
        const string source = """
            const Left = class Same<T> { static count: number = 1; };
            const Right = class Same<T> { static count: number = 2; };
            const a: any = Left;
            const b: any = Right;
            console.log(a.count);
            console.log(b.count);
            a.count = 7;
            console.log(a.count);
            console.log(b.count);
            """;
        Assert.Equal("1\n2\n7\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SubclassCanAssignProtectedGenericStaticField(ExecutionMode mode)
    {
        const string source = """
            class Base<T> { protected static count: number = 4; }
            class Derived<T> extends Base<T> {
                static update(): number { Derived.count = 8; return Derived.count; }
            }
            console.log(Derived.update());
            """;
        Assert.Equal("8\n", TestHarness.Run(source, mode));
    }
}
