namespace SharpTS.Parsing;

/// <summary>
/// A half-open range of UTF-16 offsets into a single <see cref="SourceDocument"/>.
/// </summary>
/// <param name="Start">Inclusive start offset.</param>
/// <param name="End">Exclusive end offset.</param>
/// <remarks>
/// <para>Offsets — not (line, column) pairs — are the storage form: they are what
/// <see cref="Token.Start"/> already carries, they compare and nest with plain integer arithmetic,
/// and they stay correct no matter how a document's line breaks are counted. Convert to positions
/// at the edges with <see cref="SourceDocument.Lines"/>.</para>
///
/// <para>Spans live in a <see cref="SpanTable"/> keyed by node reference rather than on the AST
/// records themselves, so that two structurally identical subtrees from different parts of a file
/// keep their own positions and record equality stays value-based.</para>
/// </remarks>
public readonly record struct SourceSpan(int Start, int End)
{
    /// <summary>
    /// Marks a node the compiler synthesized, which no source text corresponds to. Distinct from
    /// "no span recorded": a hidden span is a deliberate statement that stepping should pass through
    /// this code rather than attribute it to a nearby statement.
    /// </summary>
    public static readonly SourceSpan Hidden = new(-1, -1);

    /// <summary>True for <see cref="Hidden"/> and any other non-source-backed span.</summary>
    public bool IsHidden => Start < 0;

    /// <summary>True when the span covers no characters (including <see cref="Hidden"/>).</summary>
    public bool IsEmpty => End <= Start;

    public int Length => IsHidden ? 0 : End - Start;

    /// <summary>True when <paramref name="offset"/> falls inside the half-open range.</summary>
    public bool Contains(int offset) => !IsHidden && offset >= Start && offset < End;

    /// <summary>
    /// True when this span fully covers <paramref name="other"/>. Used to walk from the outermost
    /// node at a cursor position down to the narrowest one.
    /// </summary>
    public bool Contains(SourceSpan other) =>
        !IsHidden && !other.IsHidden && other.Start >= Start && other.End <= End;

    /// <summary>The smallest span covering both operands; hidden operands are ignored.</summary>
    public SourceSpan Union(SourceSpan other)
    {
        if (IsHidden) return other;
        if (other.IsHidden) return this;
        return new SourceSpan(Math.Min(Start, other.Start), Math.Max(End, other.End));
    }

    public override string ToString() => IsHidden ? "<hidden>" : $"[{Start}..{End})";
}
