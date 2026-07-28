using SharpTS.Execution;
using SharpTS.Parsing;

namespace SharpTS.Runtime.Types;

/// <summary>
/// A <see cref="SharpTSClass"/> subclass that represents Error constructor functions.
/// Registered as a global variable so that <c>typeof Error</c>, <c>class MyError extends Error</c>,
/// and <c>const E = Error</c> all work correctly.
/// </summary>
/// <remarks>
/// Overrides <see cref="SharpTSClass.Call"/> to initialise error-specific fields (name, message,
/// stack, cause) on the created <see cref="SharpTSInstance"/>.  When a user class extends an
/// error type, <c>VisitClass</c> creates a <see cref="SharpTSErrorClass"/> so that <c>Call()</c>
/// continues to initialise error fields via the built-in constructor.
/// </remarks>
public class SharpTSErrorClass : SharpTSClass
{
    /// <summary>
    /// The error type name this class represents (e.g. "Error", "TypeError").
    /// For user subclasses like <c>class MyError extends Error</c>, this is "MyError".
    /// </summary>
    private readonly string _errorTypeName;

    /// <summary>
    /// Global registry of built-in Error constructor classes keyed by error
    /// type name. Populated lazily on construction. Native <see cref="SharpTSError"/>
    /// instances (thrown from C# via <see cref="Exceptions.ThrowException"/>)
    /// don't know their class reference directly; this registry lets
    /// <c>ErrorBuiltIns.GetMember</c> resolve <c>.constructor</c> back to the
    /// same <see cref="SharpTSErrorClass"/> instance that the global
    /// <c>TypeError</c>/<c>RangeError</c>/... identifier resolves to, so
    /// <c>err.constructor === TypeError</c> holds. Single-interpreter
    /// assumption is acceptable — Test262 and the REPL both use a single
    /// interpreter per run.
    /// </summary>
    private static readonly Dictionary<string, SharpTSErrorClass> _builtInRegistry = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the registered built-in Error constructor for the given type
    /// name (e.g. "TypeError"), or null if none has been registered.
    /// </summary>
    public static SharpTSErrorClass? GetBuiltInClass(string errorTypeName)
        => _builtInRegistry.TryGetValue(errorTypeName, out var cls) ? cls : null;

    /// <summary>
    /// Creates a built-in Error constructor class (Error, TypeError, etc.) with no user-defined methods.
    /// </summary>
    public SharpTSErrorClass(string errorTypeName, SharpTSErrorClass? superclass)
        : base(
            errorTypeName,
            superclass,
            methods: new Dictionary<string, ISharpTSCallable>
            {
                ["constructor"] = new ErrorConstructorCallable(errorTypeName),
                ["toString"] = new ErrorToStringCallable()
            },
            staticMethods: [],
            staticProperties: [])
    {
        _errorTypeName = errorTypeName;
        _builtInRegistry[errorTypeName] = this;
    }

    /// <summary>
    /// Creates a user-defined Error subclass (e.g. <c>class MyError extends Error { ... }</c>)
    /// with user-supplied methods, fields, etc.
    /// </summary>
    public SharpTSErrorClass(
        string name,
        SharpTSErrorClass superclass,
        Dictionary<string, ISharpTSCallable> methods,
        Dictionary<string, ISharpTSCallable> staticMethods,
        Dictionary<string, object?> staticProperties,
        Dictionary<string, SharpTSFunction>? getters = null,
        Dictionary<string, SharpTSFunction>? setters = null,
        bool isAbstract = false,
        List<Stmt.Field>? instanceFields = null,
        List<Stmt.Field>? instancePrivateFields = null,
        Dictionary<string, ISharpTSCallable>? privateMethods = null,
        Dictionary<string, object?>? staticPrivateFields = null,
        Dictionary<string, ISharpTSCallable>? staticPrivateMethods = null,
        List<Stmt.AutoAccessor>? instanceAutoAccessors = null,
        Dictionary<string, object?>? staticAutoAccessors = null,
        Dictionary<string, SharpTSFunction>? staticGetters = null,
        Dictionary<string, SharpTSFunction>? staticSetters = null)
        : base(
            name,
            superclass,
            methods,
            staticMethods,
            staticProperties,
            getters,
            setters,
            isAbstract,
            instanceFields,
            instancePrivateFields,
            privateMethods,
            staticPrivateFields,
            staticPrivateMethods,
            instanceAutoAccessors,
            staticAutoAccessors,
            staticGetters,
            staticSetters)
    {
        _errorTypeName = name;
    }

    /// <summary>
    /// Initialises error fields (name, message, stack) on an instance.
    /// </summary>
    internal static void InitializeErrorFields(
        SharpTSInstance instance,
        string errorTypeName,
        List<object?> arguments)
    {
        // AggregateError: first arg is errors array, second is message
        if (errorTypeName == "AggregateError")
        {
            var message = arguments.Count > 1
                ? arguments[1]?.ToString() ?? "All promises were rejected"
                : "All promises were rejected";
            instance.SetRawField("name", errorTypeName);
            instance.SetRawField("message", message);
            instance.SetRawField("stack", $"{errorTypeName}: {message}");
            if (arguments.Count > 0)
                instance.SetRawField("errors", arguments[0]);
            // Cause is in the third argument's options
            if (arguments.Count > 2 && arguments[2] is SharpTSObject opts
                && opts.HasProperty("cause"))
            {
                instance.SetRawField("cause", opts.GetProperty("cause"));
            }
        }
        else
        {
            var message = arguments.Count > 0 ? arguments[0]?.ToString() ?? "" : "";
            instance.SetRawField("name", errorTypeName);
            instance.SetRawField("message", message);
            instance.SetRawField("stack", $"{errorTypeName}: {message}");
            // Cause is in the second argument's options
            if (arguments.Count > 1 && arguments[1] is SharpTSObject opts
                && opts.HasProperty("cause"))
            {
                instance.SetRawField("cause", opts.GetProperty("cause"));
            }
        }

        // Error's standard own slots are non-enumerable. Assigning a new value
        // to one of these existing properties preserves that attribute, while
        // later user-created expandos remain enumerable.
        foreach (var key in new[] { "name", "message", "stack", "cause", "errors" })
            instance.MarkNonEnumerable(key);
    }

    /// <summary>
    /// Returns the error-formatted toString() result for an instance.
    /// </summary>
    internal static string ErrorToString(SharpTSInstance instance)
    {
        var name = instance.GetRawField("name")?.ToString() ?? "Error";
        var message = instance.GetRawField("message")?.ToString() ?? "";
        return string.IsNullOrEmpty(message) ? name : $"{name}: {message}";
    }

    /// <summary>
    /// Overrides <see cref="SharpTSClass.Call"/> to initialise error fields after instance creation.
    /// </summary>
    public override object? Call(Interpreter interpreter, List<object?> arguments)
    {
        SharpTSInstance instance = new(this);

        InitializeInstanceFields(interpreter, instance);
        InitializePrivateFields(interpreter, instance);
        InitializeAutoAccessors(interpreter, instance);

        ISharpTSCallable? constructor = FindMethod("constructor");
        bool hasUserConstructor = constructor is not ErrorConstructorCallable;

        if (hasUserConstructor && constructor != null)
        {
            // User-defined constructor — super() in body will call ErrorConstructorCallable
            BindMethod(constructor, instance).Call(interpreter, arguments);
        }
        else
        {
            // No user constructor (or built-in Error class) — initialise error fields directly
            InitializeErrorFields(instance, _errorTypeName, arguments);
        }

        return instance;
    }

    /// <summary>
    /// Built-in Error constructor callable.  Used for <c>super(msg)</c> calls from user
    /// subclass constructors.  Implements <see cref="IInstanceBindable"/> so that
    /// <see cref="SharpTSClass.BindMethod"/> can bind the instance to it.
    /// </summary>
    internal sealed class ErrorConstructorCallable(string errorTypeName) : ISharpTSCallable, IInstanceBindable
    {
        private SharpTSInstance? _boundInstance;

        public int Arity() => 0; // All args optional

        public ISharpTSCallable BindTo(SharpTSInstance instance)
        {
            return new ErrorConstructorCallable(errorTypeName) { _boundInstance = instance };
        }

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            var instance = _boundInstance
                ?? interpreter.GetCurrentThis() as SharpTSInstance;
            if (instance != null)
                InitializeErrorFields(instance, errorTypeName, arguments);
            return null;
        }
    }
}

/// <summary>
/// Built-in toString() callable for Error instances.
/// </summary>
internal sealed class ErrorToStringCallable : ISharpTSCallable, IInstanceBindable
{
    private SharpTSInstance? _boundInstance;

    public int Arity() => 0;

    public ISharpTSCallable BindTo(SharpTSInstance instance)
    {
        return new ErrorToStringCallable { _boundInstance = instance };
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var instance = _boundInstance
            ?? interpreter.GetCurrentThis() as SharpTSInstance;
        if (instance == null) return "Error";
        return SharpTSErrorClass.ErrorToString(instance);
    }
}

/// <summary>
/// Interface for callables that can be bound to an instance.
/// Used by <see cref="SharpTSClass.BindMethod"/> to support non-SharpTSFunction callables
/// that need access to <c>this</c>.
/// </summary>
public interface IInstanceBindable
{
    ISharpTSCallable BindTo(SharpTSInstance instance);
}
