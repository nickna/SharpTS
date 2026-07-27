namespace SharpTS.Parsing;

/// <summary>
/// Converts UTF-16 offsets in a source text to one-based (line, column) positions and back.
/// </summary>
/// <remarks>
/// Built once per document and shared by everything that needs positions — diagnostics, the
/// language server, and portable PDB sequence points — so a single definition of "line 3, column 5"
/// applies everywhere. Line breaks are counted at <c>\n</c>, which also handles <c>\r\n</c> because
/// the <c>\r</c> stays on the preceding line.
/// </remarks>
public sealed class LineIndex
{
    private readonly int[] _lineStarts;
    private readonly int _length;

    public LineIndex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') starts.Add(i + 1);
        }

        _lineStarts = [.. starts];
        _length = text.Length;
    }

    /// <summary>Number of lines in the document (always at least one).</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>Converts an offset to a one-based line and column.</summary>
    public (int Line, int Column) ToPosition(int offset)
    {
        if (offset <= 0) return (1, 1);
        if (offset > _length) offset = _length;

        int low = 0, high = _lineStarts.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (_lineStarts[mid] <= offset) low = mid;
            else high = mid - 1;
        }
        return (low + 1, offset - _lineStarts[low] + 1);
    }

    /// <summary>
    /// Converts a one-based line and column back to an offset, clamping to the document.
    /// </summary>
    public int ToOffset(int line, int column)
    {
        int index = Math.Clamp(line - 1, 0, _lineStarts.Length - 1);
        int lineStart = _lineStarts[index];
        int lineEnd = index + 1 < _lineStarts.Length ? _lineStarts[index + 1] : _length;
        return Math.Clamp(lineStart + Math.Max(0, column - 1), lineStart, lineEnd);
    }
}
