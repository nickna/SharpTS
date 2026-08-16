using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Exceptions;

namespace SharpTS.Runtime.Types;

/// <summary>
/// A <see cref="SharpTSArray"/> created by a guest class that extends
/// <c>Array</c>. IS an array (all array built-ins, indexing, iteration, and
/// <c>Array.isArray</c> work unchanged) and additionally carries the guest
/// class for method/getter lookup and <c>instanceof</c> brand checks.
/// </summary>
/// <seealso cref="SharpTSArrayClass"/>
public sealed class SharpTSArraySubclassInstance(SharpTSClass klass) : SharpTSArray
{
    /// <summary>The guest class this array was constructed by.</summary>
    public SharpTSClass Klass { get; } = klass;
}

/// <summary>
/// A <see cref="SharpTSClass"/> subclass representing <c>Array</c> in a class
/// hierarchy — the bridge that lets <c>class MyArray extends Array</c> work
/// (#233). Mirrors the <see cref="SharpTSErrorClass"/> pattern: the base
/// singleton stands in for the built-in constructor (its <c>constructor</c>
/// method implements <c>super(...)</c> semantics), and user subclasses
/// override <see cref="Call"/> to produce array-backed instances.
/// </summary>
/// <remarks>
/// v1 scope: instance methods, getters/setters, public fields (stored as
/// named properties on the array), constructors with <c>super(...)</c>, and
/// instanceof both ways. Static-side inheritance of Array built-ins
/// (<c>MyArray.from</c>) and Symbol.species-driven derived creation are
/// tracked separately.
/// </remarks>
public class SharpTSArrayClass : SharpTSClass
{
    /// <summary>
    /// The singleton standing in for the built-in <c>Array</c> constructor at
    /// the root of guest Array-subclass hierarchies.
    /// </summary>
    public static readonly SharpTSArrayClass ArrayBase = new();

    private SharpTSArrayClass()
        : base(
            "Array",
            null,
            methods: new Dictionary<string, ISharpTSCallable>
            {
                ["constructor"] = new ArrayConstructorCallable()
            },
            staticMethods: [],
            staticProperties: [])
    {
    }

    /// <summary>
    /// Creates a user-defined Array subclass (e.g. <c>class MyArray extends Array</c>).
    /// </summary>
    public SharpTSArrayClass(
        string name,
        SharpTSArrayClass superclass,
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
    }

    /// <summary>
    /// ECMA-262 §23.1.1.1 Array(...values) applied to an existing instance:
    /// a single numeric argument sets the length (holes, not undefineds);
    /// any other argument shape appends the arguments as elements.
    /// </summary>
    internal static void InitializeFromArrayArguments(SharpTSArray array, List<object?> arguments)
    {
        if (arguments.Count == 1 && arguments[0] is double d)
        {
            if (d < 0 || d > uint.MaxValue || Math.Floor(d) != d)
                throw new ThrowException(new SharpTSRangeError("Invalid array length."));
            array.SetLength((long)d);
            return;
        }
        array.AddRange(arguments);
    }

    /// <summary>
    /// Constructs a <see cref="SharpTSArraySubclassInstance"/>, initialises
    /// declared public fields as named properties, then runs the user
    /// constructor (whose <c>super(...)</c> resolves to
    /// <see cref="ArrayConstructorCallable"/>) or, absent one, applies the
    /// built-in Array constructor semantics directly.
    /// </summary>
    public override object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var instance = new SharpTSArraySubclassInstance(this);

        InitializeFieldsAsNamedProperties(interpreter, instance);

        ISharpTSCallable? constructor = FindMethod("constructor");
        bool hasUserConstructor = constructor is not ArrayConstructorCallable;

        if (hasUserConstructor && constructor != null)
        {
            BindMethodToReceiver(constructor, instance).Call(interpreter, arguments);
        }
        else
        {
            InitializeFromArrayArguments(instance, arguments);
        }

        return instance;
    }

    /// <summary>
    /// Initialises declared public instance fields (walking the superclass
    /// chain root-first) as named properties on the array instance — arrays
    /// store expando properties via the named-property table rather than
    /// SharpTSInstance fields.
    /// </summary>
    private void InitializeFieldsAsNamedProperties(Interpreter interpreter, SharpTSArraySubclassInstance instance)
    {
        if (Superclass is SharpTSArrayClass parent)
            parent.InitializeFieldsAsNamedProperties(interpreter, instance);

        foreach (var field in InstanceFields)
        {
            object? value = field.Initializer != null
                ? interpreter.Evaluate(field.Initializer)
                : null;
            string key = field.ComputedKey != null
                ? interpreter.Evaluate(field.ComputedKey)?.ToString() ?? "undefined"
                : field.Name.Lexeme;
            instance.SetNamedProperty(key, value);
        }
    }

    /// <summary>
    /// Built-in Array constructor callable — the target of <c>super(...)</c>
    /// in user subclass constructors. Applies Array constructor semantics to
    /// the bound receiver.
    /// </summary>
    internal sealed class ArrayConstructorCallable : ISharpTSCallable, IReceiverBindable
    {
        private object? _boundReceiver;

        public int Arity() => 0; // All args optional

        public ISharpTSCallable BindToReceiver(object receiver)
            => new ArrayConstructorCallable { _boundReceiver = receiver };

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            var receiver = _boundReceiver ?? interpreter.GetCurrentThis();
            if (receiver is SharpTSArray array)
                InitializeFromArrayArguments(array, arguments);
            return null;
        }
    }
}

/// <summary>
/// Interface for callables that can be bound to an arbitrary receiver (not
/// just <see cref="SharpTSInstance"/>). The receiver-typed counterpart of
/// <see cref="IInstanceBindable"/>, used by class machinery when instances
/// are backed by built-in value types (e.g. Array subclass instances).
/// </summary>
public interface IReceiverBindable
{
    ISharpTSCallable BindToReceiver(object receiver);
}
