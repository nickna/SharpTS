using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Manages type scopes during static type checking (compile-time).
/// </summary>
/// <remarks>
/// The compile-time counterpart to <see cref="RuntimeEnvironment"/>. Implements a linked
/// list of scopes for type bindings. Used by <see cref="TypeChecker"/> to track variable
/// types, class/interface definitions, and type aliases during static analysis. Type lookup
/// walks up the scope chain via the enclosing reference. This environment is completely
/// separate from runtime—types are checked before execution begins.
/// </remarks>
/// <seealso cref="RuntimeEnvironment"/>
/// <seealso cref="TypeInfo"/>
public class TypeEnvironment(TypeEnvironment? enclosing = null)
{
    private readonly Dictionary<string, TypeInfo> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _typeAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Definition, List<string> TypeParams)> _genericTypeAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeInfo> _typeParameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeInfo.Namespace> _namespaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (TypeInfo Type, bool IsValue)> _importAliases = new(StringComparer.Ordinal);
    private readonly TypeEnvironment? _enclosing = enclosing;

    public void Define(string name, TypeInfo type)
    {
        _types[name] = type;
    }

    /// <summary>
    /// Defines a type parameter in the current scope (e.g., T in &lt;T&gt;).
    /// </summary>
    public void DefineTypeParameter(string name, TypeInfo typeParam)
    {
        _typeParameters[name] = typeParam;
    }

    /// <summary>
    /// Looks up a type parameter by name, searching up the scope chain.
    /// </summary>
    public TypeInfo? GetTypeParameter(string name)
    {
        if (_typeParameters.TryGetValue(name, out var typeParam))
            return typeParam;
        return _enclosing?.GetTypeParameter(name);
    }

    public TypeInfo? Get(string name)
    {
        // Check type parameters first (for generic body checking)
        if (_typeParameters.TryGetValue(name, out var typeParam))
            return typeParam;

        if (_types.TryGetValue(name, out TypeInfo? type))
        {
            return type;
        }

        if (_enclosing != null) return _enclosing.Get(name);

        return null;
    }

    public bool IsDefined(string name)
    {
        if (_types.ContainsKey(name)) return true;
        return _enclosing?.IsDefined(name) ?? false;
    }

    /// <summary>
    /// Checks if a name is defined in the current scope only (not in enclosing scopes).
    /// Used for function hoisting to avoid re-defining already hoisted functions.
    /// </summary>
    public bool IsDefinedLocally(string name)
    {
        return _types.ContainsKey(name);
    }

    public void Assign(Token name, TypeInfo type)
    {
        if (_types.ContainsKey(name.Lexeme))
        {
            // In a stricter system, we might check if the re-assignment type matches the declared type here
            // But usually we just want to look up the existing declared type.
            return;
        }

        if (_enclosing != null)
        {
            _enclosing.Assign(name, type);
            return;
        }

        // Variable not defined, will be caught by Get() usually
    }

    // Type alias support
    public void DefineTypeAlias(string name, string definition)
    {
        _typeAliases[name] = definition;
    }

    public string? GetTypeAlias(string name)
    {
        if (_typeAliases.TryGetValue(name, out var definition))
            return definition;
        return _enclosing?.GetTypeAlias(name);
    }

    /// <summary>
    /// Defines a generic type alias with type parameters.
    /// </summary>
    public void DefineGenericTypeAlias(string name, string definition, List<string> typeParams)
    {
        _genericTypeAliases[name] = (definition, typeParams);
    }

    /// <summary>
    /// Gets a generic type alias definition by name.
    /// </summary>
    public (string Definition, List<string> TypeParams)? GetGenericTypeAlias(string name)
    {
        if (_genericTypeAliases.TryGetValue(name, out var alias))
            return alias;
        return _enclosing?.GetGenericTypeAlias(name);
    }

    /// <summary>
    /// Defines or merges a namespace in the current scope.
    /// If a namespace with the same name already exists, merges the members.
    /// </summary>
    public void DefineNamespace(string name, TypeInfo.Namespace ns)
    {
        if (_namespaces.TryGetValue(name, out var existing))
        {
            // Merge: combine types and values from both namespace declarations
            // Create new merged dictionaries since FrozenDictionary is immutable
            var mergedTypes = new Dictionary<string, TypeInfo>(existing.Types);
            foreach (var (k, v) in ns.Types)
                mergedTypes[k] = v;

            var mergedValues = new Dictionary<string, TypeInfo>(existing.Values);
            foreach (var (k, v) in ns.Values)
                mergedValues[k] = v;

            // Create new namespace with merged collections
            var mergedNs = new TypeInfo.Namespace(name, mergedTypes.ToFrozenDictionary(), mergedValues.ToFrozenDictionary());
            _namespaces[name] = mergedNs;
            _types[name] = mergedNs;
        }
        else
        {
            _namespaces[name] = ns;
            // Also define in types so it can be looked up via Get()
            _types[name] = ns;
        }
    }

    /// <summary>
    /// Gets a namespace by name, searching up the scope chain.
    /// </summary>
    public TypeInfo.Namespace? GetNamespace(string name)
    {
        if (_namespaces.TryGetValue(name, out var ns))
            return ns;
        return _enclosing?.GetNamespace(name);
    }

    /// <summary>
    /// Defines an import alias in the current scope.
    /// Import aliases create local names for namespace members (import X = Namespace.Member).
    /// </summary>
    /// <param name="name">The alias name</param>
    /// <param name="type">The resolved type of the aliased member</param>
    /// <param name="isValue">True if this is a value alias (function, class, variable, enum)</param>
    public void DefineImportAlias(string name, TypeInfo type, bool isValue)
    {
        _importAliases[name] = (type, isValue);
        // Also define as a regular type so it can be looked up via Get()
        _types[name] = type;
    }

    /// <summary>
    /// Gets an import alias by name, searching up the scope chain.
    /// Returns the resolved type and whether it's a value alias.
    /// </summary>
    public (TypeInfo Type, bool IsValue)? GetImportAlias(string name)
    {
        if (_importAliases.TryGetValue(name, out var alias))
            return alias;
        return _enclosing?.GetImportAlias(name);
    }

    /// <summary>
    /// Checks if a name is an import alias in the current or enclosing scopes.
    /// </summary>
    public bool IsImportAlias(string name)
    {
        if (_importAliases.ContainsKey(name)) return true;
        return _enclosing?.IsImportAlias(name) ?? false;
    }
}
