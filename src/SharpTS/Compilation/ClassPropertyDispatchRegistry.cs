using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Owns forward declarations for generated class property dispatch. Qualified
/// names are lookup aliases; each type owns one immutable declaration.
/// </summary>
internal sealed class ClassPropertyDispatchRegistry
{
    private readonly Dictionary<string, TypeBuilder> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<TypeBuilder, ClassPropertyDispatch> _declarations = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<TypeBuilder> _emitted = new(ReferenceEqualityComparer.Instance);

    internal bool IsComplete { get; private set; }

    internal bool Contains(string name) => _names.ContainsKey(name);

    internal bool TryGet(string name, [NotNullWhen(true)] out ClassPropertyDispatch? declaration)
    {
        declaration = _names.TryGetValue(name, out var owner) ? _declarations[owner] : null;
        return declaration is not null;
    }

    internal ClassPropertyDispatch Require(string name)
        => TryGet(name, out var declaration) ? declaration
            : throw new InvalidOperationException($"Class property dispatch has not been declared for '{name}'.");

    internal void Declare(string name, TypeBuilder owner, ClassPropertyDispatch declaration)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(declaration);
        MethodBuilder[] methods = [declaration.EnsureFields, declaration.GetFields,
            declaration.GetProperty, declaration.SetProperty, declaration.HasProperty];
        if (_names.ContainsKey(name) || _declarations.ContainsKey(owner)
            || declaration.FieldsField is null || declaration.FieldsField.DeclaringType != owner
            || methods.Any(method => method is null || method.DeclaringType != owner || method.IsStatic)
            || methods.Distinct(ReferenceEqualityComparer.Instance).Count() != methods.Length)
            throw new InvalidOperationException("Invalid or duplicate class property dispatch declaration.");
        _declarations.Add(owner, declaration);
        _names.Add(name, owner);
    }

    internal void MarkBodiesEmitted(string name)
    {
        EnsureMutable();
        var declaration = Require(name);
        MethodBuilder[] methods = [declaration.EnsureFields, declaration.GetFields,
            declaration.GetProperty, declaration.SetProperty, declaration.HasProperty];
        if (methods.Any(method => method.GetILGenerator().ILOffset == 0) || !_emitted.Add(_names[name]))
            throw new InvalidOperationException("Class property dispatch bodies are empty or already emitted.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        if (_emitted.Count != _declarations.Count)
            throw new InvalidOperationException("Not all class property dispatch bodies have been emitted.");
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Class property dispatch metadata is complete.");
    }
}

/// <summary>Canonical method and storage references for one generated class.</summary>
internal sealed record ClassPropertyDispatch(
    MethodBuilder EnsureFields,
    MethodBuilder GetFields,
    MethodBuilder GetProperty,
    MethodBuilder SetProperty,
    MethodBuilder HasProperty,
    FieldInfo FieldsField);
