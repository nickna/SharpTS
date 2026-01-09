using SharpTS.Execution;
using SharpTS.Parsing;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime object representing an async generator function declaration.
/// </summary>
/// <remarks>
/// Created for declarations using both 'async' and 'function*' syntax.
/// When called, instantiates a <see cref="SharpTSAsyncGenerator"/>.
/// Async generators yield Promises and can use 'await' internally.
/// </remarks>
/// <seealso cref="SharpTSAsyncGenerator"/>
/// <seealso cref="SharpTSGeneratorFunction"/>
public class SharpTSAsyncGeneratorFunction : ISharpTSCallable
{
    private readonly Stmt.Function _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;

    public SharpTSAsyncGeneratorFunction(Stmt.Function declaration, RuntimeEnvironment closure)
    {
        _declaration = declaration;
        _closure = closure;
        _arity = declaration.Parameters?.Count ?? 0;
    }

    public int Arity() => _arity;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Create a new environment for this generator invocation
        RuntimeEnvironment environment = new(_closure);

        // Bind parameters to arguments
        if (_declaration.Parameters != null)
        {
            for (int i = 0; i < _declaration.Parameters.Count; i++)
            {
                var param = _declaration.Parameters[i];
                object? value = i < arguments.Count ? arguments[i] : param.DefaultValue;

                // Evaluate default value if needed
                if (value == null && param.DefaultValue != null)
                {
                    value = interpreter.Evaluate(param.DefaultValue);
                }

                environment.Define(param.Name.Lexeme, value);
            }
        }

        // Return the async generator object (not yet started)
        return new SharpTSAsyncGenerator(_declaration, environment, interpreter);
    }

    public override string ToString() => $"[async function* {_declaration.Name?.Lexeme ?? "anonymous"}]";
}
