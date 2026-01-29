using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'stream' module.
/// </summary>
/// <remarks>
/// Provides stream classes for data processing:
/// - Readable: Read data from a source
/// - Writable: Write data to a destination
/// - Duplex: Read and write independently
/// - Transform: Transform data as it passes through
/// - PassThrough: Pass data through unchanged
/// </remarks>
public static class StreamModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the stream module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["Readable"] = SharpTSReadableConstructor.Instance,
            ["Writable"] = SharpTSWritableConstructor.Instance,
            ["Duplex"] = SharpTSDuplexConstructor.Instance,
            ["Transform"] = SharpTSTransformConstructor.Instance,
            ["PassThrough"] = SharpTSPassThroughConstructor.Instance
        };
    }
}
