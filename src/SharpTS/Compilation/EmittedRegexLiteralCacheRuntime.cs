using System.Collections.ObjectModel;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>Per-compilation declarations for lazily cached regex literal sites.</summary>
public sealed class EmittedRegexLiteralCacheRuntime
{
    private readonly Dictionary<Expr.RegexLiteral, FieldBuilder> _fields = new(ReferenceEqualityComparer.Instance);
    private readonly ReadOnlyDictionary<Expr.RegexLiteral, FieldBuilder> _view;
    private HashSet<Expr.RegexLiteral>? _pendingSites;

    internal EmittedRegexLiteralCacheRuntime() => _view = new(_fields);

    public bool IsComplete { get; private set; }

    /// <summary>Declared cache fields keyed by AST node identity, available before body emission.</summary>
    public IReadOnlyDictionary<Expr.RegexLiteral, FieldBuilder> Fields
    {
        get
        {
            EnsureStarted();
            return _view;
        }
    }

    internal void BeginDeclarations(IEnumerable<Expr.RegexLiteral> sites)
    {
        EnsureMutable();
        if (_pendingSites is not null)
            throw new InvalidOperationException("Regex-literal cache declarations have already started.");
        ArgumentNullException.ThrowIfNull(sites);
        var pending = new HashSet<Expr.RegexLiteral>(ReferenceEqualityComparer.Instance);
        foreach (var site in sites)
        {
            ArgumentNullException.ThrowIfNull(site);
            if (!pending.Add(site))
                throw new InvalidOperationException("Regex-literal cache site was selected more than once.");
        }
        _pendingSites = pending;
    }

    internal void DeclareField(Expr.RegexLiteral site, FieldBuilder field)
    {
        EnsureMutable();
        EnsureStarted();
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(field);
        if (_fields.ContainsKey(site))
            throw new InvalidOperationException("Regex-literal cache field has already been declared for this site.");
        if (!_pendingSites!.Contains(site))
            throw new InvalidOperationException("Regex literal was not selected for caching.");
        _fields.Add(site, field);
        _pendingSites.Remove(site);
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        EnsureStarted();
        if (_pendingSites!.Count != 0)
            throw new InvalidOperationException("Not all selected regex-literal cache fields have been declared.");
        IsComplete = true;
    }

    private void EnsureStarted()
    {
        if (_pendingSites is null)
            throw new InvalidOperationException("Regex-literal cache declarations have not started.");
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Regex-literal cache metadata emission is already complete.");
    }
}
