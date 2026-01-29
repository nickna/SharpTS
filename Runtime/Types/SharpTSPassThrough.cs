using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js-compatible PassThrough stream.
/// Extends Transform but passes data through unchanged.
/// </summary>
/// <remarks>
/// PassThrough is useful for creating simple data pipelines where
/// no transformation is needed but stream events are still useful.
/// </remarks>
public class SharpTSPassThrough : SharpTSTransform
{
    /// <summary>
    /// Creates a new PassThrough stream.
    /// </summary>
    public SharpTSPassThrough()
    {
        // The default Transform behavior already passes data through
        // No custom transform callback needed
    }

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public new object? GetMember(string name)
    {
        // PassThrough inherits everything from Transform
        // with the default pass-through behavior
        return base.GetMember(name);
    }

    public override string ToString() => "PassThrough {}";
}
