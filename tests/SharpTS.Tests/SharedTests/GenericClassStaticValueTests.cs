using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericClassStaticValueTests
{
    [Theory, ModeData]
    public void ExpressionStaticMethodThroughAlias(ExecutionMode mode)
    {
        const string source = """
            const Box = class Unique<T> { static read(): number { return 7; } };
            const Alias = Box;
            console.log(Alias.read());
            """;
        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }
    [Theory, ModeData]
    public void DeclarationStaticAliasSharesState(ExecutionMode mode)
    {
        const string source = """
            class Box<T, U> {
                static count: number = 0;
                static next(): number { this.count++; return this.count; }
            }
            const Alias = Box;
            console.log(Alias.next());
            console.log(Box.next());
            console.log(Alias.next());
            console.log(Alias === Box);
            """;
        Assert.Equal("1\n2\n3\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GenericExpressionOwnersKeepStaticMethodsSeparate(ExecutionMode mode)
    {
        const string source = """
            const Left = class Same<T, U> { static read(): number { return 1; } };
            const Right = class Same<T, U> { static read(): number { return 2; } };
            const a = Left.read;
            const b = Right.read;
            console.log(a());
            console.log(b());
            console.log(Left === Right);
            """;
        Assert.Equal("1\n2\nfalse\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedStaticMethodThroughGenericAlias(ExecutionMode mode)
    {
        const string source = """
            class Base<T> { static read(): number { return 9; } }
            class Derived<T> extends Base<T> {}
            const Alias = Derived;
            console.log(Alias.read());
            """;
        Assert.Equal("9\n", TestHarness.Run(source, mode));
    }    [Theory, ModeData]
    public void OwnDescriptorStillShadowsGenericStaticMethod(ExecutionMode mode)
    {
        const string source = """
            const Box = class Unique<T> { static read(): number { return 7; } };
            const Alias = Box;
            console.log(Alias.read());
            Object.defineProperty(Box, "read", { value: () => 11, configurable: true });
            console.log(Alias.read());
            console.log(Alias === Box);
            console.log((Alias as any).missing === undefined);
            console.log((Alias as any).name);
            """;
        Assert.Equal("7\n11\ntrue\ntrue\nUnique\n", TestHarness.Run(source, mode));
    }}
