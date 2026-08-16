using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode exports for the Node.js <c>stream/web</c> module.
/// </summary>
/// <remarks>
/// Mirrors Node 18+ which re-exports the WHATWG Streams classes
/// (<c>ReadableStream</c>, <c>WritableStream</c>, <c>TransformStream</c>,
/// <c>ByteLengthQueuingStrategy</c>, <c>CountQueuingStrategy</c>) from this
/// module. All five are also exposed globally.
/// </remarks>
public static class StreamWebModuleInterpreter
{
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["ReadableStream"] = SharpTSReadableStreamConstructor.Instance,
            ["WritableStream"] = SharpTSWritableStreamConstructor.Instance,
            ["TransformStream"] = SharpTSTransformStreamConstructor.Instance,
            ["ByteLengthQueuingStrategy"] = SharpTSByteLengthQueuingStrategyConstructor.Instance,
            ["CountQueuingStrategy"] = SharpTSCountQueuingStrategyConstructor.Instance,
        };
    }
}
