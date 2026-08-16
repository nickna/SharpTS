using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using SharpTS.Execution;

namespace SharpTS.Runtime;

/// <summary>
/// Helper for binding function parameters to arguments.
/// Handles rest parameters, default values, optional parameters, and required parameter validation.
/// </summary>
internal static class ParameterBinder
{
    /// <summary>
    /// Binds parameters to arguments in a synchronous context.
    /// </summary>
    /// <param name="parameters">The function's parameter declarations.</param>
    /// <param name="arguments">The arguments provided by the caller.</param>
    /// <param name="environment">The runtime environment to define parameters in.</param>
    /// <param name="interpreter">The interpreter for evaluating default values.</param>
    internal static void Bind(
        List<Stmt.Parameter> parameters,
        List<object?> arguments,
        RuntimeEnvironment environment,
        Interpreter interpreter)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];

            if (param.IsRest)
            {
                // Rest parameter - collect all remaining arguments
                var restArgs = arguments.Skip(i).ToList();
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                break; // Rest is always last
            }

            object? value;
            bool hasArg = i < arguments.Count;
            bool argIsUndefined = hasArg && arguments[i] is SharpTSUndefined;
            if (hasArg && !argIsUndefined)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue is { } defaultExpr)
            {
                // Per ECMA-262: default value applies when argument is missing OR explicitly undefined.
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = interpreter.Evaluate(defaultExpr);
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else
            {
                // Missing argument with no default — use undefined (JavaScript semantics).
                value = SharpTSUndefined.Instance;
            }
            environment.Define(param.Name.Lexeme, value);
        }
    }

    /// <summary>
    /// Binds parameters to RuntimeValue arguments without boxing.
    /// Used by the V2 call path for user-defined functions.
    /// </summary>
    internal static void BindRV(
        List<Stmt.Parameter> parameters,
        ReadOnlySpan<RuntimeValue> arguments,
        RuntimeEnvironment environment,
        Interpreter interpreter)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];

            if (param.IsRest)
            {
                // Rest parameter - collect all remaining arguments as object? list
                var restArgs = new List<object?>();
                for (int j = i; j < arguments.Length; j++)
                    restArgs.Add(arguments[j].ToObject());
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                break; // Rest is always last
            }

            RuntimeValue value;
            bool hasArg = i < arguments.Length;
            bool argIsUndefined = hasArg && arguments[i].IsUndefined;
            if (hasArg && !argIsUndefined)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue is { } defaultExpr)
            {
                // Per ECMA-262: default value applies when argument is missing OR explicitly undefined.
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = interpreter.EvaluateRV(defaultExpr);
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else
            {
                // Missing argument with no default — use undefined (JavaScript semantics).
                value = RuntimeValue.Undefined;
            }
            environment.DefineRV(param.Name.Lexeme, value);
        }
    }

    /// <summary>
    /// Binds parameters to arguments in an asynchronous context.
    /// </summary>
    /// <param name="parameters">The function's parameter declarations.</param>
    /// <param name="arguments">The arguments provided by the caller.</param>
    /// <param name="environment">The runtime environment to define parameters in.</param>
    /// <param name="interpreter">The interpreter for evaluating default values.</param>
    internal static async Task BindAsync(
        List<Stmt.Parameter> parameters,
        List<object?> arguments,
        RuntimeEnvironment environment,
        Interpreter interpreter)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];

            if (param.IsRest)
            {
                // Rest parameter - collect all remaining arguments
                var restArgs = arguments.Skip(i).ToList();
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                break; // Rest is always last
            }

            object? value;
            bool hasArg = i < arguments.Count;
            bool argIsUndefined = hasArg && arguments[i] is SharpTSUndefined;
            if (hasArg && !argIsUndefined)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue is { } defaultExpr)
            {
                // Per ECMA-262: default value applies when argument is missing OR explicitly undefined.
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = (await interpreter.EvaluateAsync(defaultExpr)).ToObject();
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else
            {
                // Missing argument with no default — use undefined (JavaScript semantics).
                value = SharpTSUndefined.Instance;
            }
            environment.Define(param.Name.Lexeme, value);
        }
    }
}
