using SharpTS.Execution;
using SharpTS.Parsing;
using System.Collections.Frozen;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a TypeScript class definition.
/// </summary>
/// <remarks>
/// Stores class metadata: name, superclass reference, instance methods, static methods,
/// static properties, and getter/setter accessors. Implements <see cref="ISharpTSCallable"/>
/// so that calling a class (e.g., <c>new MyClass()</c>) constructs a new <see cref="SharpTSInstance"/>.
/// Method and property lookups walk the inheritance chain via <see cref="Superclass"/>.
/// </remarks>
/// <seealso cref="SharpTSInstance"/>
/// <seealso cref="SharpTSFunction"/>
public class SharpTSClass(
    string name,
    SharpTSClass? superclass,
    Dictionary<string, SharpTSFunction> methods,
    Dictionary<string, SharpTSFunction> staticMethods,
    Dictionary<string, object?> staticProperties,
    Dictionary<string, SharpTSFunction>? getters = null,
    Dictionary<string, SharpTSFunction>? setters = null,
    bool isAbstract = false,
    List<Stmt.Field>? instanceFields = null) : ISharpTSCallable
{
    public string Name { get; } = name;
    public SharpTSClass? Superclass { get; } = superclass;
    public bool IsAbstract { get; } = isAbstract;
    private readonly FrozenDictionary<string, SharpTSFunction> _methods = methods.ToFrozenDictionary();
    private readonly FrozenDictionary<string, SharpTSFunction> _staticMethods = staticMethods.ToFrozenDictionary();
    private readonly Dictionary<string, object?> _staticProperties = staticProperties;
    private readonly FrozenDictionary<string, SharpTSFunction> _getters = getters?.ToFrozenDictionary() ?? FrozenDictionary<string, SharpTSFunction>.Empty;
    private readonly FrozenDictionary<string, SharpTSFunction> _setters = setters?.ToFrozenDictionary() ?? FrozenDictionary<string, SharpTSFunction>.Empty;
    private readonly List<Stmt.Field> _instanceFields = instanceFields ?? [];

    public int Arity()
    {
        SharpTSFunction? constructor = FindMethod("constructor");
        if (constructor != null) return constructor.Arity();
        return Superclass?.Arity() ?? 0;
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        SharpTSInstance instance = new(this);

        // Initialize instance fields with their initializers (before constructor runs)
        // Superclass fields are initialized first via inheritance chain
        InitializeInstanceFields(interpreter, instance);

        SharpTSFunction? constructor = FindMethod("constructor");
        if (constructor != null)
        {
            constructor.Bind(instance).Call(interpreter, arguments);
        }
        else if (Superclass != null)
        {
            // If no constructor, call super constructor
            Superclass.Call(interpreter, arguments);
        }

        return instance;
    }

    private void InitializeInstanceFields(Interpreter interpreter, SharpTSInstance instance)
    {
        // First initialize superclass fields
        Superclass?.InitializeInstanceFields(interpreter, instance);

        // Then initialize this class's fields
        foreach (var field in _instanceFields)
        {
            if (field.Initializer != null)
            {
                object? value = interpreter.Evaluate(field.Initializer);
                instance.SetRawField(field.Name.Lexeme, value);
            }
            else
            {
                // Fields without initializers are set to null/undefined
                instance.SetRawField(field.Name.Lexeme, null);
            }
        }
    }

    public SharpTSFunction? FindMethod(string name)
    {
        if (_methods.TryGetValue(name, out SharpTSFunction? method))
        {
            return method;
        }

        return Superclass?.FindMethod(name);
    }

    public SharpTSFunction? FindStaticMethod(string name)
    {
        if (_staticMethods.TryGetValue(name, out SharpTSFunction? method))
        {
            return method;
        }

        return Superclass?.FindStaticMethod(name);
    }

    public object? GetStaticProperty(string name)
    {
        if (_staticProperties.TryGetValue(name, out var value))
        {
            return value;
        }

        return Superclass?.GetStaticProperty(name);
    }

    public bool HasStaticProperty(string name)
    {
        if (_staticProperties.ContainsKey(name))
        {
            return true;
        }

        return Superclass?.HasStaticProperty(name) ?? false;
    }

    public void SetStaticProperty(string name, object? value)
    {
        _staticProperties[name] = value;
    }

    public SharpTSFunction? FindGetter(string name)
    {
        if (_getters.TryGetValue(name, out SharpTSFunction? getter))
        {
            return getter;
        }

        return Superclass?.FindGetter(name);
    }

    public SharpTSFunction? FindSetter(string name)
    {
        if (_setters.TryGetValue(name, out SharpTSFunction? setter))
        {
            return setter;
        }

        return Superclass?.FindSetter(name);
    }

    public bool HasGetter(string name) => _getters.ContainsKey(name) || (Superclass?.HasGetter(name) ?? false);
    public bool HasSetter(string name) => _setters.ContainsKey(name) || (Superclass?.HasSetter(name) ?? false);

    public override string ToString() => Name;
}
