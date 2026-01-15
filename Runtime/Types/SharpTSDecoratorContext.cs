using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Decorator context kind for TC39 Stage 3 decorators.
/// </summary>
public enum DecoratorContextKind
{
    Class,
    Method,
    Getter,
    Setter,
    Field,
    Accessor
}

/// <summary>
/// Context object passed to TC39 Stage 3 decorators.
/// Provides metadata about the decorated element and allows adding initializers.
/// </summary>
public class SharpTSDecoratorContext
{
    /// <summary>The kind of element being decorated</summary>
    public DecoratorContextKind Kind { get; init; }

    /// <summary>The name of the decorated element</summary>
    public string Name { get; init; } = "";

    /// <summary>Whether this is a static member</summary>
    public bool Static { get; init; }

    /// <summary>Whether this is a private member (always false in SharpTS - no private syntax)</summary>
    public bool Private { get; init; }

    /// <summary>Access object for fields/accessors (contains get/set functions)</summary>
    public SharpTSObject? Access { get; init; }

    /// <summary>Metadata object (Symbol.metadata)</summary>
    public SharpTSObject Metadata { get; } = new(new Dictionary<string, object?>());

    /// <summary>Initializers added via addInitializer()</summary>
    private readonly List<ISharpTSCallable> _initializers = [];

    /// <summary>
    /// Adds an initializer function to be called after class construction.
    /// </summary>
    public void AddInitializer(ISharpTSCallable fn)
    {
        _initializers.Add(fn);
    }

    /// <summary>
    /// Gets all registered initializers.
    /// </summary>
    public IReadOnlyList<ISharpTSCallable> Initializers => _initializers;

    /// <summary>
    /// Runs all initializers with the given instance as 'this'.
    /// </summary>
    public void RunInitializers(Interpreter interpreter, SharpTSInstance instance)
    {
        foreach (var init in _initializers)
        {
            // Create a bound version of the initializer with 'this' set to instance
            var bound = init is SharpTSFunction fn ? fn.Bind(instance) : init;
            bound.Call(interpreter, []);
        }
    }

    /// <summary>
    /// Converts this context to a SharpTS object for passing to decorators.
    /// </summary>
    public SharpTSObject ToRuntimeObject()
    {
        var obj = new SharpTSObject(new Dictionary<string, object?>());

        // kind: "class" | "method" | "getter" | "setter" | "field" | "accessor"
        obj.SetProperty("kind", Kind.ToString().ToLowerInvariant());

        // name: string
        obj.SetProperty("name", Name);

        // static: boolean
        obj.SetProperty("static", Static);

        // private: boolean
        obj.SetProperty("private", Private);

        // access: { get(): T, set(v: T): void } | undefined
        if (Access != null)
        {
            obj.SetProperty("access", Access);
        }

        // metadata: object
        obj.SetProperty("metadata", Metadata);

        // addInitializer: (fn: () => void) => void
        obj.SetProperty("addInitializer", new AddInitializerMethod(this));

        return obj;
    }

    /// <summary>
    /// Creates a context for a class decorator.
    /// </summary>
    public static SharpTSDecoratorContext ForClass(string name)
    {
        return new SharpTSDecoratorContext
        {
            Kind = DecoratorContextKind.Class,
            Name = name,
            Static = false,
            Private = false
        };
    }

    /// <summary>
    /// Creates a context for a method decorator.
    /// </summary>
    public static SharpTSDecoratorContext ForMethod(string name, bool isStatic)
    {
        return new SharpTSDecoratorContext
        {
            Kind = DecoratorContextKind.Method,
            Name = name,
            Static = isStatic,
            Private = false
        };
    }

    /// <summary>
    /// Creates a context for a getter decorator.
    /// </summary>
    public static SharpTSDecoratorContext ForGetter(string name, bool isStatic)
    {
        return new SharpTSDecoratorContext
        {
            Kind = DecoratorContextKind.Getter,
            Name = name,
            Static = isStatic,
            Private = false
        };
    }

    /// <summary>
    /// Creates a context for a setter decorator.
    /// </summary>
    public static SharpTSDecoratorContext ForSetter(string name, bool isStatic)
    {
        return new SharpTSDecoratorContext
        {
            Kind = DecoratorContextKind.Setter,
            Name = name,
            Static = isStatic,
            Private = false
        };
    }

    /// <summary>
    /// Creates a context for a field decorator with access object.
    /// </summary>
    public static SharpTSDecoratorContext ForField(string name, bool isStatic, SharpTSObject? access = null)
    {
        return new SharpTSDecoratorContext
        {
            Kind = DecoratorContextKind.Field,
            Name = name,
            Static = isStatic,
            Private = false,
            Access = access
        };
    }

    /// <summary>
    /// Creates a context for an accessor decorator with access object.
    /// </summary>
    public static SharpTSDecoratorContext ForAccessor(string name, bool isStatic, SharpTSObject? access = null)
    {
        return new SharpTSDecoratorContext
        {
            Kind = DecoratorContextKind.Accessor,
            Name = name,
            Static = isStatic,
            Private = false,
            Access = access
        };
    }

    /// <summary>
    /// Built-in method for context.addInitializer()
    /// </summary>
    private class AddInitializerMethod(SharpTSDecoratorContext context) : ISharpTSCallable
    {
        public int Arity() => 1;

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            if (arguments.Count > 0 && arguments[0] is ISharpTSCallable fn)
            {
                context.AddInitializer(fn);
            }
            else
            {
                throw new Exception("Runtime Error: addInitializer expects a function argument.");
            }
            return null;
        }
    }
}
