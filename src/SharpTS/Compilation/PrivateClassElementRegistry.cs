using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using System.Reflection;

namespace SharpTS.Compilation;

/// <summary>Owns private class declarations by generated type identity.</summary>
public sealed class PrivateClassElementRegistry
{
    private readonly Dictionary<string, TypeBuilder> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<TypeBuilder, PrivateClassElements> _declarations = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<TypeBuilder> _emitted = new(ReferenceEqualityComparer.Instance);

    internal bool IsComplete { get; private set; }

    public bool TryGet(string name, [NotNullWhen(true)] out PrivateClassElements? declaration)
    {
        declaration = _names.TryGetValue(name, out var owner) ? _declarations[owner] : null;
        return declaration is not null;
    }

    internal PrivateClassElements Require(string name) => TryGet(name, out var declaration)
        ? declaration : throw new InvalidOperationException($"Private class elements have not been declared for '{name}'.");

    internal void Declare(string name, TypeBuilder owner, FieldBuilder? storage,
        IEnumerable<string> fieldNames, IReadOnlyDictionary<string, FieldBuilder> staticFields,
        IReadOnlyDictionary<string, MethodBuilder> methods, IReadOnlyDictionary<string, MethodBuilder> staticMethods,
        PrivateInstanceBridge? instanceBridge = null)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(owner);
        var declaration = new PrivateClassElements(storage, fieldNames, staticFields, methods, staticMethods, instanceBridge);
        var allMethods = declaration.Methods.Values.Concat(declaration.StaticMethods.Values).ToArray();
        if (_names.ContainsKey(name) || _declarations.ContainsKey(owner)
            || (storage is not null && (storage.DeclaringType != owner || !storage.IsStatic))
            || (storage is null && (declaration.FieldNames.Count > 0 || declaration.Methods.Count > 0))
            || (instanceBridge is not null && !instanceBridge.IsValid(owner, storage, methods))
            || declaration.FieldNames.Any(string.IsNullOrEmpty)
            || declaration.FieldNames.Distinct(StringComparer.Ordinal).Count() != declaration.FieldNames.Count
            || declaration.StaticFields.Any(pair => string.IsNullOrEmpty(pair.Key) || pair.Value is null || pair.Value.DeclaringType != owner || !pair.Value.IsStatic)
            || declaration.Methods.Any(pair => string.IsNullOrEmpty(pair.Key) || pair.Value is null || pair.Value.DeclaringType != owner || pair.Value.IsStatic)
            || declaration.StaticMethods.Any(pair => string.IsNullOrEmpty(pair.Key) || pair.Value is null || pair.Value.DeclaringType != owner || !pair.Value.IsStatic)
            || allMethods.Distinct(ReferenceEqualityComparer.Instance).Count() != allMethods.Length)
            throw new InvalidOperationException("Invalid or duplicate private class element declaration.");
        _declarations.Add(owner, declaration);
        _names.Add(name, owner);
    }

    internal void MarkBodiesEmitted(string name)
    {
        EnsureMutable();
        var declaration = Require(name);
        if (declaration.Methods.Values.Concat(declaration.StaticMethods.Values).Any(method => method.GetILGenerator().ILOffset == 0)
            || declaration.MethodValues.Values.Any(value => value.Method.GetILGenerator().ILOffset == 0
                || value.Constructor.GetILGenerator().ILOffset == 0 || value.Initializer?.GetILGenerator().ILOffset == 0)
            || (declaration.InstanceBridge is { } bridge && bridge.StorageImplementation.GetILGenerator().ILOffset == 0)
            || !_emitted.Add(_names[name]))
            throw new InvalidOperationException("Private class method bodies are empty or already emitted.");
    }

    internal void DeclareMethodValue(string owner, string name, PrivateMethodValue value)
    {
        EnsureMutable();
        var declaration = Require(owner);
        var methods = value.Source.IsStatic ? declaration.StaticMethods : declaration.Methods;
        if (_emitted.Contains(_names[owner]) || !value.Source.IsPrivate || !methods.ContainsKey(name)
            || value.Type.Module != _names[owner].Module || value.Type.IsGenericType || value.Method.IsGenericMethod
            || value.Method.DeclaringType != value.Type
            || value.Method.IsStatic || value.Constructor.DeclaringType != value.Type
            || value.Method.GetParameters().FirstOrDefault()?.ParameterType != typeof(object)
            || value.Method.GetParameters().Length != value.Source.Parameters.Count + 1
            || value.Constructor.GetParameters().Length != (value.Definition == null ? 0 : 1)
            || (value.Definition != null && value.Constructor.GetParameters()[0].ParameterType != value.Definition.FieldType)
            || (value.Cache != null) != (value.Initializer != null)
            || (value.Cache != null && (value.Cache.DeclaringType != value.Type || !value.Cache.IsStatic))
            || (value.Initializer != null && (value.Initializer.DeclaringType != value.Type || !value.Initializer.IsStatic))
            || (value.Cache == null && (value.Definition?.DeclaringType != value.Type || value.Definition.IsStatic))
            || !declaration.MutableMethodValues.TryAdd(name, value))
            throw new InvalidOperationException("Invalid or duplicate private method value declaration.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        if (_emitted.Count != _declarations.Count)
            throw new InvalidOperationException("Not all private class element bodies have been emitted.");
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Private class element metadata is complete.");
    }
}

/// <summary>Immutable declaration views; field order and builder identity are preserved.</summary>
public sealed class PrivateClassElements
{
    internal Dictionary<string, PrivateMethodValue> MutableMethodValues { get; } = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, PrivateMethodValue> MethodValues { get; }
    public PrivateInstanceBridge? InstanceBridge { get; }
    public FieldBuilder? Storage { get; }
    public IReadOnlyList<string> FieldNames { get; }
    public IReadOnlyDictionary<string, FieldBuilder> StaticFields { get; }
    public IReadOnlyDictionary<string, MethodBuilder> Methods { get; }
    public IReadOnlyDictionary<string, MethodBuilder> StaticMethods { get; }

    internal PrivateClassElements(FieldBuilder? storage, IEnumerable<string> fieldNames,
        IReadOnlyDictionary<string, FieldBuilder> staticFields, IReadOnlyDictionary<string, MethodBuilder> methods,
        IReadOnlyDictionary<string, MethodBuilder> staticMethods, PrivateInstanceBridge? instanceBridge)
    {
        MethodValues = new ReadOnlyDictionary<string, PrivateMethodValue>(MutableMethodValues);
        InstanceBridge = instanceBridge;
        Storage = storage;
        FieldNames = Array.AsReadOnly(fieldNames.ToArray());
        StaticFields = new ReadOnlyDictionary<string, FieldBuilder>(new Dictionary<string, FieldBuilder>(staticFields, StringComparer.Ordinal));
        Methods = new ReadOnlyDictionary<string, MethodBuilder>(new Dictionary<string, MethodBuilder>(methods, StringComparer.Ordinal));
        StaticMethods = new ReadOnlyDictionary<string, MethodBuilder>(new Dictionary<string, MethodBuilder>(staticMethods, StringComparer.Ordinal));
    }
}

public sealed record PrivateMethodValue(TypeBuilder Type, ConstructorBuilder Constructor,
    MethodBuilder Method, FieldBuilder? Cache, ConstructorBuilder? Initializer, SharpTS.Parsing.Stmt.Function Source,
    FieldBuilder? Definition = null);

/// <summary>Non-generic dispatch surface for a generic class's private instance elements.</summary>
public sealed class PrivateInstanceBridge
{
    public Type InterfaceType { get; }
    public MethodInfo StorageGetter { get; }
    public IReadOnlyDictionary<string, MethodInfo> Methods { get; }
    internal MethodBuilder StorageImplementation { get; }

    internal PrivateInstanceBridge(Type interfaceType, MethodInfo storageGetter,
        MethodBuilder storageImplementation, IReadOnlyDictionary<string, MethodInfo> methods)
    {
        InterfaceType = interfaceType;
        StorageGetter = storageGetter;
        StorageImplementation = storageImplementation;
        Methods = new ReadOnlyDictionary<string, MethodInfo>(new Dictionary<string, MethodInfo>(methods, StringComparer.Ordinal));
    }

    internal bool IsValid(TypeBuilder owner, FieldBuilder? storage, IReadOnlyDictionary<string, MethodBuilder> methods)
        => owner.IsGenericTypeDefinition && storage is not null && InterfaceType.IsInterface && !InterfaceType.IsGenericType
            && StorageGetter.DeclaringType == InterfaceType && StorageGetter.IsAbstract && !StorageGetter.IsStatic
            && StorageGetter.ReturnType == storage.FieldType && StorageGetter.GetParameters().Length == 0
            && StorageImplementation.DeclaringType == owner && !StorageImplementation.IsStatic && StorageImplementation.IsVirtual && StorageImplementation.IsFinal
            && StorageImplementation.ReturnType == storage.FieldType && StorageImplementation.GetParameters().Length == 0
            && Methods.Count == methods.Count && Methods.All(pair =>
                pair.Value.DeclaringType == InterfaceType && pair.Value.IsAbstract && !pair.Value.IsStatic
                && methods.TryGetValue(pair.Key, out var method) && method.IsVirtual && method.IsFinal
                && pair.Value.ReturnType == method.ReturnType
                && pair.Value.GetParameters().Select(p => p.ParameterType).SequenceEqual(method.GetParameters().Select(p => p.ParameterType)));
}
