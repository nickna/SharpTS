using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;

namespace SharpTS.Runtime.Types;

/// <summary>
/// A <see cref="SharpTSPromise"/> created by a guest class that extends
/// <c>Promise</c>. IS a promise (await, then/catch/finally, combinator
/// participation, and <c>instanceof Promise</c> work unchanged) and
/// additionally carries the guest class for method/getter lookup and
/// <c>instanceof</c> brand checks, plus an own-property table for declared
/// fields (base <see cref="SharpTSPromise"/> has no property storage).
/// </summary>
/// <seealso cref="SharpTSPromiseClass"/>
public sealed class SharpTSPromiseSubclassInstance : SharpTSPromise
{
    /// <summary>The guest class this promise was constructed by.</summary>
    public SharpTSPromiseClass Klass { get; }

    /// <summary>
    /// The capability backing this promise — settled by <c>super(executor)</c>
    /// (or directly when the subclass is constructed from an existing task,
    /// e.g. by <see cref="SharpTSPromiseClass.ConstructDerived"/>).
    /// </summary>
    internal TaskCompletionSource<object?> Capability { get; }

    public SharpTSPromiseSubclassInstance(SharpTSPromiseClass klass, TaskCompletionSource<object?> capability)
        : base(capability.Task)
    {
        Klass = klass;
        Capability = capability;
    }
}

/// <summary>
/// A <see cref="SharpTSClass"/> subclass representing <c>Promise</c> in a
/// class hierarchy — the bridge that lets <c>class MyPromise extends Promise</c>
/// work (#242). Mirrors the <see cref="SharpTSArrayClass"/> pattern: the base
/// singleton stands in for the built-in constructor (its <c>constructor</c>
/// method implements <c>super(executor)</c> semantics), and user subclasses
/// override <see cref="Call"/> to produce promise-backed instances.
/// </summary>
/// <remarks>
/// Scope: executor construction with <c>super(executor)</c>, instance
/// methods/getters/fields, instanceof both ways, static-side inheritance of
/// the Promise built-ins (<c>MyPromise.resolve</c> etc. construct
/// subclass-typed results via <see cref="ConstructDerived"/>), and
/// SpeciesConstructor-aware results from <c>then</c>/<c>catch</c>/<c>finally</c>
/// (#221 — see <see cref="Runtime.BuiltIns.PromiseBuiltIns"/>
/// <c>ResolveSpeciesConstructor</c>; the static methods build through the
/// receiver constructor <c>C</c> directly per spec). A poisoned own
/// <c>constructor</c> getter makes then/catch/finally throw synchronously (#350,
/// via <c>PromiseBuiltIns.ResolveResultPromiseFactory</c>). Remaining spec
/// surface: a non-Promise species constructor (general NewPromiseCapability,
/// #349).
/// </remarks>
public class SharpTSPromiseClass : SharpTSClass
{
    /// <summary>
    /// The singleton standing in for the built-in <c>Promise</c> constructor
    /// at the root of guest Promise-subclass hierarchies.
    /// </summary>
    public static readonly SharpTSPromiseClass PromiseBase = new();

    private SharpTSPromiseClass()
        : base(
            "Promise",
            null,
            methods: new Dictionary<string, ISharpTSCallable>
            {
                ["constructor"] = new PromiseConstructorCallable()
            },
            staticMethods: [],
            staticProperties: [])
    {
    }

    /// <summary>
    /// Creates a user-defined Promise subclass (e.g. <c>class MyPromise extends Promise</c>).
    /// </summary>
    public SharpTSPromiseClass(
        string name,
        SharpTSPromiseClass superclass,
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
    /// Constructs a <see cref="SharpTSPromiseSubclassInstance"/>, initialises
    /// declared public fields as own properties, then runs the user
    /// constructor (whose <c>super(executor)</c> resolves to
    /// <see cref="PromiseConstructorCallable"/>) or, absent one, applies the
    /// built-in Promise constructor semantics directly.
    /// </summary>
    public override object? Call(Interpreter interpreter, List<object?> arguments)
        => Construct(interpreter, arguments, this);

    internal override object AllocateInstance(Interpreter interpreter)
        => new SharpTSPromiseSubclassInstance(this, new TaskCompletionSource<object?>());

    protected override bool TryExecuteNativeConstructor(
        Interpreter interpreter, object allocation, List<object?> arguments)
    {
        if (!ReferenceEquals(this, PromiseBase)) return false;
        RunExecutor(interpreter, RequireExecutor(arguments), ((SharpTSPromiseSubclassInstance)allocation).Capability);
        return true;
    }

    /// <summary>
    /// Constructs the result from an existing task through NewPromiseCapability,
    /// preserving constructor replacements and validating the supplied executor.
    /// Used by inherited statics and by <c>then</c>/<c>catch</c>/
    /// <c>finally</c> on subclass receivers. Runs the class's own
    /// construction so declared fields and the user constructor run normally.
    /// </summary>
    internal object? ConstructDerived(Interpreter interpreter, Task<object?> source)
        => PromiseBuiltIns.ConstructPromiseCapabilityAndAdopt(interpreter, this, source);

    /// <summary>
    /// Runs a guest Promise executor against a capability: invokes it
    /// synchronously with host resolve/reject callbacks (resolve flattens
    /// promise values; both are first-settlement-wins) and converts a
    /// throwing executor into a rejection. Shared by the base
    /// <c>new Promise(executor)</c> path and the subclass bridge.
    /// </summary>
    internal static void RunExecutor(Interpreter interpreter, ISharpTSCallable executor, TaskCompletionSource<object?> tcs)
    {
        bool settled = false;
        object settledLock = new();

        var resolveCallback = new PromiseResolveCallback(value =>
        {
            lock (settledLock)
            {
                if (settled) return;
                settled = true;
            }

            // Handle promise flattening - if value is a Promise, adopt its settled
            // state. Routed through the event loop (not the thread pool) so the loop
            // can't exit before the adoption settles — see Interpreter.AdoptInnerPromise
            // (fixes the resolve-with-thenable Pass/Fail flake under load).
            if (value is SharpTSPromise innerPromise)
            {
                if (ReferenceEquals(innerPromise.Task, tcs.Task))
                {
                    tcs.TrySetException(new SharpTSPromiseRejectedException(
                        new SharpTSTypeError("A promise cannot resolve to itself")));
                }
                else
                {
                    interpreter.AdoptInnerPromise(innerPromise.Task, tcs);
                }
            }
            else if (RuntimeValue.FromBoxed(value).IsObject)
            {
                PromiseBuiltIns.ResolveThenableIntoCapability(interpreter, tcs, value);
            }
            else
            {
                tcs.TrySetResult(value);
            }
        });

        var rejectCallback = new PromiseRejectCallback(reason =>
        {
            lock (settledLock)
            {
                if (settled) return;
                settled = true;
            }
            tcs.TrySetException(new SharpTSPromiseRejectedException(reason));
        });

        try
        {
            executor.Call(interpreter, [resolveCallback, rejectCallback]);
        }
        catch (Exception ex)
        {
            lock (settledLock)
            {
                if (!settled)
                {
                    settled = true;
                    object? reason = ex is Runtime.Exceptions.ThrowException thrown
                        ? thrown.Value
                        : ex.Message;
                    tcs.TrySetException(new SharpTSPromiseRejectedException(reason));
                }
            }
        }
    }

    private static ISharpTSCallable RequireExecutor(List<object?> arguments)
    {
        if (arguments.Count < 1 || arguments[0] is not ISharpTSCallable executor)
            throw new ThrowException(new SharpTSTypeError("Promise executor must be callable."));
        return executor;
    }

    /// <summary>
    /// Built-in Promise constructor callable — the target of <c>super(executor)</c>
    /// in user subclass constructors. Wires the executor to the bound
    /// receiver's capability.
    /// </summary>
    internal sealed class PromiseConstructorCallable : ISharpTSCallable, IReceiverBindable
    {
        private object? _boundReceiver;

        public int Arity() => 1;

        public ISharpTSCallable BindToReceiver(object receiver)
            => new PromiseConstructorCallable { _boundReceiver = receiver };

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            var receiver = _boundReceiver ?? interpreter.GetCurrentThis();
            if (receiver is SharpTSPromiseSubclassInstance instance)
                RunExecutor(interpreter, RequireExecutor(arguments), instance.Capability);
            return null;
        }
    }
}
