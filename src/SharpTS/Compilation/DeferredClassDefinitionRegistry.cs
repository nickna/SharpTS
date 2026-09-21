using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Owns deferred class initializers and captured computed keys for one compilation.
/// Declarations remain readable while bodies are emitted, including nested classes;
/// completion closes registration and checks every forward-declared initializer.
/// </summary>
public sealed class DeferredClassDefinitionRegistry
{
    private readonly Dictionary<TypeBuilder, DeferredClassDefinition> _byType = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, DeferredClassDefinition> _bySource = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Field, FieldBuilder> _fieldKeys = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<DeferredClassDefinition> _emitted = new(ReferenceEqualityComparer.Instance);

    public bool IsComplete { get; private set; }

    public bool TryGet(TypeBuilder owner, [NotNullWhen(true)] out DeferredClassDefinition? definition)
        => _byType.TryGetValue(owner, out definition);

    public bool TryGet(object source, [NotNullWhen(true)] out DeferredClassDefinition? definition)
        => _bySource.TryGetValue(source, out definition);

    internal FieldBuilder RequireFieldKey(Stmt.Field field)
        => _fieldKeys.TryGetValue(field, out var key) ? key
            : throw new InvalidOperationException("The computed field key has not been declared.");

    internal bool TryGetFieldKey(Stmt.Field field, [NotNullWhen(true)] out FieldBuilder? key)
        => _fieldKeys.TryGetValue(field, out key);

    internal DeferredClassDefinition Declare(object source, TypeBuilder owner, MethodBuilder initializer,
        MethodBuilder registrar, IEnumerable<Expr> keys, IReadOnlyDictionary<Stmt.Field, FieldBuilder> fieldKeys)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(initializer);
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(fieldKeys);
        var snapshot = keys.ToArray();
        if (source is not (Stmt.Class or Expr.ClassExpr)
            || _bySource.ContainsKey(source) || _byType.ContainsKey(owner)
            || initializer.DeclaringType != owner || !initializer.IsStatic || !registrar.IsStatic
            || (!owner.IsGenericTypeDefinition && registrar.DeclaringType != owner)
            || registrar.Module != owner.Module || ReferenceEquals(initializer, registrar)
            || snapshot.Length == 0 || snapshot.Any(key => key is null))
            throw new InvalidOperationException("Invalid or duplicate deferred class declaration.");
        var sourceFields = source is Stmt.Class declaration ? declaration.Fields : ((Expr.ClassExpr)source).Fields;
        var expected = sourceFields.Where(field => field.ComputedKey != null && !field.IsDeclare).ToArray();
        if (fieldKeys.Count != expected.Length || expected.Any(field => !fieldKeys.ContainsKey(field)))
            throw new InvalidOperationException("Deferred class keys must include every computed field.");
        foreach (var (field, key) in fieldKeys)
        {
            if (key is null || _fieldKeys.ContainsKey(field) || !key.IsStatic
                || !expected.Any(expectedField => ReferenceEquals(expectedField, field))
                || key.DeclaringType != registrar.DeclaringType
                || !snapshot.Any(expression => ReferenceEquals(expression, field.ComputedKey)))
                throw new InvalidOperationException("Invalid or duplicate computed field key declaration.");
        }
        var definition = new DeferredClassDefinition(owner, initializer, registrar, Array.AsReadOnly(snapshot));
        _byType.Add(owner, definition);
        _bySource.Add(source, definition);
        foreach (var (field, key) in fieldKeys)
            _fieldKeys.Add(field, key);
        return definition;
    }

    internal void MarkInitializerEmitted(DeferredClassDefinition definition)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);
        if (!_byType.TryGetValue(definition.Owner, out var declared) || !ReferenceEquals(declared, definition)
            || definition.Initializer.GetILGenerator().ILOffset == 0
            || definition.Registrar.GetILGenerator().ILOffset == 0 || !_emitted.Add(definition))
            throw new InvalidOperationException("The deferred class initializer is foreign, empty, or already emitted.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        if (_emitted.Count != _byType.Count)
            throw new InvalidOperationException("Not all deferred class initializers have been emitted.");
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Deferred class metadata is complete.");
    }
}

/// <summary>Immutable forward references shared by declaration and expression emitters.</summary>
public sealed class DeferredClassDefinition
{
    internal DeferredClassDefinition(TypeBuilder owner, MethodBuilder initializer, MethodBuilder registrar,
        IReadOnlyList<Expr> keys)
    {
        Owner = owner;
        Initializer = initializer;
        Registrar = registrar;
        Keys = keys;
    }

    public TypeBuilder Owner { get; }
    public MethodBuilder Initializer { get; }
    public MethodBuilder Registrar { get; }
    public IReadOnlyList<Expr> Keys { get; }
}
