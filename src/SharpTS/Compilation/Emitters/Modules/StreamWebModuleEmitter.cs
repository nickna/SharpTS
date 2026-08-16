using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL for the Node.js <c>stream/web</c> module's named imports.
/// </summary>
/// <remarks>
/// All five exports (<c>ReadableStream</c>, <c>WritableStream</c>,
/// <c>TransformStream</c>, <c>ByteLengthQueuingStrategy</c>,
/// <c>CountQueuingStrategy</c>) are construction-only — JS code does
/// <c>new X(...)</c>, which the compiler routes through
/// <see cref="ExpressionEmitterBase.TryEmitBuiltInConstructor"/> directly to
/// the emitted ctor handles. The imported binding itself is rarely used as a
/// first-class value, so we just emit a stable string marker for
/// <c>typeof X</c> queries (returning <c>"function"</c> in JS terms).
///
/// Mirrors <see cref="EventsModuleEmitter"/> for <c>EventEmitter</c>.
/// </remarks>
public sealed class StreamWebModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "stream/web";

    private static readonly string[] _exportedMembers =
    [
        "ReadableStream",
        "WritableStream",
        "TransformStream",
        "ByteLengthQueuingStrategy",
        "CountQueuingStrategy",
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        // Construction goes through TryEmitBuiltInConstructor; this module
        // doesn't have free-function exports.
        return false;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (Array.IndexOf(_exportedMembers, propertyName) < 0)
            return false;

        // Emit a placeholder TSFunction so `typeof ReadableStream === 'function'`
        // works for first-class binding access. The actual `new X(...)` path
        // never reads this value — it dispatches via TryEmitModuleQualifiedConstructor
        // which routes to the emitted ctor directly.
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Use the same pattern as EventsModuleEmitter: a stable string marker.
        // typeof returns "function" because the FunctionMarker pattern is
        // detected by the typeof helper.
        il.Emit(OpCodes.Ldstr, "[" + propertyName + "]");
        return true;
    }
}
