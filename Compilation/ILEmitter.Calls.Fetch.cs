using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Fetch function emission for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Emits IL for fetch(url, options?).
    /// Calls $Runtime.Fetch(string url, object? options) which returns a Promise.
    /// </summary>
    internal void EmitFetch(List<Expr> arguments)
    {
        // Emit URL - first argument (required)
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            // fetch() with no arguments should throw, but emit null and let runtime handle it
            IL.Emit(OpCodes.Ldnull);
        }

        // Emit options - second argument (optional)
        if (arguments.Count > 1)
        {
            EmitExpression(arguments[1]);
            EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Call $Runtime.Fetch(url, options) - returns Promise
        IL.Emit(OpCodes.Call, _ctx.Runtime!.Fetch);

        // fetch returns a Promise, mark stack as reference type
        SetStackUnknown();
    }
}
