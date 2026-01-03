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
    private readonly Dictionary<string, TypeInfo> _types = [];
    private readonly Dictionary<string, string> _typeAliases = [];
    private readonly Dictionary<string, TypeInfo> _typeParameters = [];
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
}
