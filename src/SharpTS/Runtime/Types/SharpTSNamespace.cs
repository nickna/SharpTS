using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a TypeScript namespace.
/// </summary>
/// <remarks>
/// At runtime, namespaces are objects with their exported members as properties.
/// Classes become constructor functions, functions remain functions, and variables are values.
/// Supports declaration merging via the Merge method.
/// NOTE: Changes here must be mirrored in RuntimeEmitter.EmitTSNamespaceClass() for compiled assemblies.
/// </remarks>
public class SharpTSNamespace : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Namespace;

    public string Name { get; }
    private readonly Dictionary<string, object?> _members = [];

    /// <summary>
    /// Live bindings for exported mutable namespace variables (#623). Per TS semantics, a
    /// namespace's <c>export let</c>/<c>export var</c> is a live view of the binding its member
    /// functions mutate — not a snapshot taken at declaration. When a name has a live binding,
    /// <see cref="Get"/> resolves the current value through the getter instead of the
    /// declaration-time value in <see cref="_members"/>. The <see cref="_members"/> entry is
    /// still kept (the snapshot) so <see cref="HasMember"/>, <see cref="GetMemberNames"/>,
    /// <see cref="Members"/>, and <see cref="Merge"/> continue to enumerate the member by name.
    /// (<c>const</c> members never change, so their snapshot already equals the live value —
    /// they get no live binding.) Lazily allocated; null when the namespace has none.
    /// <para>
    /// Interpreter-only and deliberately NOT mirrored into the emitted compiled
    /// <c>$TSNamespace</c> (despite the class-level note): compiled mode achieves the same #623
    /// liveness by redirecting external <c>N.x</c> reads to the var's static backing field at
    /// IL-emit time (see <c>ILEmitter.TryEmitNamespaceVarGet</c>), so the compiled namespace
    /// object never needs a live-binding layer.
    /// </para>
    /// </summary>
    private Dictionary<string, Func<object?>>? _liveBindings;

    public SharpTSNamespace(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets a member value by name. An exported mutable variable with a live binding (#623)
    /// resolves to its current value; all other members return their stored value.
    /// </summary>
    public object? Get(string name)
    {
        if (_liveBindings != null && _liveBindings.TryGetValue(name, out var getter))
            return getter();
        return _members.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>
    /// Sets a member value.
    /// </summary>
    public void Set(string name, object? value)
    {
        _members[name] = value;
    }

    /// <summary>
    /// Registers a live binding for an exported mutable namespace variable so external
    /// <c>N.x</c> reflects later mutations by member functions (#623). The caller is expected
    /// to also record the declaration-time value via <see cref="Set"/> for enumeration/merging.
    /// </summary>
    public void SetLiveBinding(string name, Func<object?> getter)
    {
        _liveBindings ??= [];
        _liveBindings[name] = getter;
    }

    /// <summary>
    /// Checks if a member exists.
    /// </summary>
    public bool HasMember(string name)
    {
        return _members.ContainsKey(name);
    }

    /// <summary>
    /// Gets all member names.
    /// </summary>
    public IEnumerable<string> GetMemberNames() => _members.Keys;

    /// <summary>
    /// Gets all members as key-value pairs (for iteration during namespace merging).
    /// </summary>
    public IEnumerable<KeyValuePair<string, object?>> Members => _members;

    /// <summary>
    /// Merges another namespace's members into this one (for declaration merging).
    /// </summary>
    public void Merge(SharpTSNamespace other)
    {
        foreach (var (name, value) in other._members)
        {
            _members[name] = value;
        }
        // Carry over live bindings (#623) so a merged block's mutable exports stay live.
        if (other._liveBindings != null)
        {
            _liveBindings ??= [];
            foreach (var (name, getter) in other._liveBindings)
            {
                _liveBindings[name] = getter;
            }
        }
    }

    public override string ToString() => $"[namespace {Name}]";
}
