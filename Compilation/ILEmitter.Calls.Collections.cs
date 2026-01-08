using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Map, Set, WeakMap, and WeakSet method call emission for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Emits code for Map method calls.
    /// </summary>
    private void EmitMapMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the Map object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        switch (methodName)
        {
            case "get":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapGet);
                return;

            case "set":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapSet);
                return;

            case "has":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapHas);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "delete":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapDelete);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "clear":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapClear);
                IL.Emit(OpCodes.Ldnull); // clear returns undefined
                return;

            case "keys":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapKeys);
                return;

            case "values":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapValues);
                return;

            case "entries":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapEntries);
                return;

            case "forEach":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapForEach);
                IL.Emit(OpCodes.Ldnull); // forEach returns undefined
                return;
        }
    }

    /// <summary>
    /// Emits code for Set method calls.
    /// </summary>
    private void EmitSetMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the Set object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        switch (methodName)
        {
            case "add":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetAdd);
                return;

            case "has":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetHas);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "delete":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetDelete);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "clear":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetClear);
                IL.Emit(OpCodes.Ldnull); // clear returns undefined
                return;

            case "keys":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetKeys);
                return;

            case "values":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetValues);
                return;

            case "entries":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetEntries);
                return;

            case "forEach":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetForEach);
                IL.Emit(OpCodes.Ldnull); // forEach returns undefined
                return;

            // ES2025 Set Operations
            case "union":
                EmitSetOperationArg(arguments);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetUnion);
                return;

            case "intersection":
                EmitSetOperationArg(arguments);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIntersection);
                return;

            case "difference":
                EmitSetOperationArg(arguments);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetDifference);
                return;

            case "symmetricDifference":
                EmitSetOperationArg(arguments);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetSymmetricDifference);
                return;

            case "isSubsetOf":
                EmitSetOperationArg(arguments);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIsSubsetOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "isSupersetOf":
                EmitSetOperationArg(arguments);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIsSupersetOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "isDisjointFrom":
                EmitSetOperationArg(arguments);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIsDisjointFrom);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;
        }
    }

    /// <summary>
    /// Emits the argument for ES2025 Set operations (union, intersection, etc.).
    /// </summary>
    private void EmitSetOperationArg(List<Expr> arguments)
    {
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }
    }

    /// <summary>
    /// Emits code for WeakMap method calls.
    /// </summary>
    private void EmitWeakMapMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the WeakMap object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        switch (methodName)
        {
            case "get":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.WeakMapGet);
                return;

            case "set":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.WeakMapSet);
                return;

            case "has":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.WeakMapHas);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "delete":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.WeakMapDelete);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;
        }
    }

    /// <summary>
    /// Emits code for WeakSet method calls.
    /// </summary>
    private void EmitWeakSetMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the WeakSet object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        switch (methodName)
        {
            case "add":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.WeakSetAdd);
                return;

            case "has":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.WeakSetHas);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;

            case "delete":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.WeakSetDelete);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                return;
        }
    }
}
