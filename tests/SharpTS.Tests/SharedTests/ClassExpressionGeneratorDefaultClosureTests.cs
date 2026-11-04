using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ClassExpressionGeneratorDefaultClosureTests
{
    [Theory, ModeData]
    public void StaticGeneratorDefaultClosurePropagatesOriginalThrow(ExecutionMode mode)
    {
        Assert.Equal("true 0\ntrue 0\n", TestHarness.RunUnchecked("""
            const token = {};
            let calls = 0;
            const C = class {
                static *method(value = (function() { throw token; }())) { calls++; }
            };
            class D {
                static *method(value = (function() { throw token; }())) { calls++; }
            }
            try { C.method(); } catch (error) { console.log(error === token, calls); }
            try { D.method(); } catch (error) { console.log(error === token, calls); }
            """, mode));
    }
}
