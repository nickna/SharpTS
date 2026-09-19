using System.Collections.Immutable;

namespace SharpTS.Compilation;

/// <summary>
/// Fixed signal names and numbers embedded by the process emitter. Catalog order
/// is part of emission order; these immutable values hold no generated metadata.
/// </summary>
internal static class ProcessSignalEmitConstants
{
    internal static ImmutableArray<string> TrappableSignals { get; } =
        ["SIGINT", "SIGTERM", "SIGHUP", "SIGQUIT", "SIGBREAK", "SIGWINCH"];

    /// <summary>Node signal name to conventional number (numeric kill and 128+n exits).</summary>
    internal static ImmutableArray<(string Name, int Number)> SignalNumbers { get; } =
    [
        ("SIGHUP", 1), ("SIGINT", 2), ("SIGQUIT", 3), ("SIGABRT", 6),
        ("SIGKILL", 9), ("SIGUSR1", 10), ("SIGUSR2", 12), ("SIGTERM", 15),
        ("SIGBREAK", 21), ("SIGWINCH", 28),
    ];
}
