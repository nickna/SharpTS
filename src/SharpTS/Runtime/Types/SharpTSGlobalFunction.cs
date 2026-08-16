using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Callable wrapper around a <see cref="GlobalFunctionRegistry"/> handler.
/// Lets global functions (e.g. <c>parseFloat</c>, <c>parseInt</c>,
/// <c>isNaN</c>, <c>setTimeout</c>) be referenced as first-class values —
/// e.g. <c>var pf = parseFloat; typeof parseFloat === 'function';
/// freeParseFloat("1.5")</c> — not just called by name.
/// </summary>
public sealed class SharpTSGlobalFunction : ISharpTSCallable, ITypeCategorized,
    IBuiltInFunctionMetadata
{
    public TypeCategory RuntimeCategory => TypeCategory.Function;

    public string Name { get; }
    private readonly int _arity;
    private readonly BuiltInFunctionMetadata _metadata = new();

    public SharpTSGlobalFunction(string name, int arity = 0)
    {
        Name = name;
        _arity = arity;
    }

    public int Arity() => _arity;
    public string FunctionName => Name;
    public bool HasMetadataProperty(string name) => _metadata.Has(name);
    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public object? Call(Interp interpreter, List<object?> arguments)
    {
        // Build ephemeral literal Expr args wrapping the already-evaluated
        // argument values, then invoke the registered handler.
        var argExprs = new List<Expr>(arguments.Count);
        foreach (var a in arguments)
        {
            argExprs.Add(new Expr.Literal(a));
        }

        if (GlobalFunctionRegistry.Instance.TryGetHandlerV2(Name, out var handlerV2) && handlerV2 != null)
        {
            var task = handlerV2(
                expr => ValueTask.FromResult(interpreter.EvaluateRV(expr)),
                argExprs,
                interpreter);
            return task.GetAwaiter().GetResult().ToObject();
        }

        throw new Exception($"Runtime Error: Global function '{Name}' is not registered.");
    }

    public override string ToString() => $"function {Name}() {{ [native code] }}";
}
