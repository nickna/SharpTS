using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class AsyncLexicalSuperTests
{
    [Fact]
    public void AsyncArrowBindsReceiverBeforeItsFirstSuspension()
    {
        const string source = """
            let parents = 0, fields = 0;
            const C = class extends class { constructor() { parents++; } } {
                value = ++fields;
                constructor() {
                    const initialize = async () => {
                        const receiver = super();
                        console.log(receiver === this, this.value, new.target === C);
                        await Promise.resolve();
                        try { super(); } catch (error) { console.log(error instanceof ReferenceError, parents, fields); }
                    };
                    initialize();
                    console.log(this.value);
                }
            };
            new C();
            """;

        Assert.Equal("true 1 true\n1\ntrue 2 1\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void SuperArgumentsMaySuspendBeforeTheReceiverIsBound()
    {
        const string source = """
            let pending;
            const First = class { constructor(value) { this.base = value; } };
            const replacement = {};
            const C = class extends First {
                value = 7;
                constructor() {
                    const initialize = async () => super(await Promise.resolve(2));
                    pending = initialize();
                    return replacement;
                }
            };
            console.log(new C() === replacement);
            pending.then(receiver => console.log(receiver.base, receiver.value));
            """;

        Assert.Equal("true\n2 7\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }
}
