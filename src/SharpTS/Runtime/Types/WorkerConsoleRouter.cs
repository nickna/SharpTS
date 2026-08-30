using System.Text;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Routes Console output through an AsyncLocal override for compiled workers. Emitted IL writes
/// directly to <see cref="Console"/>, so a process-global Console.SetOut per worker would race
/// concurrent realms. The proxy is installed once and delegates to the writer associated with
/// the current execution context.
/// </summary>
internal static class WorkerConsoleRouter
{
    private static readonly AsyncLocal<TextWriter?> OutOverride = new();
    private static readonly AsyncLocal<TextWriter?> ErrorOverride = new();
    private static readonly object InstallLock = new();
    private static TextWriter? _installedOut;
    private static TextWriter? _installedError;

    internal static void EnsureInstalled()
    {
        lock (InstallLock)
        {
            // Test hosts and embedders legitimately replace Console.Out/Error between runs.
            // Reinstall only the side that was replaced, wrapping its current writer so
            // uncaptured parent output continues to flow to the host's latest destination.
            if (!ReferenceEquals(Console.Out, _installedOut))
            {
                var proxy = new ProxyWriter(Console.Out, OutOverride);
                Console.SetOut(proxy);
                _installedOut = Console.Out;
            }

            if (!ReferenceEquals(Console.Error, _installedError))
            {
                var proxy = new ProxyWriter(Console.Error, ErrorOverride);
                Console.SetError(proxy);
                _installedError = Console.Error;
            }
        }
    }

    internal static IDisposable PushOut(TextWriter writer)
    {
        EnsureInstalled();
        return Push(OutOverride, writer);
    }

    internal static IDisposable PushError(TextWriter writer)
    {
        EnsureInstalled();
        return Push(ErrorOverride, writer);
    }

    private static IDisposable Push(AsyncLocal<TextWriter?> slot, TextWriter writer)
    {
        var prior = slot.Value;
        slot.Value = writer;
        return new Scope(slot, prior);
    }

    private sealed class Scope(AsyncLocal<TextWriter?> slot, TextWriter? prior) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            slot.Value = prior;
        }
    }

    private sealed class ProxyWriter(TextWriter fallback, AsyncLocal<TextWriter?> slot) : TextWriter
    {
        private TextWriter Target => slot.Value ?? fallback;
        public override Encoding Encoding => fallback.Encoding;
        public override void Write(char value) => Target.Write(value);
        public override void Write(string? value) => Target.Write(value);
        public override void Write(char[] buffer, int index, int count) => Target.Write(buffer, index, count);
        public override void WriteLine() => Target.WriteLine();
        public override void WriteLine(string? value) => Target.WriteLine(value);
        public override void Flush() => Target.Flush();
    }
}
