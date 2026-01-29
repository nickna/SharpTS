using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'perf_hooks' module.
/// </summary>
/// <remarks>
/// Provides high-resolution timing APIs similar to the browser's Performance API.
/// </remarks>
public sealed class PerfHooksModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "perf_hooks";

    private static readonly string[] _exportedMembers = ["performance"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        // performance is an object, not a method
        // Method calls like performance.now() are handled as property access + call
        return false;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName == "performance")
        {
            // Return the performance object
            var ctx = emitter.Context;
            var il = ctx.IL;

            il.Emit(OpCodes.Call, ctx.Runtime!.PerfHooksGetPerformance);
            return true;
        }

        return false;
    }
}
