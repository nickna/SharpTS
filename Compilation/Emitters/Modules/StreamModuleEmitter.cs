using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'stream' module.
/// Exports Readable, Writable, Duplex, Transform, and PassThrough stream constructors.
/// </summary>
/// <remarks>
/// When you do `import { Readable } from 'stream'`, the Readable variable is stored
/// with a placeholder value. Actual instantiation via `new Readable()` is dispatched
/// via the EmitNew method which directly creates the stream type instances.
/// Method calls on stream instances are dispatched via the RuntimeEmitter-generated types.
/// </remarks>
public sealed class StreamModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "stream";

    private static readonly string[] _exportedMembers =
    [
        "Readable",
        "Writable",
        "Duplex",
        "Transform",
        "PassThrough"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        // The stream module doesn't have direct method calls - methods are on stream instances
        return false;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (!_exportedMembers.Contains(propertyName))
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit a placeholder value for the stream constructor.
        // The actual instantiation (new Readable(), etc.) happens via EmitNew which
        // directly creates the stream type instances.
        il.Emit(OpCodes.Ldstr, $"[{propertyName}]");
        return true;
    }
}
