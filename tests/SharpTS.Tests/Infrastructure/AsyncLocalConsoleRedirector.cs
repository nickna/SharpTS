using System.Text;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// AsyncLocal-aware shim around <see cref="Console.Out"/>, <see cref="Console.Error"/>,
/// and <see cref="Console.In"/>. Installed once per testhost via <see cref="Install"/>,
/// then individual tests scope their capture / redirection to their own logical-execution
/// context — no global lock, no race between parallel tests.
///
/// Why this exists: compiled-mode tests run in-process via <see cref="System.Reflection.Assembly.Load(byte[])"/>,
/// and the emitted IL writes directly to <see cref="Console.WriteLine"/>. Parallel tests
/// would clobber each other's output without context-scoped routing.
/// AsyncLocal flows across <c>await</c> and through <see cref="Task.Run(Action)"/>, so
/// captures persist through async test code.
///
/// This also subsumes the old <c>lock(TestHarness.ConsoleLock) + Console.SetOut(sw)</c>
/// pattern used by <c>LspBridgeTestHelper</c> and <c>DiagnosticReporterTests</c>: callers
/// now use <see cref="WithOut"/>/<see cref="WithErr"/>/<see cref="WithIn"/> to push a
/// per-context override without globally mutating the static <see cref="Console"/> state.
/// </summary>
internal static class AsyncLocalConsoleRedirector
{
    private static readonly AsyncLocal<TextWriter?> _outOverride = new();
    private static readonly AsyncLocal<TextWriter?> _errOverride = new();
    private static readonly AsyncLocal<TextReader?> _inOverride = new();

    private static bool _installed;
    private static readonly object _installLock = new();

    /// <summary>
    /// Installs proxy <see cref="Console.Out"/>/<see cref="Console.Error"/>/<see cref="Console.In"/>
    /// once per process. Idempotent.
    ///
    /// Critical: writes directly to the private <c>System.Console.s_out</c>/<c>s_error</c>/<c>s_in</c>
    /// fields rather than calling <see cref="Console.SetOut"/>. <see cref="Console.SetOut"/>
    /// unconditionally wraps the writer in <see cref="TextWriter.Synchronized"/> — that
    /// wrapper takes a lock on every <see cref="Console.WriteLine"/> call. Compiled-mode
    /// tests run in parallel under xUnit, and every emitted <c>console.log</c> would queue
    /// on that single global lock, capping testhost CPU at ~1 core regardless of how many
    /// collections xUnit dispatches in parallel (observed: 9% CPU on a 12-core box).
    ///
    /// Our <see cref="ProxyWriter"/> + <see cref="AsyncLocal{T}"/> design is already safe
    /// for concurrent writers: each test reads its own <c>_slot.Value</c> and writes to its
    /// own per-test <see cref="StringBuilderWriter"/>. There is no shared mutable state to
    /// guard, so the synchronizing wrapper is pure overhead.
    /// </summary>
    public static void Install()
    {
        if (_installed) return;
        lock (_installLock)
        {
            if (_installed) return;

            var outProxy = new ProxyWriter(Console.Out, _outOverride);
            // Compiled-mode tests assert on stdout only; mute stderr globally for tests
            // that don't explicitly capture it. Tests that do capture stderr push their
            // own writer via WithErr.
            var errProxy = new ProxyWriter(TextWriter.Null, _errOverride);
            var inProxy = new ProxyReader(Console.In, _inOverride);

            // Bypass Console.SetOut/SetError/SetIn — they wrap in Synchronized writers/readers.
            // We write directly to the static backing fields to avoid the lock.
            if (!TrySetConsoleField("s_out", outProxy) ||
                !TrySetConsoleField("s_error", errProxy) ||
                !TrySetConsoleField("s_in", inProxy))
            {
                // Field names changed in this runtime — fall back to the locked path.
                // Tests still work; just slower.
                Console.SetOut(outProxy);
                Console.SetError(errProxy);
                Console.SetIn(inProxy);
            }
            _installed = true;
        }
    }

    private static bool TrySetConsoleField(string fieldName, object proxy)
    {
        var field = typeof(Console).GetField(
            fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (field is null) return false;
        field.SetValue(null, proxy);
        return true;
    }

    /// <summary>
    /// Captures <see cref="Console.Out"/> for the current logical-execution context into
    /// a fresh <see cref="StringBuilder"/>. Dispose the returned scope to restore the prior
    /// override. The captured output is available via <see cref="CaptureScope.GetOutput"/>.
    /// </summary>
    public static CaptureScope Capture()
    {
        var sb = new StringBuilder();
        var sw = new StringBuilderWriter(sb);
        var prior = _outOverride.Value;
        _outOverride.Value = sw;
        return new CaptureScope(sb, prior, _outOverride);
    }

    /// <summary>
    /// Pushes <paramref name="writer"/> as the current context's <see cref="Console.Out"/>.
    /// Use <c>using</c> to scope the override. Multiple parallel tests can each push their
    /// own writer without colliding because the override is AsyncLocal.
    /// </summary>
    public static IDisposable WithOut(TextWriter writer) => Push(_outOverride, writer);
    public static IDisposable WithErr(TextWriter writer) => Push(_errOverride, writer);
    public static IDisposable WithIn(TextReader reader) => Push(_inOverride, reader);

    private static IDisposable Push<T>(AsyncLocal<T?> slot, T value) where T : class
    {
        var prior = slot.Value;
        slot.Value = value;
        return new Releaser<T>(slot, prior);
    }

    private sealed class Releaser<T> : IDisposable where T : class
    {
        private readonly AsyncLocal<T?> _slot;
        private readonly T? _prior;
        private bool _disposed;
        public Releaser(AsyncLocal<T?> slot, T? prior) { _slot = slot; _prior = prior; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _slot.Value = _prior;
        }
    }

    public readonly struct CaptureScope : IDisposable
    {
        private readonly StringBuilder _buffer;
        private readonly TextWriter? _prior;
        private readonly AsyncLocal<TextWriter?> _slot;

        internal CaptureScope(StringBuilder buffer, TextWriter? prior, AsyncLocal<TextWriter?> slot)
        {
            _buffer = buffer;
            _prior = prior;
            _slot = slot;
        }

        public string GetOutput() => _buffer.ToString();

        public void Dispose() => _slot.Value = _prior;
    }

    private sealed class ProxyWriter : TextWriter
    {
        private readonly TextWriter _fallback;
        private readonly AsyncLocal<TextWriter?> _slot;
        public ProxyWriter(TextWriter fallback, AsyncLocal<TextWriter?> slot)
        {
            _fallback = fallback;
            _slot = slot;
        }
        public override Encoding Encoding => _fallback.Encoding;
        private TextWriter Target => _slot.Value ?? _fallback;
        public override void Write(char value) => Target.Write(value);
        public override void Write(string? value) { if (value is not null) Target.Write(value); }
        public override void Write(char[] buffer, int index, int count) => Target.Write(buffer, index, count);
        public override void WriteLine() => Target.WriteLine();
        public override void WriteLine(string? value) => Target.WriteLine(value);
        public override void Flush() => Target.Flush();
    }

    private sealed class ProxyReader : TextReader
    {
        private readonly TextReader _fallback;
        private readonly AsyncLocal<TextReader?> _slot;
        public ProxyReader(TextReader fallback, AsyncLocal<TextReader?> slot)
        {
            _fallback = fallback;
            _slot = slot;
        }
        private TextReader Target => _slot.Value ?? _fallback;
        public override int Peek() => Target.Peek();
        public override int Read() => Target.Read();
        public override int Read(char[] buffer, int index, int count) => Target.Read(buffer, index, count);
        public override string? ReadLine() => Target.ReadLine();
        public override string ReadToEnd() => Target.ReadToEnd();
    }

    /// <summary>
    /// Minimal TextWriter that appends to a caller-owned <see cref="StringBuilder"/>.
    /// Used by <see cref="Capture"/> so the buffer can be read back via <see cref="CaptureScope.GetOutput"/>.
    /// </summary>
    private sealed class StringBuilderWriter : TextWriter
    {
        private readonly StringBuilder _sb;
        public StringBuilderWriter(StringBuilder sb) => _sb = sb;
        public override Encoding Encoding => Encoding.Unicode;
        public override void Write(char value) => _sb.Append(value);
        public override void Write(string? value) { if (value is not null) _sb.Append(value); }
        public override void Write(char[] buffer, int index, int count) => _sb.Append(buffer, index, count);
        public override void WriteLine() => _sb.Append(Environment.NewLine);
        public override void WriteLine(string? value)
        {
            if (value is not null) _sb.Append(value);
            _sb.Append(Environment.NewLine);
        }
    }
}
