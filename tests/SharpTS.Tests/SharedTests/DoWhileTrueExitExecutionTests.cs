using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DoWhileTrueExitExecutionTests
{
    [Theory, ModeData]
    public void CheckedBreakTypeMatchesTheValueAfterLaterIteration(ExecutionMode mode)
    {
        const string source = """
            let turns = 0;
            function stop(): boolean { return ++turns > 1; }
            let value: string | number = 'start';
            do {
                if (stop()) { value = 'done'; break; }
                value = 1;
            } while (true);
            const checked: string = value;
            console.log(checked);
            console.log(turns);
            """;
        Assert.Equal("done\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void LaterNumericGuardBreakUsesTheExecutedValue(ExecutionMode mode)
    {
        const string source = """
            let value: string | number = 'start';
            let turns = 0;
            do {
                turns++;
                if (typeof value === 'number') break;
                value = 1;
            } while (true);
            const checked: number = value;
            console.log(checked, turns);
            """;
        Assert.Equal("1 2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void LaterDiscriminantGuardBreakRetainsTheActualObject(ExecutionMode mode)
    {
        const string source = """
            type State = {kind:'text';text:string} | {kind:'count';count:number};
            let state: State = {kind:'text', text:'start'};
            let turns = 0;
            do {
                turns++;
                if (state.kind === 'count') break;
                state = {kind:'count', count:4};
            } while (true);
            const checked: number = state.count;
            console.log(checked, turns);
            """;
        Assert.Equal("4 2\n", TestHarness.Run(source, mode));
    }
}
