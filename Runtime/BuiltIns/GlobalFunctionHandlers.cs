using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Handler implementations for global JavaScript functions.
/// Each handler corresponds to a global function like Symbol(), parseInt(), setTimeout(), etc.
/// </summary>
internal static class GlobalFunctionHandlers
{
    /// <summary>
    /// Registers all global function handlers with the registry.
    /// </summary>
    public static void RegisterAll(GlobalFunctionRegistry registry)
    {
        // Constructors (can be called without 'new')
        registry.Register(BuiltInNames.Symbol, HandleSymbol);
        registry.Register(BuiltInNames.BigInt, HandleBigInt);
        registry.Register(BuiltInNames.Date, HandleDate);

        // Parsing functions
        registry.Register(BuiltInNames.ParseInt, HandleParseInt);
        registry.Register(BuiltInNames.ParseFloat, HandleParseFloat);

        // Type checking functions
        registry.Register(BuiltInNames.IsNaN, HandleIsNaN);
        registry.Register(BuiltInNames.IsFinite, HandleIsFinite);

        // Utility functions
        registry.Register(BuiltInNames.StructuredClone, HandleStructuredClone);

        // Timer functions
        registry.Register(BuiltInNames.SetTimeout, HandleSetTimeout);
        registry.Register(BuiltInNames.ClearTimeout, HandleClearTimeout);
        registry.Register(BuiltInNames.SetInterval, HandleSetInterval);
        registry.Register(BuiltInNames.ClearInterval, HandleClearInterval);

        // Internal helper
        registry.Register(BuiltInNames.ObjectRest, HandleObjectRest);

        // Error constructors (called without 'new')
        // Each error type gets its own handler that knows its type name
        foreach (var errorType in BuiltInNames.ErrorTypeNames)
        {
            registry.Register(errorType, CreateErrorHandler(errorType));
        }
    }

    /// <summary>
    /// Handle Symbol() constructor - creates unique symbols.
    /// </summary>
    private static async ValueTask<object?> HandleSymbol(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        string? description = null;
        if (arguments.Count > 0)
        {
            var arg = await evaluateArg(arguments[0]);
            description = arg?.ToString();
        }
        return new SharpTSSymbol(description);
    }

    /// <summary>
    /// Handle BigInt() constructor - converts number/string to bigint.
    /// </summary>
    private static async ValueTask<object?> HandleBigInt(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count != 1)
            throw new InterpreterException($"{BuiltInNames.BigInt}() requires exactly one argument.");

        var arg = await evaluateArg(arguments[0]);
        return arg switch
        {
            SharpTSBigInt bi => bi,
            System.Numerics.BigInteger biVal => new SharpTSBigInt(biVal),
            double d => new SharpTSBigInt(d),
            string s => new SharpTSBigInt(s),
            _ => throw new Exception($"Runtime Error: Cannot convert {arg?.GetType().Name ?? "null"} to bigint.")
        };
    }

    /// <summary>
    /// Handle Date() function call - returns current date as string (without 'new').
    /// Date() called without 'new' ignores all arguments and returns current date as string.
    /// </summary>
    private static ValueTask<object?> HandleDate(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        return ValueTask.FromResult<object?>(new SharpTSDate().ToString());
    }

    /// <summary>
    /// Handle global parseInt().
    /// </summary>
    private static async ValueTask<object?> HandleParseInt(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.ParseInt}() requires at least one argument.");

        var str = (await evaluateArg(arguments[0]))?.ToString() ?? "";
        int radix = 10;
        if (arguments.Count > 1)
        {
            var radixValue = await evaluateArg(arguments[1]);
            if (radixValue != null)
                radix = (int)(double)radixValue;
        }
        return NumberBuiltIns.ParseInt(str, radix);
    }

    /// <summary>
    /// Handle global parseFloat().
    /// </summary>
    private static async ValueTask<object?> HandleParseFloat(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.ParseFloat}() requires at least one argument.");

        var str = (await evaluateArg(arguments[0]))?.ToString() ?? "";
        return NumberBuiltIns.ParseFloat(str);
    }

    /// <summary>
    /// Handle global isNaN().
    /// Global isNaN coerces to number first (different from Number.isNaN).
    /// </summary>
    private static async ValueTask<object?> HandleIsNaN(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1) return true; // isNaN() with no args returns true

        var arg = await evaluateArg(arguments[0]);
        // Global isNaN coerces to number first (different from Number.isNaN)
        if (arg is double d) return double.IsNaN(d);
        if (arg is string s) return !double.TryParse(s, out _);
        if (arg is null) return true;
        if (arg is bool) return false;
        return true;
    }

    /// <summary>
    /// Handle global isFinite().
    /// Global isFinite coerces to number first (different from Number.isFinite).
    /// </summary>
    private static async ValueTask<object?> HandleIsFinite(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1) return false; // isFinite() with no args returns false

        var arg = await evaluateArg(arguments[0]);
        // Global isFinite coerces to number first (different from Number.isFinite)
        if (arg is double d) return double.IsFinite(d);
        if (arg is string s && double.TryParse(s, out double parsed)) return double.IsFinite(parsed);
        if (arg is null) return true; // null coerces to 0 which is finite
        if (arg is bool) return true; // true=1, false=0, both finite
        return false;
    }

    /// <summary>
    /// Handle global structuredClone(value, options?).
    /// </summary>
    private static async ValueTask<object?> HandleStructuredClone(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.StructuredClone}() requires at least one argument (value).");

        var value = await evaluateArg(arguments[0]);
        SharpTSArray? transfer = null;
        if (arguments.Count > 1)
        {
            var options = await evaluateArg(arguments[1]);
            if (options is SharpTSObject optObj && optObj.Fields.TryGetValue("transfer", out var transferValue))
            {
                transfer = transferValue as SharpTSArray;
            }
        }
        return StructuredClone.Clone(value, transfer);
    }

    /// <summary>
    /// Handle setTimeout(callback, delay?, ...args).
    /// </summary>
    private static async ValueTask<object?> HandleSetTimeout(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.SetTimeout}() requires at least one argument (callback).");

        var callbackValue = await evaluateArg(arguments[0]);
        if (callbackValue is not ISharpTSCallable callback)
            throw new InterpreterException($"{BuiltInNames.SetTimeout}() callback must be a function.");

        // Get delay (defaults to 0)
        double delayMs = 0;
        if (arguments.Count >= 2)
        {
            var delayValue = await evaluateArg(arguments[1]);
            if (delayValue is double dv)
                delayMs = dv;
            else if (delayValue != null && delayValue is not SharpTSUndefined)
                throw new Exception($"Runtime Error: {BuiltInNames.SetTimeout}() delay must be a number, got {delayValue.GetType().Name}.");
        }

        // Get additional args for the callback
        List<object?> callbackArgs = [];
        for (int i = 2; i < arguments.Count; i++)
        {
            callbackArgs.Add(await evaluateArg(arguments[i]));
        }

        return TimerBuiltIns.SetTimeout(interpreter, callback, delayMs, callbackArgs);
    }

    /// <summary>
    /// Handle clearTimeout(handle?).
    /// </summary>
    private static async ValueTask<object?> HandleClearTimeout(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        object? handle = null;
        if (arguments.Count > 0)
        {
            handle = await evaluateArg(arguments[0]);
        }
        TimerBuiltIns.ClearTimeout(handle);
        return null;
    }

    /// <summary>
    /// Handle setInterval(callback, delay?, ...args).
    /// </summary>
    private static async ValueTask<object?> HandleSetInterval(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.SetInterval}() requires at least one argument (callback).");

        var callbackValue = await evaluateArg(arguments[0]);
        if (callbackValue is not ISharpTSCallable callback)
            throw new InterpreterException($"{BuiltInNames.SetInterval}() callback must be a function.");

        // Get delay (defaults to 0)
        double delayMs = 0;
        if (arguments.Count >= 2)
        {
            var delayValue = await evaluateArg(arguments[1]);
            if (delayValue is double dv)
                delayMs = dv;
            else if (delayValue != null && delayValue is not SharpTSUndefined)
                throw new Exception($"Runtime Error: {BuiltInNames.SetInterval}() delay must be a number, got {delayValue.GetType().Name}.");
        }

        // Get additional args for the callback
        List<object?> callbackArgs = [];
        for (int i = 2; i < arguments.Count; i++)
        {
            callbackArgs.Add(await evaluateArg(arguments[i]));
        }

        return TimerBuiltIns.SetInterval(interpreter, callback, delayMs, callbackArgs);
    }

    /// <summary>
    /// Handle clearInterval(handle?).
    /// </summary>
    private static async ValueTask<object?> HandleClearInterval(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        object? handle = null;
        if (arguments.Count > 0)
        {
            handle = await evaluateArg(arguments[0]);
        }
        TimerBuiltIns.ClearInterval(handle);
        return null;
    }

    /// <summary>
    /// Handle __objectRest (internal helper for object rest patterns).
    /// </summary>
    private static async ValueTask<object?> HandleObjectRest(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count >= 2)
        {
            var source = await evaluateArg(arguments[0]);
            var excludeKeys = await evaluateArg(arguments[1]) as SharpTSArray;
            return ObjectBuiltIns.ObjectRest(source, excludeKeys?.Elements ?? []);
        }
        throw new Exception($"{BuiltInNames.ObjectRest} requires 2 arguments");
    }

    /// <summary>
    /// Creates an error handler for a specific error type.
    /// This factory method allows each error type to have its own handler that knows its type name.
    /// </summary>
    public static GlobalFunctionRegistry.GlobalFunctionHandler CreateErrorHandler(string errorTypeName)
    {
        return async (evaluateArg, arguments, interpreter) =>
        {
            var pooledArgs = ArgumentListPool.Rent();
            try
            {
                foreach (var arg in arguments)
                {
                    pooledArgs.Add(await evaluateArg(arg));
                }
                return ErrorBuiltIns.CreateError(errorTypeName, pooledArgs);
            }
            finally
            {
                ArgumentListPool.Return(pooledArgs);
            }
        };
    }
}
