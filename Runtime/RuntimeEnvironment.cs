using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime;

/// <summary>
/// Manages variable scopes during runtime interpretation.
/// </summary>
/// <remarks>
/// Implements a linked list of scopes via the <see cref="ScopeChain{TValue,TSelf}.Enclosing"/> property.
/// Each scope holds variable bindings in a dictionary. Variable lookup (Get) and
/// assignment (Assign) walk up the scope chain until found. Used by <see cref="Interpreter"/>
/// for lexical scoping and by <see cref="SharpTSFunction"/> for closures.
/// </remarks>
/// <seealso cref="TypeEnvironment"/>
public class RuntimeEnvironment : ScopeChain<object?, RuntimeEnvironment>
{
    private readonly Dictionary<string, SharpTSNamespace> _namespaces = [];

    public RuntimeEnvironment(RuntimeEnvironment? enclosing = null, bool? strictMode = null)
        : base(enclosing, strictMode)
    {
    }

    public object? Get(Token name)
    {
        if (_values.TryGetValue(name.Lexeme, out object? value))
        {
            return value;
        }

        if (Enclosing != null) return Enclosing.Get(name);

        throw new Exception($"Undefined variable '{name.Lexeme}'.");
    }

    /// <summary>
    /// Attempts to get a variable value in a single scope chain traversal.
    /// More efficient than IsDefined + Get when both are needed.
    /// </summary>
    public bool TryGet(string name, out object? value)
    {
        if (_values.TryGetValue(name, out value))
        {
            return true;
        }

        if (Enclosing != null)
        {
            return Enclosing.TryGet(name, out value);
        }

        value = null;
        return false;
    }

    public void Assign(Token name, object? value)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrNullRef(_values, name.Lexeme);
        if (!Unsafe.IsNullRef(ref slot))
        {
            slot = value;
            return;
        }

        if (Enclosing != null)
        {
            Enclosing.Assign(name, value);
            return;
        }

        throw new Exception($"Undefined variable '{name.Lexeme}'.");
    }

    /// <summary>
    /// Gets a variable value at a specific scope distance.
    /// </summary>
    public object? GetAt(int distance, string name)
    {
        return Ancestor(distance)._values.GetValueOrDefault(name);
    }

    /// <summary>
    /// Assigns a variable at a specific scope distance.
    /// </summary>
    public void AssignAt(int distance, Token name, object? value)
    {
        Ancestor(distance)._values[name.Lexeme] = value;
    }

    /// <summary>
    /// Traverses up the scope chain a specific number of steps.
    /// </summary>
    private RuntimeEnvironment Ancestor(int distance)
    {
        RuntimeEnvironment environment = this;
        for (int i = 0; i < distance; i++)
        {
            environment = environment.Enclosing!;
        }
        return environment;
    }

    /// <summary>
    /// Defines or merges a namespace in the current scope.
    /// If a namespace with the same name already exists, merges the members.
    /// </summary>
    public void DefineNamespace(string name, SharpTSNamespace ns)
    {
        if (_namespaces.TryGetValue(name, out var existing))
        {
            // Merge: combine members from both namespace declarations
            existing.Merge(ns);
        }
        else
        {
            _namespaces[name] = ns;
            // Also define in values so it can be looked up as a variable
            _values[name] = ns;
        }
    }

    /// <summary>
    /// Gets a namespace by name, searching up the scope chain.
    /// </summary>
    public SharpTSNamespace? GetNamespace(string name)
    {
        if (_namespaces.TryGetValue(name, out var ns))
            return ns;
        return Enclosing?.GetNamespace(name);
    }

    #region RuntimeValue Support (Phase 2 Migration)

    /// <summary>
    /// Gets a variable value as a RuntimeValue.
    /// This is the preferred method for new code during the migration period.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RuntimeValue GetValue(Token name)
    {
        return RuntimeValue.FromBoxed(Get(name));
    }

    /// <summary>
    /// Gets a variable value as a RuntimeValue by string name.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RuntimeValue GetValue(string name)
    {
        return RuntimeValue.FromBoxed(Get(name));
    }

    /// <summary>
    /// Attempts to get a variable value as a RuntimeValue.
    /// </summary>
    public bool TryGetValue(string name, out RuntimeValue value)
    {
        if (TryGet(name, out var boxed))
        {
            value = RuntimeValue.FromBoxed(boxed);
            return true;
        }
        value = RuntimeValue.Undefined;
        return false;
    }

    /// <summary>
    /// Defines a variable with a RuntimeValue.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DefineValue(string name, RuntimeValue value)
    {
        _values[name] = value.ToObject();
    }

    /// <summary>
    /// Assigns a RuntimeValue to an existing variable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AssignValue(Token name, RuntimeValue value)
    {
        Assign(name, value.ToObject());
    }

    /// <summary>
    /// Gets a variable value at a specific scope distance as RuntimeValue.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RuntimeValue GetValueAt(int distance, string name)
    {
        return RuntimeValue.FromBoxed(GetAt(distance, name));
    }

    /// <summary>
    /// Assigns a RuntimeValue at a specific scope distance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AssignValueAt(int distance, Token name, RuntimeValue value)
    {
        AssignAt(distance, name, value.ToObject());
    }

    #endregion
}
