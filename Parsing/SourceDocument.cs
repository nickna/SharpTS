using System.Security.Cryptography;
using System.Text;

namespace SharpTS.Parsing;

/// <summary>
/// One unit of source text plus everything positional derived from it: a stable identity, a line
/// index, a content checksum, and the <see cref="SpanTable"/> holding the positions of the AST
/// parsed from it.
/// </summary>
/// <remarks>
/// <para>Created per file (or per virtual module) and carried alongside the AST so that later
/// stages can answer "where did this node come from" without re-reading or re-lexing anything.
/// Debug symbols need the path and checksum; editor navigation needs the spans; both need the line
/// index.</para>
///
/// <para>Virtual documents — the bundled stdlib, REPL input, in-memory test sources — have no file
/// a debugger could open, so <see cref="IsVirtual"/> tells the PDB writer to embed the text
/// instead of just referencing it.</para>
/// </remarks>
public sealed class SourceDocument
{
    private byte[]? _checksum;

    /// <param name="path">
    /// Identity of the document. For real files this should be a normalized absolute path, which is
    /// what a debugger and an LSP client both resolve against.
    /// </param>
    /// <param name="text">The exact text that was compiled.</param>
    /// <param name="isVirtual">True when no file with this content exists on disk.</param>
    public SourceDocument(string path, string text, bool isVirtual = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);

        Path = path;
        Text = text;
        IsVirtual = isVirtual;
        Lines = new LineIndex(text);
        Spans = new SpanTable();
    }

    public string Path { get; }

    public string Text { get; }

    /// <summary>True when the text has no backing file, so consumers must carry it themselves.</summary>
    public bool IsVirtual { get; }

    public LineIndex Lines { get; }

    /// <summary>Source positions of the AST nodes parsed from this document.</summary>
    public SpanTable Spans { get; }

    /// <summary>
    /// SHA-256 of the document's UTF-8 bytes, letting a debugger detect that the file on disk has
    /// drifted from what was compiled. Computed on first use.
    /// </summary>
    public byte[] Checksum => _checksum ??= SHA256.HashData(Encoding.UTF8.GetBytes(Text));

    /// <summary>Converts a node's span to a one-based start and end position in this document.</summary>
    public (int StartLine, int StartColumn, int EndLine, int EndColumn)? PositionOf(object node)
    {
        if (!Spans.TryGetSpan(node, out var span) || span.IsHidden) return null;

        var (startLine, startColumn) = Lines.ToPosition(span.Start);
        var (endLine, endColumn) = Lines.ToPosition(span.End);
        return (startLine, startColumn, endLine, endColumn);
    }

    public override string ToString() => Path;
}
