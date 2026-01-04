namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript/TypeScript symbols.
/// </summary>
/// <remarks>
/// Symbols are unique identifiers. Each <c>Symbol()</c> call creates a distinct symbol,
/// even with the same description. Symbols can be used as property keys in objects.
/// </remarks>
public class SharpTSSymbol
{
    private static int _nextId = 0;
    private readonly int _id;
    private readonly string? _description;

    public SharpTSSymbol(string? description = null)
    {
        _id = Interlocked.Increment(ref _nextId);
        _description = description;
    }

    public string? Description => _description;

    public override bool Equals(object? obj) =>
        obj is SharpTSSymbol other && _id == other._id;

    public override int GetHashCode() => _id;

    public override string ToString() =>
        _description != null ? $"Symbol({_description})" : "Symbol()";
}
