using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'events' module.
/// The main export is the EventEmitter constructor class.
/// </summary>
/// <remarks>
/// When you do `import { EventEmitter } from 'events'`, the EventEmitter variable is stored
/// with a placeholder value. Actual instantiation via `new EventEmitter()` is dispatched
/// via the EmitNew method which directly creates the $EventEmitter type.
/// Method calls on EventEmitter instances are dispatched via EventEmitterEmitter.
/// </remarks>
public sealed class EventsModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "events";

    private static readonly string[] _exportedMembers = ["EventEmitter"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        // The events module doesn't have direct method calls - methods are on EventEmitter instances
        return false;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName != "EventEmitter")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit a placeholder value for EventEmitter.
        // The actual instantiation (new EventEmitter()) happens via EmitNew which
        // directly creates the $EventEmitter type.
        // The stored value is only used if EventEmitter is passed around as a first-class
        // value (which is rare). For now, emit a string marker.
        il.Emit(OpCodes.Ldstr, "[EventEmitter]");
        return true;
    }
}
