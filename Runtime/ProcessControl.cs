namespace SharpTS.Runtime;

/// <summary>
/// Central exit point for guest-initiated process termination: process.exit(),
/// an untrapped fatal signal's default action, process.abort(), and a numeric
/// return from the main() convention. Library code must route through this
/// instead of calling Environment.Exit / Environment.FailFast directly so that
/// embedders (the test host, REPL, LSP server) can intercept — a guest script
/// ending itself must not take down the host unless the host chose that.
/// Defaults preserve CLI behavior: real Environment.Exit / Environment.FailFast.
/// </summary>
public static class ProcessControl
{
    /// <summary>
    /// Action invoked for a normal guest-requested exit. Replace to intercept
    /// (e.g. throw a host-recognized exception); restore when done. The default
    /// terminates the process and never returns.
    /// </summary>
    public static Action<int> ExitHandler { get; set; } = Environment.Exit;

    /// <summary>
    /// Action invoked for process.abort(): abnormal termination — no 'exit'
    /// event, crash-dump semantics (Node raises SIGABRT). The default,
    /// Environment.FailFast, writes a crash dump and cannot be caught; hosts
    /// that must survive a guest abort replace it.
    /// </summary>
    public static Action<string> AbortHandler { get; set; } = static message => Environment.FailFast(message);

    public static void Exit(int code) => ExitHandler(code);

    public static void Abort(string message) => AbortHandler(message);
}
