namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Represents a built-in namespace like Math, Object, Array, JSON, or console.
/// </summary>
/// <param name="Name">The namespace name (e.g., "Math", "Object")</param>
/// <param name="IsSingleton">True if accessing the name returns a singleton object (e.g., Math)</param>
/// <param name="SingletonFactory">Factory to create the singleton instance, if IsSingleton is true</param>
/// <param name="GetMethod">Function to look up a method by name, returns null if not found</param>
public record BuiltInNamespace(
    string Name,
    bool IsSingleton,
    Func<object>? SingletonFactory,
    Func<string, BuiltInMethod?> GetMethod
);
