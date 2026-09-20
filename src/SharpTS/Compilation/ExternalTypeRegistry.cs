using System.Collections.ObjectModel;

namespace SharpTS.Compilation;

/// <summary>Owns external CLR type aliases and overload hints for one compilation.</summary>
/// <remarks>
/// Aliases and hints retain source-order replacement semantics. Reads are available
/// during declaration; validated writes are frozen at declaration completion.
/// </remarks>
public sealed class ExternalTypeRegistry
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, Dictionary<string, string>> _overloadHints = [];

    public ExternalTypeRegistry() => Types = new ReadOnlyDictionary<string, Type>(_types);

    public IReadOnlyDictionary<string, Type> Types { get; }
    public bool IsComplete { get; private set; }

    /// <summary>Declares an alias, replacing an earlier binding as in source-order resolution.</summary>
    public void DeclareOrReplace(string name, Type type)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(type);
        _types[name] = type;
    }

    /// <summary>Snapshots and merges hints; later hints replace the same method's earlier hint.</summary>
    public void RegisterOverloadHints(Type type, IReadOnlyDictionary<string, string> hints)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(hints);
        var snapshot = hints.ToArray();
        foreach (var (name, hint) in snapshot)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(hint);
        }
        if (snapshot.Length == 0) return;
        if (!_overloadHints.TryGetValue(type, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            _overloadHints.Add(type, map);
        }
        foreach (var (name, hint) in snapshot)
            map[name] = hint;
    }

    public string? GetOverloadHint(Type type, string methodName)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(methodName);
        return _overloadHints.TryGetValue(type, out var map)
            ? map.GetValueOrDefault(methodName) : null;
    }

    public void CompleteDeclarations()
    {
        EnsureMutable();
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("External type declarations are already complete.");
    }
}
