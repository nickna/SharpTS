namespace SharpTS.Runtime.Types;

/// <summary>
/// Global metadata store implementing the reflect-metadata polyfill API.
/// Stores metadata associated with targets and optional property keys.
/// Used for TypeScript decorator metadata (design:type, design:paramtypes, design:returntype).
/// </summary>
public class ReflectMetadataStore
{
    /// <summary>Singleton instance for global metadata storage.</summary>
    public static ReflectMetadataStore Instance { get; } = new();

    /// <summary>
    /// Metadata storage: (target, propertyKey, metadataKey) -> value
    /// PropertyKey is null for class-level metadata.
    /// </summary>
    private readonly Dictionary<(object Target, string? PropertyKey, string MetadataKey), object?> _metadata = new();

    /// <summary>
    /// Defines metadata for a target with an optional property key.
    /// Implements: Reflect.defineMetadata(metadataKey, metadataValue, target [, propertyKey])
    /// </summary>
    public void DefineMetadata(string metadataKey, object? metadataValue, object target, string? propertyKey = null)
    {
        var key = (target, propertyKey, metadataKey);
        _metadata[key] = metadataValue;
    }

    /// <summary>
    /// Gets metadata for a target with an optional property key.
    /// Implements: Reflect.getMetadata(metadataKey, target [, propertyKey])
    /// Returns null if metadata is not defined.
    /// </summary>
    public object? GetMetadata(string metadataKey, object target, string? propertyKey = null)
    {
        var key = (target, propertyKey, metadataKey);
        return _metadata.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Gets own metadata (not inherited) for a target with an optional property key.
    /// Implements: Reflect.getOwnMetadata(metadataKey, target [, propertyKey])
    /// </summary>
    public object? GetOwnMetadata(string metadataKey, object target, string? propertyKey = null)
    {
        // For our implementation, getOwnMetadata behaves same as getMetadata
        // (we don't walk prototype chain)
        return GetMetadata(metadataKey, target, propertyKey);
    }

    /// <summary>
    /// Checks if metadata exists for a target with an optional property key.
    /// Implements: Reflect.hasMetadata(metadataKey, target [, propertyKey])
    /// </summary>
    public bool HasMetadata(string metadataKey, object target, string? propertyKey = null)
    {
        var key = (target, propertyKey, metadataKey);
        return _metadata.ContainsKey(key);
    }

    /// <summary>
    /// Checks if own metadata exists for a target with an optional property key.
    /// Implements: Reflect.hasOwnMetadata(metadataKey, target [, propertyKey])
    /// </summary>
    public bool HasOwnMetadata(string metadataKey, object target, string? propertyKey = null)
    {
        return HasMetadata(metadataKey, target, propertyKey);
    }

    /// <summary>
    /// Gets all metadata keys for a target with an optional property key.
    /// Implements: Reflect.getMetadataKeys(target [, propertyKey])
    /// </summary>
    public List<string> GetMetadataKeys(object target, string? propertyKey = null)
    {
        return _metadata.Keys
            .Where(k => ReferenceEquals(k.Target, target) && k.PropertyKey == propertyKey)
            .Select(k => k.MetadataKey)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Gets own metadata keys for a target with an optional property key.
    /// Implements: Reflect.getOwnMetadataKeys(target [, propertyKey])
    /// </summary>
    public List<string> GetOwnMetadataKeys(object target, string? propertyKey = null)
    {
        return GetMetadataKeys(target, propertyKey);
    }

    /// <summary>
    /// Deletes metadata for a target with an optional property key.
    /// Implements: Reflect.deleteMetadata(metadataKey, target [, propertyKey])
    /// Returns true if metadata was deleted, false if it didn't exist.
    /// </summary>
    public bool DeleteMetadata(string metadataKey, object target, string? propertyKey = null)
    {
        var key = (target, propertyKey, metadataKey);
        return _metadata.Remove(key);
    }

    /// <summary>
    /// Clears all metadata. Useful for testing.
    /// </summary>
    public void Clear()
    {
        _metadata.Clear();
    }

    /// <summary>
    /// Standard metadata keys used by TypeScript when emitDecoratorMetadata is enabled.
    /// </summary>
    public static class StandardKeys
    {
        /// <summary>Type of the decorated property/parameter</summary>
        public const string DesignType = "design:type";

        /// <summary>Parameter types for methods/constructors</summary>
        public const string DesignParamTypes = "design:paramtypes";

        /// <summary>Return type for methods</summary>
        public const string DesignReturnType = "design:returntype";
    }
}
