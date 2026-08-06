using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Exceptions;

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
        Prototype.DefineExtraProperty("name", new SharpTSPropertyDescriptor
        {
            Value = errorTypeName,
            HasValue = true,
            Writable = true,
            HasWritable = true,
            Enumerable = false,
            HasEnumerable = true,
            Configurable = true,
            HasConfigurable = true,
        });
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
internal sealed class ErrorToStringCallable : ISharpTSCallable, IInstanceBindable,
    Runtime.BuiltIns.IBuiltInFunctionMetadata
{
    private object? _receiver;
    private bool _hasReceiver;
    private readonly Runtime.BuiltIns.BuiltInFunctionMetadata _metadata;

    public ErrorToStringCallable()
        : this(new Runtime.BuiltIns.BuiltInFunctionMetadata())
    {
    }

    private ErrorToStringCallable(Runtime.BuiltIns.BuiltInFunctionMetadata metadata)
    {
        _metadata = metadata;
    }

    public string FunctionName => "toString";
    public bool HasMetadataProperty(string name) => _metadata.Has(name);
    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public int Arity() => 0;

    public ISharpTSCallable BindTo(SharpTSInstance instance)
    {
        return Bind(instance);
    }

    internal ErrorToStringCallable Bind(object? receiver)
        => new(_metadata) { _receiver = receiver, _hasReceiver = true };

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Built-in functions use strict this semantics: an unbound call has
        // undefined as its receiver rather than the global object.
        if (!_hasReceiver || _receiver is null or SharpTSUndefined
            or double or float or int or long or bool or string
            or SharpTSBigInt or SharpTSSymbol)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Error.prototype.toString requires an object receiver"));
        }

        object? nameValue = interpreter.GetPropertyValue(_receiver, "name");
        string name = nameValue is SharpTSUndefined
            ? "Error"
            : interpreter.ToStringForBuiltInArgument(nameValue);

        object? messageValue = interpreter.GetPropertyValue(_receiver, "message");
        string message = messageValue is SharpTSUndefined
            ? ""
            : interpreter.ToStringForBuiltInArgument(messageValue);

        if (name.Length == 0) return message;
        if (message.Length == 0) return name;
        return $"{name}: {message}";
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
