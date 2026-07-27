using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for Phase 3h URL-migration blocker #2: a bare <c>return;</c>
/// inside a void function compiled to <c>ldloca/initobj/ldloc/ret</c> —
/// loading a default value of type <c>void</c>. CLR verifier rejected the
/// method with "Common Language Runtime detected an invalid program."
/// </summary>
/// <remarks>
/// The void branch in <c>EmitReturn</c> fell through to the generic
/// value-type default path, because <c>typeof(void).IsValueType</c> is true.
/// Fix: explicitly skip value emission when the return type is <c>void</c>.
/// </remarks>
public class VoidFunctionEarlyReturnTests
{
    [Theory, ModeData]
    public void EarlyReturn_InVoidFunction_WithInterfaceParam(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                interface Rec { path: string[]; }
                function shorten(r: Rec): void {
                    if (r.path.length === 0) return;
                    r.path.pop();
                }
                const r: Rec = { path: ['a', 'b', 'c'] };
                shorten(r);
                console.log(r.path.length + ' ' + r.path[0] + ' ' + r.path[1]);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("2 a b\n", output);
    }

    [Theory, ModeData]
    public void EarlyReturn_InVoidFunction_WithArrayParam(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                function ensureHead(arr: string[]): void {
                    if (arr.length === 0) return;
                    arr[0] = 'first';
                }
                const a: string[] = ['x', 'y'];
                ensureHead(a);
                console.log(a[0] + ' ' + a[1]);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("first y\n", output);
    }
}
