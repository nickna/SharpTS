using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Global metadata store implementing the reflect-metadata polyfill API.
/// Stores metadata associated with targets and optional property keys.
/// Used for TypeScript decorator metadata (design:type, design:paramtypes, design:returntype).
///
/// IMPORTANT: Uses weak references matching JavaScript WeakMap semantics.
/// When a target object is garbage collected, all associated metadata is automatically removed.
/// </summary>
public class ReflectMetadataStore
{
    /// <summary>Singleton instance for global metadata storage.</summary>
    public static ReflectMetadataStore Instance { get; } = new();

    /// <summary>
    /// Metadata storage with weak references to target objects.
    /// Structure: Target (weak) -> [(PropertyKey, MetadataKey) -> Value]
    /// PropertyKey is null for class-level metadata.
    /// </summary>
    private readonly ConditionalWeakTable<object, Dictionary<(string? PropertyKey, string MetadataKey), object?>> _metadata = new();

    /// <summary>
    /// Defines metadata for a target with an optional property key.
    /// Implements: Reflect.defineMetadata(metadataKey, metadataValue, target [, propertyKey])
    /// </summary>
    /// <remarks>
    /// Metadata is stored with weak references to the target object.
    /// If the target is garbage collected, the metadata will be lost.
    /// This matches JavaScript reflect-metadata WeakMap behavior.
    /// </remarks>
    public void DefineMetadata(string metadataKey, object? metadataValue, object target, string? propertyKey = null)
    {
        var innerDict = _metadata.GetValue(target, _ => new Dictionary<(string?, string), object?>());
        innerDict[(propertyKey, metadataKey)] = metadataValue;
    }

    /// <summary>
    /// Gets metadata for a target with an optional property key.
    /// Implements: Reflect.getMetadata(metadataKey, target [, propertyKey])
    /// Returns null if metadata is not defined or target has been garbage collected.
    /// </summary>
    public object? GetMetadata(string metadataKey, object target, string? propertyKey = null)
    {
        if (!_metadata.TryGetValue(target, out var innerDict))
            return null;

        return innerDict.TryGetValue((propertyKey, metadataKey), out var value) ? value : null;
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
    /// Returns false if target has been garbage collected.
    /// </summary>
    public bool HasMetadata(string metadataKey, object target, string? propertyKey = null)
    {
        if (!_metadata.TryGetValue(target, out var innerDict))
            return false;

        return innerDict.ContainsKey((propertyKey, metadataKey));
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
    /// Returns empty list if target has been garbage collected.
    /// </summary>
    public List<string> GetMetadataKeys(object target, string? propertyKey = null)
    {
        if (!_metadata.TryGetValue(target, out var innerDict))
            return new List<string>();

        return innerDict.Keys
            .Where(k => k.PropertyKey == propertyKey)
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
    /// Returns true if metadata was deleted, false if it didn't exist or target was garbage collected.
    /// </summary>
    public bool DeleteMetadata(string metadataKey, object target, string? propertyKey = null)
    {
        if (!_metadata.TryGetValue(target, out var innerDict))
            return false;

        return innerDict.Remove((propertyKey, metadataKey));
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
