using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpTS.Parsing;
using SharpTS.Runtime.Exceptions;
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
public class RuntimeEnvironment : ScopeChain<RuntimeValue, RuntimeEnvironment>
{
    private Dictionary<string, SharpTSNamespace>? _namespaces;
    private HashSet<string>? _uninitializedLexicalNames;
    private string? _directBindingName;
    private RuntimeValue _directBindingValue;
    private bool _hasDirectBinding;
    private Dictionary<string, (Func<RuntimeValue> Read, Action<RuntimeValue, bool> Write)>? _objectBindings;
    private SharpTSClass? _privateClass;
    private bool? _staticSuper;
    private bool _ownsNewTarget;
    private RuntimeValue _newTarget;
    private bool _ownsConstructionFrame;
    private ClassConstructionFrame? _constructionFrame;

    // Try/finally and switch lexical scopes redirect var declarations to their previous home.
    // Invocation and ordinary block environments start unmarked.
    internal RuntimeEnvironment? VarDeclarationTarget { get; set; }

    // Ordinary invocations and class initializers explicitly own a null slot;
    // true arrows and blocks inherit their lexical constructor invocation.
    internal ClassConstructionFrame? ConstructionFrame
    {
        get => _ownsConstructionFrame ? _constructionFrame : Enclosing?.ConstructionFrame;
        set { _ownsConstructionFrame = true; _constructionFrame = value; }
    }

    internal bool TryGetThis(out RuntimeValue value)
    {
        ConstructionFrame?.EnsureThisBound();
        return TryGet("this", out value);
    }

    // Ordinary invocations own a slot, including undefined. True arrows and
    // ordinary block scopes inherit the nearest invocation's lexical slot.
    internal RuntimeValue NewTarget
    {
        get => _ownsNewTarget ? _newTarget : Enclosing?.NewTarget ?? RuntimeValue.Undefined;
        set { _ownsNewTarget = true; _newTarget = value; }
    }

    // The super facet follows the lexical method, including through arrows;
    // rebinding this must not change which parent member is selected.
    internal bool StaticSuper
    {
        get => _staticSuper ?? Enclosing?.StaticSuper ?? false;
        set => _staticSuper = value;
    }

    // Private names follow the method's lexical declaration across receiver
    // rebinding, closures and suspension. This does not add a variable scope.
    internal SharpTSClass? PrivateClass
    {
        get => _privateClass ?? Enclosing?.PrivateClass;
        set => _privateClass = value;
    }

    internal SharpTSClass? FindPrivateClass(string name)
    {
        for (RuntimeEnvironment? scope = this; scope is not null; scope = scope.Enclosing)
            if (scope._privateClass is { } owner && owner.DeclaresPrivateName(name))
                return owner;
        return null;
    }

    public RuntimeEnvironment(RuntimeEnvironment? enclosing = null, bool? strictMode = null)
        : base(enclosing, strictMode)
    {
    }

    /// <summary>
    /// Creates a scope with one binding stored inline. Catch clauses use this
    /// path so catching a primitive does not box it or allocate a dictionary.
    /// The environment remains a distinct object, preserving closure identity.
    /// </summary>
    internal RuntimeEnvironment(
        RuntimeEnvironment enclosing,
        string directBindingName,
        RuntimeValue directBindingValue)
        : base(enclosing)
    {
        _directBindingName = directBindingName;
        _directBindingValue = directBindingValue;
        _hasDirectBinding = true;
    }

    public override IEnumerable<string> Names
    {
        get
        {
            if (_hasDirectBinding)
                yield return _directBindingName!;
            if (_values != null)
            {
                foreach (string name in _values.Keys)
                    yield return name;
            }
        }
    }

    public override void Define(string name, RuntimeValue value)
        => DefineValue(name, value);

    /// <summary>Reserves a missing local lexical binding until its declaration executes.</summary>
    internal void ReserveLexical(string name, bool replaceExisting = false)
    {
        if (!replaceExisting && HasLocalBinding(name)) return;
        Values[name] = RuntimeValue.Undefined;
        (_uninitializedLexicalNames ??= new(StringComparer.Ordinal)).Add(name);
    }

    private void DefineValue(string name, RuntimeValue value)
    {
        SetLocalValue(name, value);
        if (_uninitializedLexicalNames?.Remove(name) == true
            && _uninitializedLexicalNames.Count == 0)
            _uninitializedLexicalNames = null;
    }

    // Only explicitly registered classic-global vars use object-backed storage.
    // The dictionary slot records ownership; the object remains authoritative.
    internal void DefineObjectBinding(
        string name, Func<RuntimeValue> read, Action<RuntimeValue, bool> write)
    {
        if (HasObjectBinding(name)) return;
        Values[name] = RuntimeValue.Undefined;
        (_objectBindings ??= new(StringComparer.Ordinal))[name] = (read, write);
    }

    internal bool HasObjectBinding(string name) => _objectBindings?.ContainsKey(name) == true;

    private void SetLocalValue(string name, RuntimeValue value, bool? strictMode = null)
    {
        if (_objectBindings?.TryGetValue(name, out var binding) == true)
        {
            binding.Write(value, strictMode ?? IsStrictMode);
            return;
        }
        if (_hasDirectBinding && name == _directBindingName)
        {
            _directBindingValue = value;
            return;
        }
        Values[name] = value;
    }

    public override RuntimeValue Get(string name)
    {
        if (TryGetLocal(name, out RuntimeValue value))
            return value;
        return Enclosing != null ? Enclosing.Get(name) : default;
    }

    public override bool IsDefined(string name) =>
        HasLocalBinding(name) || (Enclosing?.IsDefined(name) ?? false);

    public override bool IsDefinedLocally(string name) =>
        HasLocalBinding(name);

    /// <summary>Reports a reserved local binding without reading or initializing its value.</summary>
    internal bool IsUninitializedLocally(string name) =>
        _uninitializedLexicalNames?.Contains(name) == true;

    public RuntimeValue Get(Token name)
    {
        if (TryGetLocal(name.Lexeme, out RuntimeValue value))
        {
            return value;
        }

        if (Enclosing != null) return Enclosing.Get(name);

        // ECMA-262 §9.4.2: resolving an unbound name is a ReferenceError. The name prefix
        // is what routes this to a guest ReferenceError at the catch binding.
        throw new Exception($"Runtime Error: ReferenceError: Undefined variable '{name.Lexeme}'.");
    }

    /// <summary>
    /// Attempts to get a variable value in a single scope chain traversal.
    /// </summary>
    public bool TryGet(string name, out RuntimeValue value)
    {
        if (TryGetLocal(name, out value))
        {
            return true;
        }

        if (Enclosing != null)
        {
            return Enclosing.TryGet(name, out value);
        }

        value = RuntimeValue.Undefined;
        return false;
    }

    public void Assign(Token name, RuntimeValue value)
        => AssignCore(name, value, IsStrictMode);

    private void AssignCore(Token name, RuntimeValue value, bool strictMode)
    {
        EnsureInitialized(name.Lexeme);
        if (_objectBindings?.TryGetValue(name.Lexeme, out var binding) == true)
        {
            binding.Write(value, strictMode);
            return;
        }
        if (_hasDirectBinding && name.Lexeme == _directBindingName)
        {
            _directBindingValue = value;
            return;
        }
        if (_values != null)
        {
            ref var slot = ref CollectionsMarshal.GetValueRefOrNullRef(_values, name.Lexeme);
            if (!Unsafe.IsNullRef(ref slot))
            {
                slot = value;
                return;
            }
        }

        if (Enclosing != null)
        {
            Enclosing.AssignCore(name, value, strictMode);
            return;
        }

        // ECMA-262 §9.4.2: resolving an unbound name is a ReferenceError. The name prefix
        // is what routes this to a guest ReferenceError at the catch binding.
        throw new Exception($"Runtime Error: ReferenceError: Undefined variable '{name.Lexeme}'.");
    }

    /// <summary>
    /// Assigns a variable with a boxed value (legacy compatibility).
    /// </summary>
    public void Assign(Token name, object? value) => Assign(name, RuntimeValue.FromBoxed(value));

    /// <summary>
    /// Gets a variable value at a specific scope distance.
    /// </summary>
    public RuntimeValue GetAt(int distance, string name)
    {
        RuntimeEnvironment environment = Ancestor(distance);
        return environment.TryGetLocal(name, out RuntimeValue value) ? value : default;
    }

    /// <summary>
    /// Assigns a variable at a specific scope distance.
    /// </summary>
    public void AssignAt(int distance, Token name, RuntimeValue value)
    {
        RuntimeEnvironment environment = Ancestor(distance);
        environment.EnsureInitialized(name.Lexeme);
        environment.SetLocalValue(name.Lexeme, value, IsStrictMode);
    }

    /// <summary>
    /// Assigns a variable at a specific scope distance (legacy compatibility).
    /// </summary>
    public void AssignAt(int distance, Token name, object? value)
    {
        AssignAt(distance, name, RuntimeValue.FromBoxed(value));
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
        if (_namespaces?.TryGetValue(name, out var existing) == true)
        {
            // Merge: combine members from both namespace declarations
            existing.Merge(ns);
        }
        else
        {
            (_namespaces ??= [])[name] = ns;
            // Also define in values so it can be looked up as a variable
            DefineRV(name, RuntimeValue.FromObject(ns));
        }
    }

    /// <summary>
    /// Gets a namespace by name, searching up the scope chain.
    /// </summary>
    public SharpTSNamespace? GetNamespace(string name)
    {
        if (_namespaces?.TryGetValue(name, out var ns) == true)
            return ns;
        return Enclosing?.GetNamespace(name);
    }

    /// <summary>
    /// Gets a namespace by name from THIS scope only (no chain traversal).
    /// Use when deciding whether to merge vs. create a new namespace declaration —
    /// avoids treating a same-named namespace in an enclosing scope as a merge target (#746).
    /// </summary>
    public SharpTSNamespace? GetLocalNamespace(string name)
    {
        return _namespaces != null && _namespaces.TryGetValue(name, out var ns)
            ? ns
            : null;
    }

    /// <summary>
    /// Defines a variable with an unboxed <see cref="RuntimeValue"/> — the fast path used by
    /// V2 parameter binding. Stores the value directly via the base scope chain, avoiding the
    /// box-then-unbox round-trip through <see cref="RuntimeValue.FromBoxed"/> that the
    /// <see cref="object"/>-typed <see cref="Define(string, object?)"/> incurs.
    /// (Named <c>DefineRV</c> rather than overloading <c>Define</c> because that boxing overload,
    /// declared here, would otherwise shadow the base <c>Define(string, RuntimeValue)</c> by name.)
    /// </summary>
    public void DefineRV(string name, RuntimeValue value) => DefineValue(name, value);

    /// <summary>
    /// Defines a variable with a boxed value (legacy compatibility).
    /// Wraps the value in RuntimeValue.FromBoxed automatically.
    /// </summary>
    public void Define(string name, object? value) =>
        DefineValue(name, RuntimeValue.FromBoxed(value));

    private bool HasLocalBinding(string name) =>
        (_hasDirectBinding && name == _directBindingName)
        || _values?.ContainsKey(name) == true;

    private void EnsureInitialized(string name)
    {
        if (_uninitializedLexicalNames?.Contains(name) == true)
            throw new ThrowException(new SharpTSReferenceError(
                $"Cannot access '{name}' before initialization"));
    }

    private bool TryGetLocal(string name, out RuntimeValue value)
    {
        EnsureInitialized(name);
        if (_objectBindings?.TryGetValue(name, out var binding) == true)
        {
            value = binding.Read();
            return true;
        }
        if (_hasDirectBinding && name == _directBindingName)
        {
            value = _directBindingValue;
            return true;
        }
        if (_values?.TryGetValue(name, out value) == true)
            return true;
        value = default;
        return false;
    }

}
