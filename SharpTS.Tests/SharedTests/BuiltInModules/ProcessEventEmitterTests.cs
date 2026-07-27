using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for process EventEmitter functionality (process.on, process.once, process.emit, etc.).
/// </summary>
public class ProcessEventEmitterTests
{
    [Theory, ModeData]
    public void Process_On_RegistersAndEmitsEvent(ExecutionMode mode)
    {
        var source = """
            let called = false;
            process.on('customEvent', () => {
                called = true;
            });
            process.emit('customEvent');
            console.log(called);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Process_On_PassesArguments(ExecutionMode mode)
    {
        var source = """
            let receivedCode = -1;
            process.on('testEvent', (code: number) => {
                receivedCode = code;
            });
            process.emit('testEvent', 42);
            console.log(receivedCode === 42);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Process_Once_CalledOnlyOnce(ExecutionMode mode)
    {
        var source = """
            let count = 0;
            process.once('singleEvent', () => {
                count++;
            });
            process.emit('singleEvent');
            process.emit('singleEvent');
            console.log(count === 1);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Process_Emit_ReturnsTrueWhenListenersExist(ExecutionMode mode)
    {
        var source = """
            process.on('hasListener', () => {});
            const result = process.emit('hasListener');
            console.log(result === true);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Process_Emit_ReturnsFalseWhenNoListeners(ExecutionMode mode)
    {
        var source = """
            const result = process.emit('noSuchEvent');
            console.log(result === false);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Process_RemoveListener_RemovesSpecificListener(ExecutionMode mode)
    {
        var source = """
            let count = 0;
            const listener = () => { count++; };
            process.on('removeMe', listener);
            process.emit('removeMe');
            process.removeListener('removeMe', listener);
            process.emit('removeMe');
            console.log(count === 1);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Process_ListenerCount_ReturnsCorrectCount(ExecutionMode mode)
    {
        var source = """
            process.on('counted', () => {});
            process.on('counted', () => {});
            console.log(process.listenerCount('counted') === 2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Process_EventNames_ReturnsRegisteredEvents(ExecutionMode mode)
    {
        var source = """
            process.on('eventA', () => {});
            process.on('eventB', () => {});
            const names = process.eventNames();
            console.log(Array.isArray(names));
            console.log(names.length >= 2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Process_RemoveAllListeners_ClearsAllForEvent(ExecutionMode mode)
    {
        var source = """
            process.on('clearMe', () => {});
            process.on('clearMe', () => {});
            process.removeAllListeners('clearMe');
            console.log(process.listenerCount('clearMe') === 0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Process_On_MethodChaining(ExecutionMode mode)
    {
        var source = """
            let a = false;
            let b = false;
            process
                .on('chainA', () => { a = true; })
                .on('chainB', () => { b = true; });
            process.emit('chainA');
            process.emit('chainB');
            console.log(a && b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Process_MultipleListeners_AllCalled(ExecutionMode mode)
    {
        var source = """
            let results: number[] = [];
            process.on('multi', () => { results.push(1); });
            process.on('multi', () => { results.push(2); });
            process.on('multi', () => { results.push(3); });
            process.emit('multi');
            console.log(results.length === 3);
            console.log(results[0] === 1);
            console.log(results[1] === 2);
            console.log(results[2] === 3);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Process_StillHasOriginalProperties(ExecutionMode mode)
    {
        // Ensure EventEmitter integration doesn't break existing process properties
        var source = """
            console.log(typeof process.platform === 'string');
            console.log(typeof process.pid === 'number');
            console.log(process.cwd().length > 0);
            console.log(process.on !== undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }
}
