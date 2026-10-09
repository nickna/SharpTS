namespace SharpTS.Parsing;

/// <summary>
/// Source-span bookkeeping for the parser: where each production started and ended, and how that
/// provenance is handed to nodes the parser synthesizes rather than reads.
/// </summary>
public partial class Parser
{
    private SpanTable _spans = new();
    private SourceDocument? _document;
    private EditorSyntaxBuilder? _editorSyntax;

    /// <summary>The completed opt-in syntax index for this parse.</summary>
    public EditorSyntaxIndex? EditorSyntax { get; private set; }

    /// <summary>Captures bounded editor provenance in addition to ordinary debugger spans.</summary>
    public Parser WithEditorSyntax(bool enabled = true)
    {
        _editorSyntax = enabled ? new EditorSyntaxBuilder() : null;
        _spans.Copied = _editorSyntax is null ? null : _editorSyntax.Copy;
        return this;
    }

    private bool EditorSyntaxEnabled => _editorSyntax is not null;

    private int CurrentSourceStart() => _editorSyntax is null ? -1 : Peek().Start;

    // A split generic closer remains at _current; its shifted remainder starts precisely where
    // the consumed prefix ended. Cursor rewinds ignore a partial end from another token index.
    private int _partialGreaterIndex = -1;
    private int _partialGreaterEnd = -1;
    private int ConsumedSourceEnd => _partialGreaterIndex == _current && Peek().Start == _partialGreaterEnd
        ? _partialGreaterEnd : _current > 0 ? Previous().End : 0;

    private readonly record struct ParserCursorCheckpoint(int Position, int PartialGreaterIndex,
        int PartialGreaterEnd, int TokenEditCount);

    // Checkpoints are value types. The undo journal is allocated only when a compound generic
    // closer is actually split, and preserves the exact original token on failed speculation.
    private List<(int Index, Token Original)>? _splitTokenEdits;

    private ParserCursorCheckpoint SaveCursor() => new(_current, _partialGreaterIndex,
        _partialGreaterEnd, _splitTokenEdits?.Count ?? 0);

    private void RestoreCursor(ParserCursorCheckpoint checkpoint)
    {
        if (_splitTokenEdits is { } edits)
        {
            for (int index = edits.Count - 1; index >= checkpoint.TokenEditCount; index--)
                _tokens[edits[index].Index] = edits[index].Original;
            if (edits.Count > checkpoint.TokenEditCount)
                edits.RemoveRange(checkpoint.TokenEditCount, edits.Count - checkpoint.TokenEditCount);
        }
        _current = checkpoint.Position;
        _partialGreaterIndex = checkpoint.PartialGreaterIndex;
        _partialGreaterEnd = checkpoint.PartialGreaterEnd;
    }

    private void RecordSplitToken(Token original) => (_splitTokenEdits ??= []).Add((_current, original));

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
        _spans.Copied = null;
        _document = document;
        _spans = document.Spans;
        _spans.Copied = _editorSyntax is null ? null : _editorSyntax.Copy;
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
        int end = ConsumedSourceEnd;
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

    private T RecordExpression<T>(T node, int startOffset, int endOffset,
        EditorSyntaxOrigin origin = EditorSyntaxOrigin.Written) where T : Expr
    {
        if (_editorSyntax is null || startOffset < 0 || endOffset < startOffset) return node;
        var span = new SourceSpan(startOffset, endOffset);
        _spans.Record(node, span);
        RecordEditorRange(node, span, EditorSyntaxKind.Expression, origin: origin);
        return node;
    }

    private void RecordEditorRange(object? node, SourceSpan span, EditorSyntaxKind kind,
        EditorSyntaxRole role = EditorSyntaxRole.Whole, EditorSyntaxOrigin origin = EditorSyntaxOrigin.Written,
        Token? token = null)
    {
        if (_editorSyntax is not null && node is not null)
            _editorSyntax.Record(new EditorSyntaxRecord(node, kind, span, role, origin, token));
    }

    private void RecordEditorName(Token token, object owner, EditorSyntaxRole role = EditorSyntaxRole.Name,
        EditorSyntaxKind kind = EditorSyntaxKind.Name, EditorSyntaxOrigin origin = EditorSyntaxOrigin.Written)
    {
        if (_editorSyntax is null) return;
        token = _editorSyntax.WrittenToken(token);
        RecordEditorRange(owner, new SourceSpan(token.Start, token.End), kind, role, origin, token);
    }

    private Token AliasEditorToken(Token normalized, Token written)
    {
        _editorSyntax?.AliasToken(normalized, written);
        return normalized;
    }

    private void RecordLiteralSyntax(Expr owner, Token token) =>
        RecordEditorName(token, owner, EditorSyntaxRole.Whole, EditorSyntaxKind.Literal);

    private void CopyEditorSyntax(object original, object replacement) => _editorSyntax?.CopyViews(original, replacement);

    // Some parser branches intentionally keep only the legacy type spelling in their semantic
    // AST. Preserve their already-parsed type provenance for the editor without changing what
    // the checker sees; the attachment lives only while its final owner is reachable.
    private void AttachEditorSyntax(object owner, TypeNode? node)
    {
        if (_editorSyntax is not null && node is not null) _editorSyntax.Attach(owner, node);
    }

    private void RecordMemberSyntax(Expr owner, Expr receiver, Token name, SourceSpan operatorSpan,
        bool optional = false, bool isPrivate = false, bool isIndex = false, bool recovered = false)
    {
        if (_editorSyntax is null) return;
        name = _editorSyntax.WrittenToken(name);
        int start = _spans.GetSpan(receiver)?.Start ?? operatorSpan.Start;
        int end = Math.Max(name.End, Math.Max(operatorSpan.End, ConsumedSourceEnd));
        var span = _spans.GetSpan(owner) ?? new SourceSpan(start, end);
        _editorSyntax.Record(new EditorMemberSyntax(owner, receiver, name, span, operatorSpan,
            optional, isPrivate, isIndex, recovered));
        RecordEditorRange(owner, span, EditorSyntaxKind.MemberAccess,
            origin: recovered ? EditorSyntaxOrigin.Recovered : EditorSyntaxOrigin.Written);
        RecordEditorName(name, owner, isPrivate ? EditorSyntaxRole.PrivateName :
            isIndex ? EditorSyntaxRole.LiteralKey : EditorSyntaxRole.MemberName,
            origin: recovered ? EditorSyntaxOrigin.Recovered : EditorSyntaxOrigin.Written);
    }

    private void RecordCallSyntax(Expr owner, Expr callee, Token open, Token? close,
        IReadOnlyList<Token> commas, bool isNew = false, bool optional = false, bool recovered = false)
    {
        if (_editorSyntax is null) return;
        int start = _spans.GetSpan(owner)?.Start ?? _spans.GetSpan(callee)?.Start ?? open.Start;
        int end = close?.End ?? Math.Max(_spans.GetSpan(owner)?.End ?? open.End, Math.Max(open.End, ConsumedSourceEnd));
        var span = new SourceSpan(start, end);
        _editorSyntax.Record(new EditorInvocationSyntax(owner, callee, span,
            new SourceSpan(open.Start, open.End), close is null ? null : new SourceSpan(close.Start, close.End),
            commas.Select(comma => new SourceSpan(comma.Start, comma.End)).ToArray(), isNew, optional, recovered));
        RecordEditorRange(owner, span, EditorSyntaxKind.Invocation,
            origin: recovered ? EditorSyntaxOrigin.Recovered : EditorSyntaxOrigin.Written);
    }

    private void PublishEditorSyntax(IReadOnlyList<Stmt> roots)
    {
        _spans.Copied = null;
        if (_editorSyntax is null) return;
        // WithEditorSyntax can be used without a document by small parser clients. Require real
        // source ownership for an index instead of inventing text or offsets from token lexemes.
        if (_document is not null) _document.EditorSyntax = EditorSyntax =
            _editorSyntax.Build(_document, roots, _cancellationToken);
        _editorSyntax = null; // Do not retain abandoned/speculative objects after publication.
    }
}
