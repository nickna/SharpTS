using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for generator function declarations (function*).
/// </summary>
/// <remarks>
/// When called, this does NOT execute the function body immediately.
/// Instead, it returns a <see cref="SharpTSGenerator"/> instance that
/// lazily executes the body as next() is called.
/// </remarks>
/// <seealso cref="SharpTSGenerator"/>
/// <seealso cref="SharpTSFunction"/>
public class SharpTSGeneratorFunction(Stmt.Function declaration, RuntimeEnvironment closure) : ISharpTSCallable
{
    private readonly Stmt.Function _declaration = declaration;
    private readonly RuntimeEnvironment _closure = closure;

    public int Arity() => _declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);

    /// <summary>
    /// Creates a new generator instance. Does NOT execute the function body.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Create environment and bind parameters (like a regular function)
        RuntimeEnvironment environment = new(_closure);
        for (int i = 0; i < _declaration.Parameters.Count; i++)
        {
            var param = _declaration.Parameters[i];

            if (param.IsRest)
            {
                var restArgs = arguments.Skip(i).ToList();
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                break;
            }

            object? value;
            if (i < arguments.Count)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue != null)
            {
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
                value = null;
            }
            else
            {
                throw new Exception($"Missing required argument for parameter '{param.Name.Lexeme}'.");
            }
            environment.Define(param.Name.Lexeme, value);
        }

        // Return a generator that will execute the body lazily
        return new SharpTSGenerator(_declaration, environment, interpreter);
    }

    /// <summary>
    /// Creates a bound version with 'this' set for method calls.
    /// </summary>
    public SharpTSGeneratorFunction Bind(SharpTSInstance instance)
    {
        RuntimeEnvironment boundEnv = new(_closure);
        boundEnv.Define("this", instance);
        return new SharpTSGeneratorFunction(_declaration, boundEnv);
    }

    public override string ToString() => $"<generator fn {_declaration.Name.Lexeme}>";
}
