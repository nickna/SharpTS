namespace SharpTS.Runtime.Types;

/// <summary>
/// Array-like storage for a function's arguments object.
/// </summary>
/// <remarks>
/// Reuses <see cref="SharpTSArray"/>'s indexed-property implementation, while
/// retaining the distinct ECMAScript identity needed by Object.prototype.toString,
/// Array.isArray, and instanceof Array.
/// </remarks>
public sealed class SharpTSArguments(IEnumerable<object?> arguments)
    : SharpTSArray(arguments);
