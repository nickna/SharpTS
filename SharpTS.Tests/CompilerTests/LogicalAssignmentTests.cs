using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class LogicalAssignmentTests
{
    #region &&= Operator

    [Fact]
    public void AndAssign_TruthyValue_AssignsNewValue()
    {
        var source = """
            let x: any = 5;
            x &&= 10;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void AndAssign_FalsyValue_KeepsOriginal()
    {
        var source = """
            let x: any = 0;
            x &&= 10;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void AndAssign_NullValue_KeepsNull()
    {
        var source = """
            let x: any = null;
            x &&= 10;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null\n", output);
    }

    [Fact]
    public void AndAssign_ShortCircuit_DoesNotEvaluateRight()
    {
        var source = """
            let sideEffect = false;
            function setValue(): number { sideEffect = true; return 10; }
            let x: any = 0;
            x &&= setValue();
            console.log(x);
            console.log(sideEffect);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\nfalse\n", output);
    }

    #endregion

    #region ||= Operator

    [Fact]
    public void OrAssign_FalsyValue_AssignsNewValue()
    {
        var source = """
            let x: any = 0;
            x ||= 10;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void OrAssign_TruthyValue_KeepsOriginal()
    {
        var source = """
            let x: any = 5;
            x ||= 10;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void OrAssign_NullValue_AssignsNewValue()
    {
        var source = """
            let x: any = null;
            x ||= 10;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void OrAssign_ShortCircuit_DoesNotEvaluateRight()
    {
        var source = """
            let sideEffect = false;
            function setValue(): number { sideEffect = true; return 10; }
            let x: any = 5;
            x ||= setValue();
            console.log(x);
            console.log(sideEffect);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\nfalse\n", output);
    }

    #endregion

    #region ??= Operator

    [Fact]
    public void NullishAssign_NullValue_AssignsNewValue()
    {
        var source = """
            let x: any = null;
            x ??= 10;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void NullishAssign_UndefinedValue_AssignsNewValue()
    {
        var source = """
            let x: any = undefined;
            x ??= 10;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void NullishAssign_ZeroValue_KeepsOriginal()
    {
        var source = """
            let x: any = 0;
            x ??= 10;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void NullishAssign_EmptyString_KeepsOriginal()
    {
        var source = """
            let x: any = "";
            x ??= "default";
            console.log(x === "");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NullishAssign_ShortCircuit_DoesNotEvaluateRight()
    {
        var source = """
            let sideEffect = false;
            function setValue(): number { sideEffect = true; return 10; }
            let x: any = 0;
            x ??= setValue();
            console.log(x);
            console.log(sideEffect);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\nfalse\n", output);
    }

    #endregion

    #region Object Property Access

    [Fact]
    public void AndAssign_ObjectProperty_Works()
    {
        var source = """
            let obj: any = { x: 5 };
            obj.x &&= 10;
            console.log(obj.x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void OrAssign_ObjectProperty_Works()
    {
        var source = """
            let obj: any = { x: 0 };
            obj.x ||= 10;
            console.log(obj.x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void NullishAssign_ObjectProperty_Works()
    {
        var source = """
            let obj: any = { x: null };
            obj.x ??= 10;
            console.log(obj.x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region Array Index Access

    [Fact]
    public void AndAssign_ArrayIndex_Works()
    {
        var source = """
            let arr: any[] = [5];
            arr[0] &&= 10;
            console.log(arr[0]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void OrAssign_ArrayIndex_Works()
    {
        var source = """
            let arr: any[] = [0];
            arr[0] ||= 10;
            console.log(arr[0]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void NullishAssign_ArrayIndex_Works()
    {
        var source = """
            let arr: any[] = [null];
            arr[0] ??= 10;
            console.log(arr[0]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region Return Value

    [Fact]
    public void LogicalAssign_ReturnsResultValue()
    {
        var source = """
            let x: any = null;
            let result = (x ??= 10);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void LogicalAssign_ReturnsOriginalWhenNotAssigned()
    {
        var source = """
            let x: any = 5;
            let result = (x ||= 10);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    #endregion
}
