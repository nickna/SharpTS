using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DerivedEarlyObjectReturnTests
{
    [Theory, ModeData]
    public void ReplacementBeforeSuperSkipsParentEffects(ExecutionMode mode)
    {
        const string source = """
            const replacement: any = {};
            let effects = 0;
            class Base { constructor() { effects++; } }
            class C extends Base {
                constructor(replace: boolean) {
                    if (replace) return replacement;
                    super();
                }
            }
            console.log(new C(true) === replacement, effects);
            console.log(new C(false) instanceof C, effects);
            const D = class extends Base {
                constructor() { return replacement; }
            };
            console.log(new D() === replacement, effects);
            """;
        Assert.Equal("true 0\ntrue 1\ntrue 1\n", TestHarness.Run(source, mode));
    }
}
