namespace SharpTS.Parsing;

/// <summary>
/// Maps AST nodes to their <see cref="SourceSpan"/>, keyed by object reference.
/// </summary>
/// <remarks>
/// <para><b>Why a side table.</b> AST nodes are C# records, so adding a span field would drag
/// position into value equality and <c>GetHashCode</c> — two <c>return x;</c> statements on
/// different lines would stop comparing equal, and <c>TypeMap</c> (which is reference-keyed) would
/// behave differently from every other consumer. Reference keying also means a node can be shared
/// or rebuilt without its span silently following the wrong copy.</para>
///
/// <para><b>Transforms.</b> Parsing and compilation replace nodes (var hoisting, arrow lifting,
/// destructuring lowering, generator rewriting). Provenance has to be stated explicitly at each of
/// those boundaries — <see cref="CopySpan"/> when a replacement stands in for source the user wrote,
/// <see cref="MarkHidden"/> when the node is scaffolding the user never typed. A node with neither
/// simply has no span, and consumers treat it as unknown rather than guessing.</para>
/// </remarks>
public sealed class SpanTable
{
    private readonly Dictionary<object, SourceSpan> _spans = new(ReferenceEqualityComparer.Instance);

    public int Count => _spans.Count;

    /// <summary>
    /// Associates <paramref name="node"/> with <paramref name="span"/>. The first span recorded for
    /// a node wins, so the innermost production — which knows the tightest extent — is the one kept
    /// when an outer production re-records the same node on its way back up.
    /// </summary>
    public void Record(object node, SourceSpan span)
    {
        ArgumentNullException.ThrowIfNull(node);
        _spans.TryAdd(node, span);
    }

    /// <summary>Overwrites any existing span for <paramref name="node"/>.</summary>
    public void Replace(object node, SourceSpan span)
    {
        ArgumentNullException.ThrowIfNull(node);
        _spans[node] = span;
    }

    public bool TryGetSpan(object node, out SourceSpan span) => _spans.TryGetValue(node, out span);

    /// <summary>The node's span, or null when none was recorded.</summary>
    public SourceSpan? GetSpan(object node) => _spans.TryGetValue(node, out var span) ? span : null;

    /// <summary>
    /// Gives <paramref name="replacement"/> the source position of <paramref name="original"/>,
    /// for a transform that rewrites a node into an equivalent one the user still recognizes as
    /// their code. A no-op when the original had no span, which keeps provenance honest instead of
    /// inventing a position.
    /// </summary>
    public void CopySpan(object original, object replacement)
    {
        if (ReferenceEquals(original, replacement)) return;
        if (_spans.TryGetValue(original, out var span))
            Record(replacement, span);
    }

    /// <summary>
    /// Copies <paramref name="original"/>'s span onto every node in <paramref name="replacements"/>.
    /// Lowerings routinely expand one statement into several — each piece is still attributable to
    /// the statement the user wrote.
    /// </summary>
    public void CopySpan(object original, IEnumerable<object> replacements)
    {
        if (!_spans.TryGetValue(original, out var span)) return;
        foreach (var replacement in replacements)
        {
            if (!ReferenceEquals(original, replacement))
                Record(replacement, span);
        }
    }

    /// <summary>
    /// Marks <paramref name="node"/> as compiler-generated: real enough to emit, but with no source
    /// the user could step to.
    /// </summary>
    public void MarkHidden(object node) => Replace(node, SourceSpan.Hidden);

    /// <summary>True when the node was explicitly marked compiler-generated.</summary>
    public bool IsHidden(object node) => _spans.TryGetValue(node, out var span) && span.IsHidden;

    /// <summary>
    /// Merges another table into this one, used when a transform builds nodes with its own table
    /// and then hands them back to the owning document.
    /// </summary>
    public void MergeFrom(SpanTable other)
    {
        foreach (var (node, span) in other._spans)
            _spans.TryAdd(node, span);
    }
}
