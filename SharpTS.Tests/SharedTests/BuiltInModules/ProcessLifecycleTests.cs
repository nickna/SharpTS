using SharpTS.Runtime.BuiltIns;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Lifecycle, signal and warning event tests for the process object
/// (epic #1078: #1080 beforeExit/exit/unhandledRejection/warning, #1081
/// signals + kill). Serialized: these register listeners on the shared
/// process singleton; state is reset before every run.
/// </summary>
[Collection("ProcessLifecycleTests")]
public class ProcessLifecycleTests
{
    public ProcessLifecycleTests()
    {
        // Interpreter-side process state is process-wide; isolate each test.
        ProcessBuiltIns.ResetProcessState();
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_ExitEvent_FiresAtNaturalEnd(ExecutionMode mode)
    {
        var source = """
            process.on('exit', (code: number) => { console.log('exit', code); });
            console.log('main');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("main\nexit 0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_BeforeExit_FiresBeforeExit(ExecutionMode mode)
    {
        var source = """
            process.on('beforeExit', (code: number) => { console.log('beforeExit', code); });
            process.on('exit', (code: number) => { console.log('exit', code); });
            console.log('main');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("main\nbeforeExit 0\nexit 0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_BeforeExit_ListenerCanScheduleMoreWork(ExecutionMode mode)
    {
        // A beforeExit listener scheduling async work re-enters the loop and
        // beforeExit fires again after the next drain (Node semantics).
        var source = """
            let rounds = 0;
            process.on('beforeExit', () => {
                rounds++;
                if (rounds === 1) {
                    setTimeout(() => { console.log('extra work'); }, 1);
                }
            });
            process.on('exit', () => { console.log('exit after', rounds); });
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("extra work\nexit after 2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_Warning_EventCarriesNameMessageAndCode(ExecutionMode mode)
    {
        var source = """
            process.on('warning', (warning: any) => {
                console.log(warning.name, warning.message, warning.code);
            });
            process.emitWarning('boom', { type: 'CustomWarning', code: 'W123' });
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("CustomWarning boom W123\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_NoDeprecation_SuppressesDeprecationWarnings(ExecutionMode mode)
    {
        var source = """
            process.noDeprecation = true;
            let fired = false;
            process.on('warning', () => { fired = true; });
            process.emitWarning('old api', 'DeprecationWarning');
            setTimeout(() => { console.log('fired:', fired); }, 10);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("fired: false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Process_UnhandledRejection_And_RejectionHandled(ExecutionMode mode)
    {
        // Compiled-mode promise rejection tracking is a documented deferral
        // (epic #1078) — interpreted-only until the $Promise emitter grows
        // handler bookkeeping.
        var source = """
            let captured: any = null;
            process.on('unhandledRejection', (reason: any, promise: any) => {
                console.log('unhandled:', reason);
                captured = promise;
            });
            process.on('rejectionHandled', () => { console.log('handled'); });
            setTimeout(async () => { throw 'boom'; }, 1);
            setTimeout(() => { if (captured) { captured.catch(() => {}); } }, 60);
            setTimeout(() => { console.log('end'); }, 140);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("unhandled: boom\nhandled\nend\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_Kill_SelfSignal_DispatchesToListener(ExecutionMode mode)
    {
        // NOTE: no process.exit() here — the harness runs guest code in-process,
        // so Environment.Exit would take down the test host. The parked timer
        // is cleared by the listener instead, letting the loop drain naturally.
        var source = """
            const parked = setTimeout(() => { console.log('never'); }, 5000);
            process.on('SIGINT', (signal: string) => {
                console.log('got', signal);
                clearTimeout(parked);
            });
            process.kill(process.pid, 'SIGINT');
            console.log('after kill');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("after kill\ngot SIGINT\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_Kill_SignalZero_ExistenceCheck(ExecutionMode mode)
    {
        var source = """
            console.log(process.kill(process.pid, 0));
            let threw = false;
            try {
                process.kill(999999999, 0);
            } catch (e: any) {
                threw = true;
            }
            console.log('nonexistent threw:', threw);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nnonexistent threw: true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Process_ExitEvent_FiresOnProcessExitThroughModuleListener(ExecutionMode mode)
    {
        // Listener registered through the module surface fires for the global
        // process.exit — one emitter across surfaces (#1079).
        var source = """
            import { on } from 'process';
            on('exit', (code: number) => { console.log('module exit', code); });
            console.log('main');
            """;

        var output = TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode);
        Assert.Equal("main\nmodule exit 0\n", output);
    }
}
