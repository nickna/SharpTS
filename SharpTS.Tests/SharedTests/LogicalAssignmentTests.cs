using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for logical assignment operators (&&=, ||=, ??=). Runs against both interpreter and compiler.
/// </summary>
public class LogicalAssignmentTests
{
    #region &&= Operator

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AndAssign_TruthyValue_AssignsNewValue(ExecutionMode mode)
    {
        var source = """
            let x: any = 5;
            x &&= 10;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AndAssign_FalsyValue_KeepsOriginal(ExecutionMode mode)
    {
        var source = """
            let x: any = 0;
            x &&= 10;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AndAssign_NullValue_KeepsNull(ExecutionMode mode)
    {
        var source = """
            let x: any = null;
            x &&= 10;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AndAssign_ShortCircuit_DoesNotEvaluateRight(ExecutionMode mode)
    {
        var source = """
            let sideEffect = false;
            function setValue(): number { sideEffect = true; return 10; }
            let x: any = 0;
            x &&= setValue();
            console.log(x);
            console.log(sideEffect);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\nfalse\n", output);
    }

    #endregion

    #region ||= Operator

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OrAssign_FalsyValue_AssignsNewValue(ExecutionMode mode)
    {
        var source = """
            let x: any = 0;
            x ||= 10;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OrAssign_TruthyValue_KeepsOriginal(ExecutionMode mode)
    {
        var source = """
            let x: any = 5;
            x ||= 10;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OrAssign_NullValue_AssignsNewValue(ExecutionMode mode)
    {
        var source = """
            let x: any = null;
            x ||= 10;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OrAssign_ShortCircuit_DoesNotEvaluateRight(ExecutionMode mode)
    {
        var source = """
            let sideEffect = false;
            function setValue(): number { sideEffect = true; return 10; }
            let x: any = 5;
            x ||= setValue();
            console.log(x);
            console.log(sideEffect);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\nfalse\n", output);
    }

    #endregion

    #region ??= Operator

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishAssign_NullValue_AssignsNewValue(ExecutionMode mode)
    {
        var source = """
            let x: any = null;
            x ??= 10;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishAssign_UndefinedValue_AssignsNewValue(ExecutionMode mode)
    {
        var source = """
            let x: any = undefined;
            x ??= 10;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishAssign_ZeroValue_KeepsOriginal(ExecutionMode mode)
    {
        var source = """
            let x: any = 0;
            x ??= 10;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishAssign_EmptyString_KeepsOriginal(ExecutionMode mode)
    {
        var source = """
            let x: any = "";
            x ??= "default";
            console.log(x === "");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishAssign_ShortCircuit_DoesNotEvaluateRight(ExecutionMode mode)
    {
        var source = """
            let sideEffect = false;
            function setValue(): number { sideEffect = true; return 10; }
            let x: any = 0;
            x ??= setValue();
            console.log(x);
            console.log(sideEffect);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\nfalse\n", output);
    }

    #endregion

    #region Object Property Access

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AndAssign_ObjectProperty_Works(ExecutionMode mode)
    {
        var source = """
            let obj: any = { x: 5 };
            obj.x &&= 10;
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OrAssign_ObjectProperty_Works(ExecutionMode mode)
    {
        var source = """
            let obj: any = { x: 0 };
            obj.x ||= 10;
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishAssign_ObjectProperty_Works(ExecutionMode mode)
    {
        var source = """
            let obj: any = { x: null };
            obj.x ??= 10;
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region Array Index Access

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AndAssign_ArrayIndex_Works(ExecutionMode mode)
    {
        var source = """
            let arr: any[] = [5];
            arr[0] &&= 10;
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OrAssign_ArrayIndex_Works(ExecutionMode mode)
    {
        var source = """
            let arr: any[] = [0];
            arr[0] ||= 10;
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishAssign_ArrayIndex_Works(ExecutionMode mode)
    {
        var source = """
            let arr: any[] = [null];
            arr[0] ??= 10;
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region Return Value

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LogicalAssign_ReturnsResultValue(ExecutionMode mode)
    {
        var source = """
            let x: any = null;
            let result = (x ??= 10);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LogicalAssign_ReturnsOriginalWhenNotAssigned(ExecutionMode mode)
    {
        var source = """
            let x: any = 5;
            let result = (x ||= 10);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    #endregion
}
