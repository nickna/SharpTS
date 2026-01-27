using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'buffer' module.
/// </summary>
/// <remarks>
/// Provides the Buffer class for working with binary data.
/// The buffer module exports the Buffer constructor which is also available globally.
/// </remarks>
public static class BufferModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the buffer module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["Buffer"] = SharpTSBufferConstructor.Instance
        };
    }
}
