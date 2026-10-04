using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Owns ordered generic parameters of ordinary functions by method identity.
/// Registration stays open through body emission, which can declare nested functions.
/// Empty declarations represent non-generic functions independently of lookup aliases.
/// </summary>
public sealed class FunctionGenericParameterRegistry
{
    private readonly Dictionary<MethodBuilder, IReadOnlyList<GenericTypeParameterBuilder>> _parameters =
        new(ReferenceEqualityComparer.Instance);

    public bool IsComplete { get; private set; }

    public IReadOnlyList<GenericTypeParameterBuilder> Require(MethodBuilder owner)
        => _parameters.TryGetValue(owner, out var parameters) ? parameters
            : throw new InvalidOperationException("Function generic parameters have not been declared.");

    internal void Declare(MethodBuilder owner, IEnumerable<GenericTypeParameterBuilder> parameters)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(parameters);
        var snapshot = parameters.ToArray();
        Validate(owner, snapshot);
        if (!_parameters.TryAdd(owner, Array.AsReadOnly(snapshot)))
            throw new InvalidOperationException("Function generic parameters are already declared.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        foreach (var (owner, parameters) in _parameters)
            Validate(owner, parameters);
        IsComplete = true;
    }

    private static void Validate(MethodBuilder owner, IReadOnlyList<GenericTypeParameterBuilder> parameters)
    {
        var declared = owner.GetGenericArguments() ?? Type.EmptyTypes;
        if (declared.Length != parameters.Count)
            throw new InvalidOperationException("Function generic parameter arity does not match its owner.");
        for (int i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter is null || parameter.GenericParameterPosition != i || !ReferenceEquals(parameter, declared[i]))
                throw new InvalidOperationException("Function generic parameters must match their owner and declaration order.");
        }
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Function generic parameter metadata is complete.");
    }
}
