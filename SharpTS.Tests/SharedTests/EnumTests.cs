using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for enum declarations (numeric, string, const enums). Runs against both interpreter and compiler.
/// </summary>
public class EnumTests
{
    #region Numeric Enums

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericEnum_ForwardMapping_ReturnsValue(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericEnum_ReverseMapping_ReturnsName(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Up\nDown\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericEnum_CustomStart_AutoIncrements(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n11\n12\n13\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericEnum_ExplicitValues_SetsCorrectly(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("200\n404\n500\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericEnum_VariableAssignment_Works(ExecutionMode mode)
    {
        var source = """
            enum Direction {
                Up,
                Down
            }
            let d: Direction = Direction.Up;
            console.log(d);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericEnum_InConditional_ComparesCorrectly(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("going up\n", output);
    }

    #endregion

    #region String Enums

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringEnum_ForwardMapping_ReturnsValue(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("success\nerror\npending\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringEnum_VariableAssignment_Works(ExecutionMode mode)
    {
        var source = """
            enum Status {
                Success = "success",
                Error = "error"
            }
            let s: Status = Status.Success;
            console.log(s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("success\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StringEnum_InConditional_ComparesCorrectly(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("failed\n", output);
    }

    #endregion

    #region Heterogeneous Enums

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void HeterogeneousEnum_MixedValues_Works(ExecutionMode mode)
    {
        var source = """
            enum Mixed {
                No = 0,
                Yes = "yes"
            }
            console.log(Mixed.No);
            console.log(Mixed.Yes);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\nyes\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void HeterogeneousEnum_NumericReverseMapping_Works(ExecutionMode mode)
    {
        var source = """
            enum Mixed {
                No = 0,
                Yes = "yes"
            }
            console.log(Mixed[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("No\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Enum_UsedInSwitch_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("green\n", output);
    }

    #endregion

    #region Const Enums

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConstEnum_NumericForwardMapping_ReturnsValue(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConstEnum_NumericAutoIncrement_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConstEnum_NumericCustomValues_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("200\n404\n500\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConstEnum_StringForwardMapping_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("success\nerror\npending\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConstEnum_ComputedMemberValue_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n12\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConstEnum_VariableAssignment_Works(ExecutionMode mode)
    {
        var source = """
            const enum Direction {
                Up,
                Down
            }
            let d: Direction = Direction.Up;
            console.log(d);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConstEnum_ConditionalComparison_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("going up\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConstEnum_SwitchStatement_Works(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("green\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ConstEnum_ReverseMapping_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            const enum Direction {
                Up,
                Down
            }
            console.log(Direction[0]);
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("const enum", ex.Message);
    }

    #endregion
}
