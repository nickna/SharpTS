using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #728: a <c>for await...of</c> that carries a label
/// (<c>outer: for await (...)</c>) was iterated <em>synchronously</em> — the
/// <see cref="SharpTS.Parsing.Stmt.ForOf.IsAsync"/> flag never reached the async-iteration lowering on
/// the labeled path. It failed with "for...of requires an iterable" (interpreter) or a compile failure
/// ("Label N has not been marked", from the unconsumed reserved await states) in compiled mode, while
/// an unlabeled <c>for await</c> worked. The fix routes the labeled case through the same async lowering:
/// the interpreter registers an async <c>LabeledStatement</c> handler (and the async loops drain the
/// parked labels), and the compiled emitter's <c>EmitLabeledForOf</c> delegates to <c>EmitForAwaitOf</c>
/// when <c>IsAsync</c>, threading the label so <c>break</c>/<c>continue &lt;label&gt;</c> resolve.
/// </summary>
public class LabeledForAwaitTests
{
    private const string Gen = "async function* g() { yield 1; yield 2; yield 3; yield 4; }\n";

    [Theory, ModeData]
    public void LabeledForAwait_ContinueOuter(ExecutionMode mode)
    {
        // The exact #728 repro.
        var source = Gen + """
            async function main() {
              let sum = 0;
              outer: for await (const x of g()) {
                if (x === 2) continue outer;
                sum += x;
              }
              console.log(sum);
            }
            main();
            """;

        Assert.Equal("8\n", TestHarness.Run(source, mode));  // 1 + 3 + 4 (2 skipped)
    }

    [Theory, ModeData]
    public void LabeledForAwait_BreakOuter(ExecutionMode mode)
    {
        var source = Gen + """
            async function main() {
              let sum = 0;
              outer: for await (const x of g()) {
                if (x === 3) break outer;
                sum += x;
              }
              console.log(sum);
            }
            main();
            """;

        Assert.Equal("3\n", TestHarness.Run(source, mode));  // 1 + 2, then break at 3
    }

    [Theory, ModeData]
    public void LabeledForAwait_ContinueOuterFromInnerLoop(ExecutionMode mode)
    {
        // `continue outer` from a regular loop nested in the for-await body resumes the for-await.
        var source = Gen + """
            async function main() {
              let log = "";
              outer: for await (const x of g()) {
                for (let j = 0; j < 3; j++) {
                  if (j === 1) continue outer;
                  log += x + ":" + j + ",";
                }
              }
              console.log(log);
            }
            main();
            """;

        Assert.Equal("1:0,2:0,3:0,4:0,\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForAwaitAsInnerLoop_BreakToOuterFor(ExecutionMode mode)
    {
        // A for-await nested inside a labeled regular `for`; `break outer` from inside the for-await.
        var source = Gen + """
            async function main() {
              let log = "";
              outer: for (let i = 0; i < 3; i++) {
                for await (const x of g()) {
                  if (x === 2 && i === 1) break outer;
                  log += i + "/" + x + ",";
                }
              }
              console.log(log);
            }
            main();
            """;

        Assert.Equal("0/1,0/2,0/3,0/4,1/1,\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void LabeledForAwait_ProducesValidIL()
    {
        var source = Gen + """
            async function main() {
              let sum = 0;
              outer: for await (const x of g()) {
                if (x === 2) continue outer;
                if (x === 4) break outer;
                sum += x;
              }
              console.log(sum);
            }
            main();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("4\n", output);  // 1 + 3 (2 skipped, break at 4)
    }
}
