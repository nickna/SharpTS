using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FrozenConstructorSymbolSetterTests
{
    [Theory, ModeData]
    public void PreserveIssue2630Behavior(ExecutionMode mode)
    {
        const string source = """
            for (const kind of ['preventExtensions', 'seal', 'freeze']) {
                const key = Symbol('value'), state = { value: 1, calls: 0, correct: false };
                class Owner {
                    static get [key]() { return state.value; }
                    static set [key](value: number) { state.value = value; state.calls++; state.correct = this === Owner; }
                }
                const owner: any = Owner;
                if (kind === 'freeze') Object.freeze(owner);
                else if (kind === 'seal') Object.seal(owner);
                else Object.preventExtensions(owner);
                owner[key] = 7;
                console.log(kind, state.value, state.calls, state.correct, owner[key]);
            }
            """;
        Assert.Equal("preventExtensions 7 1 true 7\nseal 7 1 true 7\nfreeze 7 1 true 7\n", TestHarness.Run(source, mode));
    }
}
