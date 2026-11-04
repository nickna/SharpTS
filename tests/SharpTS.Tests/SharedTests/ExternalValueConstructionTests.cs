using SharpTS.Tests.Infrastructure;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ExternalValueConstructionTests
{
    [Theory, ModeData]
    public void StructConstructorResultCanEnterGuestObjectStorage(ExecutionMode mode)
    {
        Assert.Equal("1 2\n1 2\n", TestHarness.Run("""
            @DotNetType("System.Numerics.Vector2")
            declare class Vector {
                constructor(x: number, y: number);
                x: number;
                y: number;
            }
            const value: Vector = new Vector(1, 2);
            console.log(value.x, value.y);
            const copied: Vector = value;
            console.log(copied.x, copied.y);
            """, mode, DecoratorMode.Legacy));
    }
}
