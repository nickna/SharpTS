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
            if (i < arguments.Count)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue != null)
            {
                // Evaluate default value in the function's environment
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = interpreter.Evaluate(param.DefaultValue!);
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else if (param.IsOptional)
            {
                // Optional parameter with no argument and no default - use null
                value = null;
            }
            else
            {
                throw new Exception($"Missing required argument for parameter '{param.Name.Lexeme}'.");
            }
            environment.Define(param.Name.Lexeme, value);
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
            if (i < arguments.Count)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue != null)
            {
                // Evaluate default value in the function's environment
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = await interpreter.EvaluateAsync(param.DefaultValue!);
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else if (param.IsOptional)
            {
                // Optional parameter with no argument and no default - use null
                value = null;
            }
            else
            {
                throw new Exception($"Missing required argument for parameter '{param.Name.Lexeme}'.");
            }
            environment.Define(param.Name.Lexeme, value);
        }
    }
}
