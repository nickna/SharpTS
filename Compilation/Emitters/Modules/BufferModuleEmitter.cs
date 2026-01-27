using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'buffer' module.
/// The main export is the Buffer constructor with static methods.
/// </summary>
/// <remarks>
/// When you do `import { Buffer } from 'buffer'`, the Buffer variable is stored
/// with a placeholder value. Actual method calls like `Buffer.from()` are dispatched
/// via the TypeEmitterRegistry which looks up "Buffer" by name and uses BufferStaticEmitter.
/// </remarks>
public sealed class BufferModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "buffer";

    private static readonly string[] _exportedMembers = ["Buffer"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        // The buffer module doesn't have direct method calls - methods are on Buffer.xxx
        return false;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName != "Buffer")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit a placeholder value for Buffer.
        // The actual method dispatch (Buffer.from, Buffer.alloc, etc.) happens via
        // the TypeEmitterRegistry which looks up "Buffer" by variable name and uses
        // BufferStaticEmitter. The stored value is only used if Buffer is passed
        // around as a first-class value (which is rare).
        // For now, emit a string marker that identifies this as the Buffer constructor.
        il.Emit(OpCodes.Ldstr, "[Buffer]");
        return true;
    }
}
