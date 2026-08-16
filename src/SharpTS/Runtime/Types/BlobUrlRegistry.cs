using System.Collections.Concurrent;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Process-wide registry mapping <c>blob:</c> object-URL ids to their
/// <see cref="SharpTSBlob"/>. Populated by <c>URL.createObjectURL</c> and read by
/// <c>buffer.resolveObjectURL</c> (and cleared by <c>URL.revokeObjectURL</c>).
/// </summary>
public static class BlobUrlRegistry
{
    private const string Prefix = "blob:nodedata:";

    private static readonly ConcurrentDictionary<string, SharpTSBlob> _registry = new();
    private static long _counter;

    /// <summary>Registers a blob and returns a fresh <c>blob:</c> URL string.</summary>
    public static string Create(SharpTSBlob blob)
    {
        var id = $"{Prefix}{System.Threading.Interlocked.Increment(ref _counter):x}-{Guid.NewGuid():N}";
        _registry[id] = blob;
        return id;
    }

    /// <summary>Removes a previously created object URL.</summary>
    public static void Revoke(string url) => _registry.TryRemove(url, out _);

    /// <summary>Resolves an object URL to its Blob, or null if unknown/revoked.</summary>
    public static SharpTSBlob? Resolve(string url)
        => _registry.TryGetValue(url, out var blob) ? blob : null;
}
