using SharpTS.Runtime;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// process.exit(), process.abort(), and untrapped fatal-signal defaults route
/// through <see cref="ProcessControl"/> so an embedder (this test host included)
/// can intercept guest-initiated termination — previously library code called
/// Environment.Exit / Environment.FailFast directly, so any guest script doing
/// process.exit() took down the whole host, and process.abort() additionally
/// wrote a crash dump. Handler swaps are process-wide: tests in this class run
/// serially (same class) and restore the defaults in finally; no other test can
/// legitimately trigger these paths (before this change it would have died).
/// </summary>
public class ProcessControlHookTests
{
    [Fact]
    public void ProcessExit_RoutesThroughExitHandler()
    {
        var original = ProcessControl.ExitHandler;
        int? captured = null;
        try
        {
            ProcessControl.ExitHandler = code => captured = code;
            var output = TestHarness.RunInterpreted("""
                process.exit(7);
                console.log("after-exit");
                """);
            Assert.Equal(7, captured);
            // With a handler that returns, the guest continues — that documents
            // the embedder contract: throw from the handler to unwind instead.
            Assert.Contains("after-exit", output);
        }
        finally
        {
            ProcessControl.ExitHandler = original;
            // process.exit publishes the code to Environment.ExitCode BEFORE the
            // handler runs; with an intercepting handler the process survives, so
            // the code would leak into every later test that reads it (the
            // ProcessLifecycleTests 'exit 0' events read Environment.ExitCode).
            Environment.ExitCode = 0;
        }
    }

    [Fact]
    public void ProcessAbort_RoutesThroughAbortHandler()
    {
        var original = ProcessControl.AbortHandler;
        string? captured = null;
        try
        {
            ProcessControl.AbortHandler = message => captured = message;
            TestHarness.RunInterpreted("process.abort();");
            Assert.Equal("process.abort() called", captured);
        }
        finally { ProcessControl.AbortHandler = original; }
    }

    [Fact]
    public void SelfSignal_DefaultAction_RoutesThroughExitHandler()
    {
        var original = ProcessControl.ExitHandler;
        int? captured = null;
        try
        {
            ProcessControl.ExitHandler = code => captured = code;
            // SIGTERM = 15; Node's default action exits with 128 + signal.
            TestHarness.RunInterpreted("process.kill(process.pid, 'SIGTERM');");
            Assert.Equal(128 + 15, captured);
        }
        finally { ProcessControl.ExitHandler = original; }
    }
}
