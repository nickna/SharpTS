using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.Runtime;

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
public class TypeEnvironment : ScopeChain<TypeInfo, TypeEnvironment>
{
    // TypeScript symbols have independent type and value facets.  Keeping these
    // bindings separate is essential for declarations such as
    // `interface Error { ... }` + `declare var Error: ErrorConstructor`.
    private readonly Dictionary<string, TypeInfo> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Definition, TypeNode? DefinitionNode)> _typeAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Definition, List<string> TypeParams, TypeNode? DefinitionNode)> _genericTypeAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeInfo> _typeParameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeInfo.Namespace> _namespaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (TypeInfo Type, bool IsValue)> _importAliases = new(StringComparer.Ordinal);

    public TypeEnvironment(TypeEnvironment? enclosing = null, bool? strictMode = null)
        : base(enclosing, strictMode)
    {
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
        return Enclosing?.GetTypeParameter(name);
    }

    public override TypeInfo? Get(string name)
    {
        // Check type parameters first (for generic body checking)
        if (_typeParameters.TryGetValue(name, out var typeParam))
            return typeParam;

        return base.Get(name);
    }

    /// <summary>Defines the type facet of a symbol in the current scope.</summary>
    public void DefineType(string name, TypeInfo type) => _types[name] = type;

    /// <summary>Gets a type facet, searching outward through enclosing scopes.</summary>
    public TypeInfo? GetTypeBinding(string name)
    {
        if (_typeParameters.TryGetValue(name, out var typeParam))
            return typeParam;
        if (_types.TryGetValue(name, out var type))
            return type;
        return Enclosing?.GetTypeBinding(name);
    }

    public TypeInfo? GetLocalTypeBinding(string name) =>
        _types.GetValueOrDefault(name) ?? _typeParameters.GetValueOrDefault(name);

    public bool IsTypeDefined(string name) =>
        _types.ContainsKey(name) || _typeParameters.ContainsKey(name)
        || (Enclosing?.IsTypeDefined(name) ?? false);

    public bool IsTypeDefinedLocally(string name) =>
        _types.ContainsKey(name) || _typeParameters.ContainsKey(name);

    /// <summary>The type-facet names defined directly in this scope.</summary>
    public IEnumerable<string> TypeNames => _types.Keys;

    /// <summary>
    /// Marks a variable as const (read-only). Used for named function expressions
    /// where the function name cannot be reassigned inside the function body.
    /// </summary>
    public void MarkAsConst(string name) => MarkAsReadOnly(name);

    /// <summary>
    /// Checks if a variable is marked as const in the current or enclosing scopes.
    /// </summary>
    public bool IsConst(string name) => IsReadOnly(name);

    public void Assign(Token name, TypeInfo type)
    {
        if (_values.ContainsKey(name.Lexeme))
        {
            // In a stricter system, we might check if the re-assignment type matches the declared type here
            // But usually we just want to look up the existing declared type.
            return;
        }

        if (Enclosing != null)
        {
            Enclosing.Assign(name, type);
            return;
        }

        // Variable not defined, will be caught by Get() usually
    }

    // Type alias support. <paramref name="definitionNode"/> is the structured form of the
    // definition when the parser produced one (type-AST migration); the string stays stored —
    // it keys the checker's expansion cache and discriminates same-named aliases across scopes.
    public void DefineTypeAlias(string name, string definition, TypeNode? definitionNode = null)
    {
        _typeAliases[name] = (definition, definitionNode);
    }

    public (string Definition, TypeNode? DefinitionNode)? GetTypeAlias(string name)
    {
        if (_typeAliases.TryGetValue(name, out var alias))
            return alias;
        return Enclosing?.GetTypeAlias(name);
    }

    /// <summary>
    /// Defines a generic type alias with type parameters. <paramref name="definitionNode"/> is
    /// the structured form of the definition when the parser produced one (type-AST migration);
    /// the string stays authoritative.
    /// </summary>
    public void DefineGenericTypeAlias(string name, string definition, List<string> typeParams, TypeNode? definitionNode = null)
    {
        _genericTypeAliases[name] = (definition, typeParams, definitionNode);
    }

    /// <summary>
    /// Gets a generic type alias definition by name.
    /// </summary>
    public (string Definition, List<string> TypeParams, TypeNode? DefinitionNode)? GetGenericTypeAlias(string name)
    {
        // Generic alias expansion can create a deep chain of short-lived type
        // parameter scopes. Walk iteratively so a valid recursive declaration
        // cannot overflow the CLR stack during an outward lookup.
        var visited = new HashSet<TypeEnvironment>(ReferenceEqualityComparer.Instance);
        for (TypeEnvironment? environment = this;
             environment != null && visited.Add(environment);
             environment = environment.Enclosing)
        {
            if (environment._genericTypeAliases.TryGetValue(name, out var alias))
                return alias;
        }
        return null;
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
            _values[name] = mergedNs;
            _types[name] = mergedNs;
        }
        else
        {
            _namespaces[name] = ns;
            // Also define in values so it can be looked up via Get()
            _values[name] = ns;
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
        return Enclosing?.GetNamespace(name);
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
        _types[name] = type;
        if (isValue)
            _values[name] = type;
    }

    /// <summary>
    /// Gets an import alias by name, searching up the scope chain.
    /// Returns the resolved type and whether it's a value alias.
    /// </summary>
    public (TypeInfo Type, bool IsValue)? GetImportAlias(string name)
    {
        if (_importAliases.TryGetValue(name, out var alias))
            return alias;
        return Enclosing?.GetImportAlias(name);
    }

    /// <summary>
    /// Checks if a name is an import alias in the current or enclosing scopes.
    /// </summary>
    public bool IsImportAlias(string name)
    {
        if (_importAliases.ContainsKey(name)) return true;
        return Enclosing?.IsImportAlias(name) ?? false;
    }
}
