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
public class SharpTSGeneratorFunction : ISharpTSCallable
{
    private readonly Stmt.Function _declaration;
    private readonly RuntimeEnvironment _closure;
    private readonly int _arity;

    public SharpTSGeneratorFunction(Stmt.Function declaration, RuntimeEnvironment closure)
    {
        _declaration = declaration;
        _closure = closure;
        _arity = declaration.Parameters.Count(p => p.DefaultValue == null && !p.IsRest && !p.IsOptional);
    }

    public int Arity() => _arity;

    /// <summary>
    /// Creates a new generator instance. Does NOT execute the function body.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Create environment and bind parameters (like a regular function)
        RuntimeEnvironment environment = new(_closure);
        ParameterBinder.Bind(_declaration.Parameters, arguments, environment, interpreter);

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
