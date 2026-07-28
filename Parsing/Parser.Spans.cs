namespace SharpTS.Parsing;

/// <summary>
/// Source-span bookkeeping for the parser: where each production started and ended, and how that
/// provenance is handed to nodes the parser synthesizes rather than reads.
/// </summary>
public partial class Parser
{
    private SpanTable _spans = new();

    /// <summary>
    /// Source positions of the nodes produced by this parser, keyed by node reference.
    /// </summary>
    /// <remarks>
    /// Always populated — recording an entry is a dictionary insert per statement — so consumers can
    /// ask for a position without the parse having to know in advance whether anyone will.
    /// </remarks>
    public SpanTable Spans => _spans;

    /// <summary>
    /// Parses into <paramref name="document"/>, so the document owns the resulting spans.
    /// </summary>
    public Parser WithSourceDocument(SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _spans = document.Spans;
        _filePath ??= document.Path;
        return this;
    }

    /// <summary>
    /// Records <paramref name="node"/>'s extent as running from the token at
    /// <paramref name="firstTokenIndex"/> through the last token consumed.
    /// </summary>
    private void RecordSpanFrom(object? node, int firstTokenIndex)
    {
        if (node is null || firstTokenIndex >= _tokens.Count) return;

        int start = _tokens[firstTokenIndex].Start;
        if (start < 0) return;

        // _current has already advanced past the production, so the previous token is its last.
        int last = Math.Clamp(_current - 1, firstTokenIndex, _tokens.Count - 1);
        int end = _tokens[last].End;
        if (end < start) end = start;

        var span = new SourceSpan(start, end);
        _spans.Record(node, span);

        // A Sequence is not something the user writes — it is how a lowering returns several
        // statements where one appeared, as destructuring declarations do. Its parts are all
        // attributable to that one construct, so they inherit its position unless they already
        // carry a tighter one of their own.
        if (node is Stmt.Sequence sequence) RecordLoweredParts(sequence, span);

        // `export class C {}` parses the declaration directly rather than back through this
        // dispatcher, so the wrapped declaration would otherwise have no position of its own —
        // and it, not the wrapper, is what consumers ask about.
        if (node is Stmt.Export { Declaration: { } exported }) _spans.Record(exported, span);
    }

    /// <summary>
    /// Attributes the parts of a lowering to the construct they came from.
    /// </summary>
    /// <remarks>
    /// Descends only into parts that do not already have a span. That guard is what keeps the walk
    /// linear: statement nodes are freely shared between sequences, and a sequence is re-recorded
    /// every time an enclosing production returns it, so re-descending into already-attributed
    /// subtrees compounds — it cost ~19s on a two-file project before the guard existed. A part that
    /// already carries a span was attributed by its own production or by an outer lowering, and its
    /// own parts were attributed at the same time, so there is nothing below it left to do.
    /// </remarks>
    private void RecordLoweredParts(Stmt.Sequence sequence, SourceSpan span)
    {
        foreach (var part in sequence.Statements)
        {
            if (_spans.TryGetSpan(part, out _)) continue;

            _spans.Record(part, span);
            if (part is Stmt.Sequence nested) RecordLoweredParts(nested, span);
        }
    }

    /// <summary>Records a node's extent as covering exactly one token.</summary>
    private void RecordSpan(object? node, Token token)
    {
        if (node is null || token.Start < 0) return;
        _spans.Record(node, token.Span);
    }

    /// <summary>Records a node's extent as running from one token through another.</summary>
    private void RecordSpan(object? node, Token first, Token last)
    {
        if (node is null || first.Start < 0) return;
        int end = last.End >= 0 ? last.End : first.End;
        _spans.Record(node, new SourceSpan(first.Start, Math.Max(first.Start, end)));
    }

    /// <summary>
    /// Gives a node the parser synthesized the position of the source construct it stands for, so
    /// that lowered code still points back at what the user wrote.
    /// </summary>
    private void CopySpan(object original, object replacement) => _spans.CopySpan(original, replacement);

    /// <summary>
    /// Gives every node in <paramref name="replacements"/> the position of the construct it was
    /// lowered from. Destructuring and similar rewrites expand one statement into several.
    /// </summary>
    private void CopySpan(object original, IEnumerable<object> replacements) => _spans.CopySpan(original, replacements);

    /// <summary>
    /// Marks a node as pure scaffolding with no source of its own, so stepping passes through it
    /// instead of attributing it to whatever statement happens to be nearby.
    /// </summary>
    private void MarkHidden(object node) => _spans.MarkHidden(node);
}
