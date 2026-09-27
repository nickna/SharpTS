using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Owns generic parameter declarations for class declarations and expressions.
/// Empty declarations explicitly represent non-generic classes. References remain
/// readable during nested body emission; completion closes registration.
/// </summary>
public sealed class ClassGenericParameterRegistry
{
    private readonly Dictionary<TypeBuilder, IReadOnlyList<GenericTypeParameterBuilder>> _parameters =
        new(ReferenceEqualityComparer.Instance);

    public bool IsComplete { get; private set; }

    public IReadOnlyList<GenericTypeParameterBuilder> Require(TypeBuilder owner)
        => _parameters.TryGetValue(owner, out var parameters) ? parameters
            : throw new InvalidOperationException("Class generic parameters have not been declared.");

    internal void Declare(TypeBuilder owner, IEnumerable<GenericTypeParameterBuilder> parameters)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(parameters);
        var snapshot = parameters.ToArray();
        Validate(owner, snapshot);
        if (!_parameters.TryAdd(owner, Array.AsReadOnly(snapshot)))
            throw new InvalidOperationException("Class generic parameters are already declared.");
    }

    internal void CompleteEmission(IEnumerable<TypeBuilder> owners)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(owners);
        var expected = new HashSet<TypeBuilder>(owners, ReferenceEqualityComparer.Instance);
        if (expected.Count != _parameters.Count || expected.Any(owner => !_parameters.ContainsKey(owner)))
            throw new InvalidOperationException("Class generic parameter declarations are incomplete.");
        foreach (var (owner, parameters) in _parameters)
            Validate(owner, parameters);
        IsComplete = true;
    }

    private static void Validate(TypeBuilder owner, IReadOnlyList<GenericTypeParameterBuilder> parameters)
    {
        var declared = owner.GetGenericArguments() ?? Type.EmptyTypes;
        if (declared.Length != parameters.Count)
            throw new InvalidOperationException("Class generic parameter arity does not match its owner.");
        for (int i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            // Persisted parameter builders may not expose DeclaringType. Identity
            // in the owner's ordered argument list proves ownership directly.
            if (parameter is null || parameter.GenericParameterPosition != i || !ReferenceEquals(parameter, declared[i]))
                throw new InvalidOperationException("Class generic parameters must match their owner and declaration order.");
        }
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Class generic parameter metadata is complete.");
    }
}
