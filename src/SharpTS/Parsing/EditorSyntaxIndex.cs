using System.Collections.Frozen;
using System.Collections.Immutable;

namespace SharpTS.Parsing;

public enum EditorSyntaxKind
{
    Expression, Name, Literal, Type, Grouping, Function, Class, Block,
    ParameterList, Invocation, MemberAccess, JsxElement, JsxAttribute,
}

public enum EditorSyntaxRole
{
    Whole, Name, DeclarationName, MemberName, PrivateName, LiteralKey,
    Receiver, Callee, Argument, Annotation, Header, Body, Delimiter, Parameter,
}

public enum EditorSyntaxOrigin { Written, Grouping, SourceEquivalent, Recovered, Synthetic, Implicit }

/// <summary>A source view over an existing AST object; it does not participate in AST equality.</summary>
public sealed record EditorSyntaxRecord(object Node, EditorSyntaxKind Kind, SourceSpan Span,
    EditorSyntaxRole Role = EditorSyntaxRole.Whole,
    EditorSyntaxOrigin Origin = EditorSyntaxOrigin.Written, Token? Token = null)
{
    internal long Ordinal { get; init; }
    public bool IsAuthoritative => (Origin is EditorSyntaxOrigin.Written or EditorSyntaxOrigin.Grouping or
        EditorSyntaxOrigin.SourceEquivalent) &&
        !Span.IsHidden && !Span.IsEmpty;
}

public sealed record EditorInvocationSyntax(Expr Owner, Expr Callee, SourceSpan Span,
    SourceSpan OpenParen, SourceSpan? CloseParen, IReadOnlyList<SourceSpan> Commas,
    bool IsNew = false, bool IsOptional = false, bool IsRecovered = false)
{
    internal long Ordinal { get; init; }
    public bool ContainsCursor(int offset) => offset >= OpenParen.End &&
        offset <= (CloseParen?.Start ?? Span.End);

    /// <summary>Only the parser's top-level argument separators contribute to this index.</summary>
    public int GetActiveArgumentIndex(int offset) => Commas.Count(comma => comma.End <= offset);
}

public sealed record EditorMemberSyntax(Expr Owner, Expr Receiver, Token Name, SourceSpan Span,
    SourceSpan OperatorSpan, bool IsOptional = false, bool IsPrivate = false,
    bool IsIndex = false, bool IsRecovered = false)
{
    internal long Ordinal { get; init; }
    public bool ContainsCursor(int offset) => offset >= OperatorSpan.Start &&
        offset <= (Name.Start >= 0 ? Name.End : Span.End);
}

/// <summary>
/// Immutable, document-owned syntax views over the final parser AST. Its interval arrays answer
/// cursor queries without traversing the AST or guessing from a nearest token. Hidden, implicit
/// and synthetic views cannot be authoritative source matches.
/// </summary>
public sealed class EditorSyntaxIndex
{
    private readonly IntervalIndex<EditorSyntaxRecord> _records;
    private readonly IntervalIndex<EditorInvocationSyntax> _invocations;
    private readonly IntervalIndex<EditorMemberSyntax> _members;
    private readonly FrozenDictionary<object, IReadOnlyList<EditorSyntaxRecord>> _byNode;

    internal EditorSyntaxIndex(SourceDocument document, IEnumerable<EditorSyntaxRecord> records,
        IEnumerable<EditorInvocationSyntax> invocations, IEnumerable<EditorMemberSyntax> members)
    {
        Document = document;
        Records = records.OrderBy(record => record.Span.Start).ThenBy(record => record.Span.Length)
            .ThenBy(record => record.Kind).ThenBy(record => record.Role).ThenBy(record => record.Ordinal).ToImmutableArray();
        Invocations = invocations.OrderBy(record => record.Span.Start).ThenBy(record => record.Span.Length)
            .Select(record => record with { Commas = record.Commas.ToImmutableArray() }).ToImmutableArray();
        Members = members.OrderBy(record => record.Span.Start).ThenBy(record => record.Span.Length).ToImmutableArray();
        _records = new(Records, record => record.Span);
        _invocations = new(Invocations, record => record.Span);
        _members = new(Members, record => record.Span);
        _byNode = Records.GroupBy(record => record.Node, ReferenceEqualityComparer.Instance)
            .ToFrozenDictionary(group => group.Key, group => (IReadOnlyList<EditorSyntaxRecord>)group.ToImmutableArray(),
                ReferenceEqualityComparer.Instance);
    }

    public SourceDocument Document { get; }
    public IReadOnlyList<EditorSyntaxRecord> Records { get; }
    public IReadOnlyList<EditorInvocationSyntax> Invocations { get; }
    public IReadOnlyList<EditorMemberSyntax> Members { get; }
    public int Count => Records.Count + Invocations.Count + Members.Count;
    public long EstimatedBytes => Records.Count * 96L + Members.Count * 112L +
        Invocations.Sum(invocation => 112L + invocation.Commas.Count * 8L);

    public IReadOnlyList<EditorSyntaxRecord> GetRecords(object node) =>
        _byNode.TryGetValue(node, out var records) ? records : ImmutableArray<EditorSyntaxRecord>.Empty;

    public EditorSyntaxRecord? FindNarrowest(int offset, EditorSyntaxKind? kind = null,
        EditorSyntaxRole? role = null) => _records.Containing(offset)
        .Where(record => record.IsAuthoritative && (kind is null || kind == record.Kind) &&
            (role is null || role == record.Role))
        .OrderBy(record => record.Span.Length).ThenByDescending(record => record.Span.Start)
        .ThenBy(record => record.Kind).ThenBy(record => record.Role).ThenBy(record => record.Ordinal).FirstOrDefault();

    public EditorInvocationSyntax? FindInvocation(int offset) => _invocations.Containing(offset, includeEnd: true)
        .Where(invocation => invocation.ContainsCursor(offset))
        .OrderBy(invocation => invocation.Span.Length).ThenByDescending(invocation => invocation.OpenParen.Start)
        .ThenBy(invocation => invocation.Ordinal).FirstOrDefault();

    public EditorMemberSyntax? FindMember(int offset) => _members.Containing(offset, includeEnd: true)
        .Where(member => member.ContainsCursor(offset))
        .OrderBy(member => member.Span.Length).ThenByDescending(member => member.OperatorSpan.Start)
        .ThenBy(member => member.Ordinal).FirstOrDefault();

    private sealed class IntervalIndex<T>(IReadOnlyList<T> items, Func<T, SourceSpan> span)
    {
        private readonly int[] _maximumEnd = PrefixEnds(items, span);

        public IEnumerable<T> Containing(int offset, bool includeEnd = false)
        {
            int low = 0, high = items.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (span(items[middle]).Start <= offset) low = middle + 1;
                else high = middle;
            }
            for (int index = low - 1; index >= 0; index--)
            {
                if (includeEnd ? _maximumEnd[index] < offset : _maximumEnd[index] <= offset) yield break;
                SourceSpan candidate = span(items[index]);
                if (!candidate.IsHidden && candidate.Start <= offset &&
                    (includeEnd ? offset <= candidate.End : offset < candidate.End)) yield return items[index];
            }
        }

        private static int[] PrefixEnds(IReadOnlyList<T> values, Func<T, SourceSpan> getSpan)
        {
            int[] ends = new int[values.Count];
            int maximum = -1;
            for (int index = 0; index < ends.Length; index++)
            {
                maximum = Math.Max(maximum, getSpan(values[index]).End);
                ends[index] = maximum;
            }
            return ends;
        }
    }
}

internal sealed class EditorSyntaxBuilder
{
    private readonly Dictionary<object, List<EditorSyntaxRecord>> _records = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, EditorInvocationSyntax> _invocations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, EditorMemberSyntax> _members = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, List<TypeNode>> _attachedTypes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, Token> _writtenTokens = new(ReferenceEqualityComparer.Instance);
    private long _nextOrdinal;

    public void Record(EditorSyntaxRecord record)
    {
        if (!_records.TryGetValue(record.Node, out var views)) _records.Add(record.Node, views = []);
        if (!views.Any(view => view.Kind == record.Kind && view.Role == record.Role &&
            view.Span == record.Span && view.Origin == record.Origin && view.Token == record.Token))
            views.Add(record with { Ordinal = _nextOrdinal++ });
    }
    public void Record(EditorInvocationSyntax invocation) =>
        _invocations[invocation.Owner] = invocation with { Ordinal = _nextOrdinal++ };
    public void Record(EditorMemberSyntax member) =>
        _members[member.Owner] = member with { Ordinal = _nextOrdinal++ };
    public void Attach(object owner, TypeNode node)
    {
        if (!_attachedTypes.TryGetValue(owner, out var types)) _attachedTypes.Add(owner, types = []);
        if (!types.Any(type => ReferenceEquals(type, node))) types.Add(node);
    }
    public void AliasToken(Token normalized, Token written) => _writtenTokens[normalized] = written;
    public Token WrittenToken(Token token) => _writtenTokens.TryGetValue(token, out var written) ? written : token;
    public void Copy(object original, object replacement) => CopyCore(original, replacement, includeExpressionWhole: true);
    public void CopyViews(object original, object replacement) => CopyCore(original, replacement, includeExpressionWhole: false);

    private void CopyCore(object original, object replacement, bool includeExpressionWhole)
    {
        if (ReferenceEquals(original, replacement)) return;
        if (_attachedTypes.TryGetValue(original, out var attachedTypes))
            foreach (TypeNode type in attachedTypes) Attach(replacement, type);
        if (_records.TryGetValue(original, out var views))
            foreach (var view in views.ToArray())
                if (includeExpressionWhole || view.Kind != EditorSyntaxKind.Expression || view.Role != EditorSyntaxRole.Whole)
                    Record(view with { Node = replacement, Origin = view.Origin == EditorSyntaxOrigin.Written
                        ? EditorSyntaxOrigin.SourceEquivalent : view.Origin });
        if (replacement is Expr expression)
        {
            if (_invocations.TryGetValue(original, out var invocation))
                Record(invocation with { Owner = expression });
            if (_members.TryGetValue(original, out var member))
                Record(member with { Owner = expression });
        }
    }

    public EditorSyntaxIndex Build(SourceDocument document, IReadOnlyList<Stmt> roots, CancellationToken ct)
    {
        var reachable = EditorSyntaxReachability.Collect(roots, ct, _attachedTypes);
        document.Spans.RetainReachable(reachable);
        bool Valid(SourceSpan span) => !span.IsHidden && span.Start <= span.End && span.End <= document.Text.Length;
        return new(document,
            _records.Where(pair => reachable.Contains(pair.Key)).SelectMany(pair => pair.Value).Where(record => Valid(record.Span)),
            _invocations.Values.Where(invocation => reachable.Contains(invocation.Owner) && Valid(invocation.Span)),
            _members.Values.Where(member => reachable.Contains(member.Owner) && Valid(member.Span)));
    }
}
