using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for a compiled-mode compiler bug surfaced by the Phase 3e
/// `assert` migration: a chained <c>||</c> of mixed <c>typeof</c>/<c>===</c>
/// comparisons, evaluated inside a function imported from another module,
/// evaluates truthy when each individual sub-expression is false.
/// </summary>
/// <remarks>
/// Regression note: the compiler bug is fixed (this test runs un-skipped in
/// both modes and passes). <c>stdlib/node/assert.ts</c>'s <c>deepEquals</c>
/// still uses the bind-each-sub-condition-to-a-local workaround from the
/// broken era; it is behavior-equivalent, so reverting it is optional.
/// </remarks>
public class CrossModuleChainedLogicalOrTests
{
    [Theory, ModeData]
    public void ChainedLogicalOr_WithTypeofAndNull_AcrossModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function isBothObjects(a: any, b: any): boolean {
                    // Each individual sub-expression is false for two arrays,
                    // so the `||` chain should be false. In compiled mode it
                    // evaluates truthy when lib is an imported module.
                    if (typeof a !== 'object' || a === null || typeof b !== 'object' || b === null) {
                        return false;
                    }
                    return true;
                }
                """,
            ["main.ts"] = """
                import { isBothObjects } from './lib';
                console.log(isBothObjects([1, 2, 3], [1, 2, 3]));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }
}
