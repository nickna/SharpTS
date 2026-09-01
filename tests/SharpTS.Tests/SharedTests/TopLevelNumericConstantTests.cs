using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Semantic parity coverage for module-level numeric bindings whose compiled representation may
/// propagate an immutable literal into function bodies.
/// </summary>
public sealed class TopLevelNumericConstantTests
{
    [Theory, ModeData]
    public void NumericConstantReadBeforeInitialization_ThrowsReferenceError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                invokeBeforeInitialization();
                const MODULUS: number = 7;

                function kernel(value: number): number {
                    return value % MODULUS;
                }

                function invokeBeforeInitialization(): void {
                    try {
                        kernel(15);
                        console.log(false);
                    } catch (error) {
                        console.log(error instanceof ReferenceError);
                    }
                }

                console.log(kernel(15));
                """
        };

        Assert.Equal("true\n1\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void MutableTopLevelNumber_RemainsLiveAcrossFunctionCalls(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["model.ts"] = """
                let modulus: number = 7;
                export function kernel(value: number): number { return value % modulus; }
                export function update(value: number): void { modulus = value; }
                """,
            ["main.ts"] = """
                import { kernel, update } from './model';
                console.log(kernel(15));
                update(5);
                console.log(kernel(15));
                """
        };

        Assert.Equal("1\n0\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}
