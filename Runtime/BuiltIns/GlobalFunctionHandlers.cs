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
        registry.RegisterV2(BuiltInNames.Symbol, HandleSymbol);
        registry.RegisterV2(BuiltInNames.BigInt, HandleBigInt);
        registry.RegisterV2(BuiltInNames.Date, HandleDate);

        // Dynamic code evaluation
        registry.RegisterV2(BuiltInNames.Eval, HandleEval);

        // Parsing functions
        registry.RegisterV2(BuiltInNames.ParseInt, HandleParseInt);
        registry.RegisterV2(BuiltInNames.ParseFloat, HandleParseFloat);

        // Type checking functions
        registry.RegisterV2(BuiltInNames.IsNaN, HandleIsNaN);
        registry.RegisterV2(BuiltInNames.IsFinite, HandleIsFinite);

        // Utility functions
        registry.RegisterV2(BuiltInNames.StructuredClone, HandleStructuredClone);

        // URI encoding globals (ECMAScript standard)
        registry.RegisterV2(BuiltInNames.EncodeURIComponent, HandleEncodeURIComponent);
        registry.RegisterV2(BuiltInNames.DecodeURIComponent, HandleDecodeURIComponent);

        // Base64 globals (also exported by the 'buffer' module)
        registry.RegisterV2(BuiltInNames.Atob, HandleAtob);
        registry.RegisterV2(BuiltInNames.Btoa, HandleBtoa);

        // Timer functions
        registry.RegisterV2(BuiltInNames.SetTimeout, HandleSetTimeout);
        registry.RegisterV2(BuiltInNames.ClearTimeout, HandleClearTimeout);
        registry.RegisterV2(BuiltInNames.SetInterval, HandleSetInterval);
        registry.RegisterV2(BuiltInNames.ClearInterval, HandleClearInterval);

        // Microtask function
        registry.RegisterV2(BuiltInNames.QueueMicrotask, HandleQueueMicrotask);

        // CommonJS require
        registry.RegisterV2(BuiltInNames.Require, HandleRequire);

        // Internal helper
        registry.RegisterV2(BuiltInNames.ObjectRest, HandleObjectRest);
        registry.RegisterV2(BuiltInNames.ArrayDestructure, HandleArrayDestructure);

        // Note: Error constructors are handled by SharpTSErrorClass (registered as globals).
        // Error() without 'new' resolves to SharpTSErrorClass.Call() via the general
        // ISharpTSCallable dispatch path.
    }

    private static async ValueTask<RuntimeValue> HandleSymbol(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        string? description = null;
        if (arguments.Count > 0)
        {
            var arg = await evaluateArg(arguments[0]);
            // ECMA-262 §20.4.1.1: Symbol(undefined) has no description.
            description = arg.IsUndefined ? null : arg.ToObject()?.ToString();
        }
        // FromSymbol (not FromObject) so the result carries ValueKind.Symbol —
        // otherwise `typeof Symbol("x")` reports "object".
        return RuntimeValue.FromSymbol(new SharpTSSymbol(description));
    }

    private static async ValueTask<RuntimeValue> HandleBigInt(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count != 1)
            throw new InterpreterException($"{BuiltInNames.BigInt}() requires exactly one argument.");

        var argRV = await evaluateArg(arguments[0]);

        if (argRV.IsBigInt)
            return argRV;
        if (argRV.IsNumber)
            return RuntimeValue.FromBigInt(new SharpTSBigInt(Interpreter.ToNumber(argRV)));
        if (argRV.IsString)
            return RuntimeValue.FromBigInt(new SharpTSBigInt(argRV.AsString()));

        var arg = argRV.ToObject();
        if (arg is SharpTSBigInt bi)
            return RuntimeValue.FromBigInt(bi);
        if (arg is System.Numerics.BigInteger biVal)
            return RuntimeValue.FromBigInt(new SharpTSBigInt(biVal));

        throw new Exception($"Runtime Error: Cannot convert {arg?.GetType().Name ?? "null"} to bigint.");
    }

    private static ValueTask<RuntimeValue> HandleDate(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        return ValueTask.FromResult(RuntimeValue.FromString(new SharpTSDate().ToString()));
    }

    private static async ValueTask<RuntimeValue> HandleEval(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            return RuntimeValue.Undefined;

        var argRV = await evaluateArg(arguments[0]);

        // Per ECMA-262 §19.2.1: if the argument is not a string, eval returns it unchanged.
        if (!argRV.IsString)
            return argRV;

        return RuntimeValue.FromBoxed(interpreter.Eval(argRV.AsString()));
    }

    private static async ValueTask<RuntimeValue> HandleParseInt(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.ParseInt}() requires at least one argument.");

        var strRV = await evaluateArg(arguments[0]);
        var str = strRV.ToObject()?.ToString() ?? "";
        int radix = 10;
        if (arguments.Count > 1)
        {
            var radixRV = await evaluateArg(arguments[1]);
            if (radixRV.IsNumber)
                radix = (int)Interpreter.ToNumber(radixRV);
        }
        return RuntimeValue.FromNumber(NumberBuiltIns.ParseInt(str, radix));
    }

    private static async ValueTask<RuntimeValue> HandleParseFloat(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.ParseFloat}() requires at least one argument.");

        var strRV = await evaluateArg(arguments[0]);
        var str = strRV.ToObject()?.ToString() ?? "";
        return RuntimeValue.FromNumber(NumberBuiltIns.ParseFloat(str));
    }

    private static async ValueTask<RuntimeValue> HandleIsNaN(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1) return RuntimeValue.True;

        var argRV = await evaluateArg(arguments[0]);
        if (argRV.IsNumber) return RuntimeValue.FromBoolean(double.IsNaN(Interpreter.ToNumber(argRV)));
        if (argRV.IsString) return RuntimeValue.FromBoolean(!double.TryParse(argRV.AsString(), out _));
        if (argRV.IsNull) return RuntimeValue.True;
        if (argRV.IsBoolean) return RuntimeValue.False;
        return RuntimeValue.True;
    }

    private static async ValueTask<RuntimeValue> HandleIsFinite(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1) return RuntimeValue.False;

        var argRV = await evaluateArg(arguments[0]);
        if (argRV.IsNumber) return RuntimeValue.FromBoolean(double.IsFinite(Interpreter.ToNumber(argRV)));
        if (argRV.IsString && double.TryParse(argRV.AsString(), out double parsed))
            return RuntimeValue.FromBoolean(double.IsFinite(parsed));
        if (argRV.IsNull) return RuntimeValue.True; // null coerces to 0 which is finite
        if (argRV.IsBoolean) return RuntimeValue.True; // true=1, false=0, both finite
        return RuntimeValue.False;
    }

    private static async ValueTask<RuntimeValue> HandleStructuredClone(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.StructuredClone}() requires at least one argument (value).");

        var value = (await evaluateArg(arguments[0])).ToObject();
        SharpTSArray? transfer = null;
        if (arguments.Count > 1)
        {
            var options = (await evaluateArg(arguments[1])).ToObject();
            if (options is SharpTSObject optObj && optObj.Fields.TryGetValue("transfer", out var transferValue))
            {
                transfer = transferValue as SharpTSArray;
            }
        }
        return RuntimeValue.FromBoxed(StructuredClone.Clone(value, transfer));
    }

    private static async ValueTask<RuntimeValue> HandleEncodeURIComponent(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        // JS: encodeURIComponent(undefined) === "undefined"; encodeURIComponent() throws.
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.EncodeURIComponent}() requires exactly one argument.");

        var argRV = await evaluateArg(arguments[0]);
        var str = CoerceToString(argRV);
        return RuntimeValue.FromString(Uri.EscapeDataString(str));
    }

    private static async ValueTask<RuntimeValue> HandleDecodeURIComponent(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.DecodeURIComponent}() requires exactly one argument.");

        var argRV = await evaluateArg(arguments[0]);
        var str = CoerceToString(argRV);
        try
        {
            return RuntimeValue.FromString(Uri.UnescapeDataString(str));
        }
        catch (UriFormatException ex)
        {
            throw new InterpreterException($"URIError: {ex.Message}");
        }
    }

    private static async ValueTask<RuntimeValue> HandleAtob(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.Atob}() requires exactly one argument.");
        var str = CoerceToString(await evaluateArg(arguments[0]));
        return RuntimeValue.FromString(Types.BufferModuleHelpers.Atob(str));
    }

    private static async ValueTask<RuntimeValue> HandleBtoa(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.Btoa}() requires exactly one argument.");
        var str = CoerceToString(await evaluateArg(arguments[0]));
        return RuntimeValue.FromString(Types.BufferModuleHelpers.Btoa(str));
    }

    private static string CoerceToString(RuntimeValue value)
    {
        if (value.IsUndefined) return "undefined";
        if (value.IsNull) return "null";
        if (value.IsString) return value.AsString();
        return value.ToObject()?.ToString() ?? "";
    }

    private static async ValueTask<RuntimeValue> HandleSetTimeout(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.SetTimeout}() requires at least one argument (callback).");

        var callbackValue = (await evaluateArg(arguments[0])).ToObject();
        if (callbackValue is not ISharpTSCallable callback)
            throw new InterpreterException($"{BuiltInNames.SetTimeout}() callback must be a function.");

        double delayMs = 0;
        if (arguments.Count >= 2)
        {
            var delayRV = await evaluateArg(arguments[1]);
            if (delayRV.IsNumber)
                delayMs = Interpreter.ToNumber(delayRV);
            else if (!delayRV.IsUndefined && !delayRV.IsNull)
                throw new Exception($"Runtime Error: {BuiltInNames.SetTimeout}() delay must be a number.");
        }

        List<object?> callbackArgs = [];
        for (int i = 2; i < arguments.Count; i++)
        {
            callbackArgs.Add((await evaluateArg(arguments[i])).ToObject());
        }

        return RuntimeValue.FromBoxed(TimerBuiltIns.SetTimeout(interpreter, callback, delayMs, callbackArgs));
    }

    private static async ValueTask<RuntimeValue> HandleClearTimeout(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        object? handle = null;
        if (arguments.Count > 0)
        {
            handle = (await evaluateArg(arguments[0])).ToObject();
        }
        TimerBuiltIns.ClearTimeout(handle);
        return RuntimeValue.Undefined;
    }

    private static async ValueTask<RuntimeValue> HandleSetInterval(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.SetInterval}() requires at least one argument (callback).");

        var callbackValue = (await evaluateArg(arguments[0])).ToObject();
        if (callbackValue is not ISharpTSCallable callback)
            throw new InterpreterException($"{BuiltInNames.SetInterval}() callback must be a function.");

        double delayMs = 0;
        if (arguments.Count >= 2)
        {
            var delayRV = await evaluateArg(arguments[1]);
            if (delayRV.IsNumber)
                delayMs = Interpreter.ToNumber(delayRV);
            else if (!delayRV.IsUndefined && !delayRV.IsNull)
                throw new Exception($"Runtime Error: {BuiltInNames.SetInterval}() delay must be a number.");
        }

        List<object?> callbackArgs = [];
        for (int i = 2; i < arguments.Count; i++)
        {
            callbackArgs.Add((await evaluateArg(arguments[i])).ToObject());
        }

        return RuntimeValue.FromBoxed(TimerBuiltIns.SetInterval(interpreter, callback, delayMs, callbackArgs));
    }

    private static async ValueTask<RuntimeValue> HandleClearInterval(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        object? handle = null;
        if (arguments.Count > 0)
        {
            handle = (await evaluateArg(arguments[0])).ToObject();
        }
        TimerBuiltIns.ClearInterval(handle);
        return RuntimeValue.Undefined;
    }

    private static async ValueTask<RuntimeValue> HandleQueueMicrotask(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count < 1)
            throw new InterpreterException($"{BuiltInNames.QueueMicrotask}() requires exactly one argument (callback).");

        var callbackValue = (await evaluateArg(arguments[0])).ToObject();
        if (callbackValue is not ISharpTSCallable callback)
            throw new InterpreterException($"{BuiltInNames.QueueMicrotask}() callback must be a function.");

        interpreter.QueueMicrotask(callback);
        return RuntimeValue.Undefined;
    }

    private static async ValueTask<RuntimeValue> HandleObjectRest(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count >= 2)
        {
            var source = (await evaluateArg(arguments[0])).ToObject();
            var excludeKeys = (await evaluateArg(arguments[1])).ToObject() as SharpTSArray;
            return RuntimeValue.FromBoxed(ObjectBuiltIns.ObjectRest(source, (IEnumerable<object?>?)excludeKeys ?? []));
        }
        throw new Exception($"{BuiltInNames.ObjectRest} requires 2 arguments");
    }

    private static async ValueTask<RuntimeValue> HandleArrayDestructure(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count != 1)
            throw new Exception($"{BuiltInNames.ArrayDestructure} requires 1 argument");

        var source = (await evaluateArg(arguments[0])).ToObject();
        return RuntimeValue.FromBoxed(interpreter.NormalizeArrayDestructureSource(source));
    }

    /// <summary>
    /// Implements the CommonJS <c>require(specifier)</c> function. Resolves the module relative
    /// to the calling module and returns its <c>module.exports</c> value. Specifier must be a
    /// string at runtime; throws an error with code <c>MODULE_NOT_FOUND</c> if resolution fails.
    /// </summary>
    private static async ValueTask<RuntimeValue> HandleRequire(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter)
    {
        if (arguments.Count != 1)
        {
            throw new InterpreterException("require() requires exactly one argument.");
        }

        var argRV = await evaluateArg(arguments[0]);
        var specifier = argRV.ToObject()?.ToString();
        if (string.IsNullOrEmpty(specifier))
        {
            throw new InterpreterException("require() argument must be a non-empty string.");
        }

        var result = interpreter.RequireCommonJsModule(specifier);
        return RuntimeValue.FromBoxed(result);
    }

}
