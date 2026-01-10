using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EnumTests
{
    // Numeric Enums
    [Fact]
    public void NumericEnum_ForwardMapping_ReturnsValue()
    {
        var source = """
            enum Direction {
                Up,
                Down,
                Left,
                Right
            }
            console.log(Direction.Up);
            console.log(Direction.Down);
            console.log(Direction.Left);
            console.log(Direction.Right);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n1\n2\n3\n", output);
    }

    [Fact]
    public void NumericEnum_ReverseMapping_ReturnsName()
    {
        var source = """
            enum Direction {
                Up,
                Down,
                Left,
                Right
            }
            console.log(Direction[0]);
            console.log(Direction[1]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Up\nDown\n", output);
    }

    [Fact]
    public void NumericEnum_CustomStart_AutoIncrements()
    {
        var source = """
            enum Direction {
                Up = 10,
                Down,
                Left,
                Right
            }
            console.log(Direction.Up);
            console.log(Direction.Down);
            console.log(Direction.Left);
            console.log(Direction.Right);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n11\n12\n13\n", output);
    }

    [Fact]
    public void NumericEnum_ExplicitValues_SetsCorrectly()
    {
        var source = """
            enum HttpStatus {
                OK = 200,
                NotFound = 404,
                ServerError = 500
            }
            console.log(HttpStatus.OK);
            console.log(HttpStatus.NotFound);
            console.log(HttpStatus.ServerError);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("200\n404\n500\n", output);
    }

    [Fact]
    public void NumericEnum_VariableAssignment_Works()
    {
        var source = """
            enum Direction {
                Up,
                Down
            }
            let d: Direction = Direction.Up;
            console.log(d);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void NumericEnum_InConditional_ComparesCorrectly()
    {
        var source = """
            enum Direction {
                Up,
                Down
            }
            let d: Direction = Direction.Up;
            if (d == Direction.Up) {
                console.log("going up");
            } else {
                console.log("going down");
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("going up\n", output);
    }

    // String Enums
    [Fact]
    public void StringEnum_ForwardMapping_ReturnsValue()
    {
        var source = """
            enum Status {
                Success = "success",
                Error = "error",
                Pending = "pending"
            }
            console.log(Status.Success);
            console.log(Status.Error);
            console.log(Status.Pending);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("success\nerror\npending\n", output);
    }

    [Fact]
    public void StringEnum_VariableAssignment_Works()
    {
        var source = """
            enum Status {
                Success = "success",
                Error = "error"
            }
            let s: Status = Status.Success;
            console.log(s);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("success\n", output);
    }

    [Fact]
    public void StringEnum_InConditional_ComparesCorrectly()
    {
        var source = """
            enum Status {
                Success = "success",
                Error = "error"
            }
            let s: Status = Status.Error;
            if (s == Status.Error) {
                console.log("failed");
            } else {
                console.log("ok");
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("failed\n", output);
    }

    // Heterogeneous Enums
    [Fact]
    public void HeterogeneousEnum_MixedValues_Works()
    {
        var source = """
            enum Mixed {
                No = 0,
                Yes = "yes"
            }
            console.log(Mixed.No);
            console.log(Mixed.Yes);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\nyes\n", output);
    }

    [Fact]
    public void HeterogeneousEnum_NumericReverseMapping_Works()
    {
        var source = """
            enum Mixed {
                No = 0,
                Yes = "yes"
            }
            console.log(Mixed[0]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("No\n", output);
    }

    [Fact]
    public void Enum_UsedInSwitch_Works()
    {
        var source = """
            enum Color {
                Red,
                Green,
                Blue
            }
            let c: Color = Color.Green;
            switch (c) {
                case Color.Red:
                    console.log("red");
                    break;
                case Color.Green:
                    console.log("green");
                    break;
                case Color.Blue:
                    console.log("blue");
                    break;
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("green\n", output);
    }

    // Const Enums
    [Fact]
    public void ConstEnum_NumericForwardMapping_ReturnsValue()
    {
        var source = """
            const enum Direction {
                Up,
                Down,
                Left,
                Right
            }
            console.log(Direction.Up);
            console.log(Direction.Down);
            console.log(Direction.Left);
            console.log(Direction.Right);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n1\n2\n3\n", output);
    }

    [Fact]
    public void ConstEnum_NumericAutoIncrement_Works()
    {
        var source = """
            const enum Priority {
                Low = 1,
                Medium,
                High
            }
            console.log(Priority.Low);
            console.log(Priority.Medium);
            console.log(Priority.High);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void ConstEnum_NumericCustomValues_Works()
    {
        var source = """
            const enum HttpStatus {
                OK = 200,
                NotFound = 404,
                ServerError = 500
            }
            console.log(HttpStatus.OK);
            console.log(HttpStatus.NotFound);
            console.log(HttpStatus.ServerError);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("200\n404\n500\n", output);
    }

    [Fact]
    public void ConstEnum_StringForwardMapping_Works()
    {
        var source = """
            const enum Status {
                Success = "success",
                Error = "error",
                Pending = "pending"
            }
            console.log(Status.Success);
            console.log(Status.Error);
            console.log(Status.Pending);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("success\nerror\npending\n", output);
    }

    [Fact]
    public void ConstEnum_ComputedMemberValue_Works()
    {
        var source = """
            const enum Values {
                A = 1,
                B = Values.A * 2,
                C = Values.B + 10,
                D = Values.A | Values.B
            }
            console.log(Values.A);
            console.log(Values.B);
            console.log(Values.C);
            console.log(Values.D);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n12\n3\n", output);
    }

    [Fact]
    public void ConstEnum_VariableAssignment_Works()
    {
        var source = """
            const enum Direction {
                Up,
                Down
            }
            let d: Direction = Direction.Up;
            console.log(d);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void ConstEnum_ConditionalComparison_Works()
    {
        var source = """
            const enum Direction {
                Up,
                Down
            }
            let d: Direction = Direction.Up;
            if (d == Direction.Up) {
                console.log("going up");
            } else {
                console.log("going down");
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("going up\n", output);
    }

    [Fact]
    public void ConstEnum_SwitchStatement_Works()
    {
        var source = """
            const enum Color {
                Red,
                Green,
                Blue
            }
            let c: Color = Color.Green;
            switch (c) {
                case Color.Red:
                    console.log("red");
                    break;
                case Color.Green:
                    console.log("green");
                    break;
                case Color.Blue:
                    console.log("blue");
                    break;
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("green\n", output);
    }

    [Fact]
    public void ConstEnum_ReverseMapping_ThrowsTypeError()
    {
        var source = """
            const enum Direction {
                Up,
                Down
            }
            console.log(Direction[0]);
            """;

        // Should fail at type-checking phase
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("const enum", ex.Message);
    }
}
