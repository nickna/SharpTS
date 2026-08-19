using System.Text;

namespace SharpTS.DebugAdapter;

/// <summary>Keeps an explicitly requested diagnostic log bounded to one session.</summary>
internal sealed class BoundedFileLogWriter : TextWriter
{
    internal const int MaximumCharacters = 1024 * 1024;

    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private int _remainingCharacters = MaximumCharacters;

    public BoundedFileLogWriter(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        _writer = new StreamWriter(new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous))
        {
            AutoFlush = true,
        };
    }

    public override Encoding Encoding => _writer.Encoding;

    public override void Write(char value)
    {
        lock (_gate)
        {
            if (_remainingCharacters == 0)
                return;
            _writer.Write(value);
            _remainingCharacters--;
        }
    }

    public override void Write(string? value)
    {
        if (value is null)
            return;
        lock (_gate)
            WriteCore(value);
    }

    public override void WriteLine(string? value)
    {
        lock (_gate)
        {
            if (value is not null)
                WriteCore(value);
            WriteCore(NewLine);
        }
    }

    public override void Flush()
    {
        lock (_gate)
            _writer.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
                _writer.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void WriteCore(string value)
    {
        if (_remainingCharacters == 0 || value.Length == 0)
            return;
        int length = Math.Min(value.Length, _remainingCharacters);
        _writer.Write(value.AsSpan(0, length));
        _remainingCharacters -= length;
    }
}
