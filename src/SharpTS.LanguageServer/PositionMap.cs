using SharpTS.Diagnostics;
using SharpTS.Parsing;

namespace SharpTS.LanguageServer;

/// <summary>
/// Maps a global character offset (as carried by <see cref="Token.Start"/>) to a 1-based
/// (line, column), and produces precise <see cref="SourceLocation"/> spans for tokens.
/// Built once per document buffer. This is what makes token-precise diagnostic ranges and
/// cursor-on-token detection possible without any AST source-span instrumentation.
/// </summary>
public sealed class PositionMap
{
    private readonly int[] _lineStarts; // global offset where each 0-based line begins

    public PositionMap(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts.Add(i + 1);
        _lineStarts = starts.ToArray();
    }

    /// <summary>Global offset → (1-based line, 1-based column), matching SourceLocation.</summary>
    public (int line, int column) ToLineCol(int offset)
    {
        if (offset <= 0) return (1, 1);
        int lo = 0, hi = _lineStarts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_lineStarts[mid] <= offset) lo = mid; else hi = mid - 1;
        }
        return (lo + 1, offset - _lineStarts[lo] + 1);
    }

    /// <summary>Zero-based LSP position → global UTF-16 offset.</summary>
    public int ToOffset(int line, int character)
    {
        if ((uint)line >= (uint)_lineStarts.Length)
            throw new ArgumentOutOfRangeException(nameof(line));
        if (character < 0)
            throw new ArgumentOutOfRangeException(nameof(character));

        return checked(_lineStarts[line] + character);
    }

    /// <summary>Precise span for a single token (falls back to line-only if Start is unset).</summary>
    public SourceLocation Span(Token token)
    {
        if (token.Start < 0) return SourceLocation.FromLine(token.Line);
        var (line, col) = ToLineCol(token.Start);
        var (endLine, endCol) = ToLineCol(token.Start + token.Lexeme.Length);
        return new SourceLocation(null, line, col, endLine, endCol);
    }

    /// <summary>Span from the start of one token to the end of another (e.g. <c>@</c> → decorator name).</summary>
    public SourceLocation Span(Token start, Token end)
    {
        if (start.Start < 0) return SourceLocation.FromLine(start.Line);
        var (line, col) = ToLineCol(start.Start);
        int endOffset = end.Start >= 0 ? end.Start + end.Lexeme.Length : start.Start + start.Lexeme.Length;
        var (endLine, endCol) = ToLineCol(endOffset);
        return new SourceLocation(null, line, col, endLine, endCol);
    }

    /// <summary>True if the (0-based LSP) cursor falls within the token's span.</summary>
    public bool Contains(Token token, int line0, int character0)
    {
        if (token.Start < 0) return false;
        var (line, col) = ToLineCol(token.Start);
        // single-line tokens only (identifiers); good enough for member names.
        return line - 1 == line0 && character0 >= col - 1 && character0 < col - 1 + token.Lexeme.Length;
    }
}
