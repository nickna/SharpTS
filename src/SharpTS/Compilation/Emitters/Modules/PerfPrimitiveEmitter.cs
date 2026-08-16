using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL for the <c>primitive:perf</c> primitive module. Exposes a single
/// <c>now()</c> method returning high-resolution ms since first call.
/// Dispatches to <c>$Runtime.PerfPrimitiveNow()</c> which lazily captures a
/// <see cref="System.Diagnostics.Stopwatch"/> reference timestamp on first call.
/// </summary>
/// <remarks>
/// Registered under the specifier <c>primitive:perf</c> only — user code can't
/// import primitives directly (origin-gated in <see cref="Modules.ModuleResolver"/>).
/// <c>stdlib/node/perf_hooks.ts</c> imports <c>now</c> from here and builds the
/// full Node perf_hooks surface in pure TypeScript.
/// </remarks>
public sealed class PerfPrimitiveEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:perf";

    private static readonly string[] _exportedMembers = ["now"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        if (methodName == "now")
        {
            var ctx = emitter.Context;
            var il = ctx.IL;
            il.Emit(OpCodes.Call, ctx.Runtime!.PerfPrimitiveNow);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }
        return false;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName) => false;

    public bool IsExportedProperty(string memberName) => false;
}
