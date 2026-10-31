using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DuplicateSuperBindingTests
{
    [Theory, ModeData]
    public void SecondSuperRunsParentEffectsBeforeRejectingBinding(ExecutionMode mode)
    {
        const string source = """
            const state = { trace: '' };
            class Base {
                value = (state.trace += 'f', 1);
                constructor() { state.trace += 'b'; }
            }
            class Derived extends Base {
                child = (state.trace += 'd', 1);
                constructor() {
                    super();
                    try { super(); console.log(false, state.trace); }
                    catch (error) { console.log(error instanceof ReferenceError, state.trace); }
                }
            }
            new Derived();
            """;
        Assert.Equal("true fbdfb\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SecondSuperDoesNotReplaceOriginalReceiver(ExecutionMode mode)
    {
        const string source = """
            let calls = 0;
            class Base { value = ++calls; }
            class Derived extends Base {
                constructor() {
                    super();
                    try { super(); } catch (error) { console.log(error instanceof ReferenceError); }
                    console.log(this.value, calls);
                }
            }
            new Derived();
            """;
        Assert.Equal("true\n1 2\n", TestHarness.Run(source, mode));
    }
}
