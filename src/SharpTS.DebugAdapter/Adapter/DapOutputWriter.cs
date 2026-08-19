using System.Text;

namespace SharpTS.DebugAdapter.Adapter;

internal sealed class DapOutputWriter(string category, Action<string, string> emit) : TextWriter
{
    private const int MaximumBufferedCharacters = 8 * 1024;
    private readonly object _gate = new();
    private readonly StringBuilder _buffer = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_gate)
        {
            _buffer.Append(value);
            if (value == '\n' || _buffer.Length >= MaximumBufferedCharacters)
                EmitBuffer();
        }
    }

    public override void Write(string? value)
    {
        if (value is null)
            return;
        lock (_gate)
        {
            int start = 0;
            while (start < value.Length)
            {
                int newline = value.IndexOf('\n', start);
                int count = newline < 0 ? value.Length - start : newline - start + 1;
                _buffer.Append(value, start, count);
                start += count;
                if (newline >= 0 || _buffer.Length >= MaximumBufferedCharacters)
                    EmitBuffer();
            }
        }
    }

    public override void Flush()
    {
        lock (_gate)
            EmitBuffer();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Flush();
        base.Dispose(disposing);
    }

    private void EmitBuffer()
    {
        if (_buffer.Length == 0)
            return;
        string output = _buffer.ToString();
        _buffer.Clear();
        emit(category, output);
    }
}
