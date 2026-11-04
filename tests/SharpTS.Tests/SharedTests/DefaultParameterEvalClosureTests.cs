using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class DefaultParameterEvalClosureTests
{
    [Theory, ModeData]
    public void EvalIntroducedParameterBindingIsSharedByAllClosures(ExecutionMode mode)
    {
        Assert.Equal("inside inside inside outside\n", TestHarness.RunUnchecked("""
            var x = 'outside';
            var probe1, probe2, probeBody;
            ((_ = (eval('var x = "inside";'), probe1 = function() { return x; }),
              __ = probe2 = function() { return x; }) => {
                probeBody = function() { return x; };
            })();
            console.log(probe1(), probe2(), probeBody(), x);
            """, mode));
    }
}
