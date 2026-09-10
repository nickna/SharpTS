using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Promise metadata for one compilation. Declarations support forward references;
/// completion validates every handle and freezes the component for consumers.
/// </summary>
public sealed class EmittedPromiseRuntime
{
    internal EmittedPromiseRuntime() { }

    public bool IsComplete { get; private set; }

    private FieldBuilder? _prototypeField;
    /// <summary>Promise.prototype singleton dict (ECMA-262 §27.2.5): then/catch/finally/constructor + @@toStringTag.</summary>
    public FieldBuilder PrototypeField
    {
        get => Require(_prototypeField);
        internal set => Set(ref _prototypeField, value);
    }

    private MethodBuilder? _prototypePopulateMethod;
    /// <summary>Idempotent populate for <see cref="PrototypeField"/>.</summary>
    public MethodBuilder PrototypePopulateMethod
    {
        get => Require(_prototypePopulateMethod);
        internal set => Set(ref _prototypePopulateMethod, value);
    }

    private MethodBuilder? _thenHelperMethod;
    /// <summary>$Runtime.PromiseThenHelper(__this, args) — wraps PromiseThen for Promise.prototype.then.call patterns.</summary>
    public MethodBuilder ThenHelperMethod
    {
        get => Require(_thenHelperMethod);
        internal set => Set(ref _thenHelperMethod, value);
    }

    private MethodBuilder? _catchHelperMethod;
    /// <summary>$Runtime.PromiseCatchHelper(__this, args) — wraps PromiseCatch for Promise.prototype.catch.call patterns.</summary>
    public MethodBuilder CatchHelperMethod
    {
        get => Require(_catchHelperMethod);
        internal set => Set(ref _catchHelperMethod, value);
    }

    private MethodBuilder? _finallyHelperMethod;
    /// <summary>$Runtime.PromiseFinallyHelper(__this, args) — wraps PromiseFinally for Promise.prototype.finally.call patterns.</summary>
    public MethodBuilder FinallyHelperMethod
    {
        get => Require(_finallyHelperMethod);
        internal set => Set(ref _finallyHelperMethod, value);
    }

    // Promise support
    private MethodBuilder? _resolve;
    public MethodBuilder Resolve
    {
        get => Require(_resolve);
        internal set => Set(ref _resolve, value);
    }

    private MethodBuilder? _reject;
    public MethodBuilder Reject
    {
        get => Require(_reject);
        internal set => Set(ref _reject, value);
    }

    // Value-form `Promise.resolve` / `Promise.reject` wrappers that validate
    // `this` is Object per ECMA-262 §27.2.5.1 step 2. Used by the $TSFunction
    // value-form path so `let r = Promise.resolve; r.call(undefined, x)` throws.
    private MethodBuilder? _resolveStatic;
    public MethodBuilder ResolveStatic
    {
        get => Require(_resolveStatic);
        internal set => Set(ref _resolveStatic, value);
    }

    private MethodBuilder? _rejectStatic;
    public MethodBuilder RejectStatic
    {
        get => Require(_rejectStatic);
        internal set => Set(ref _rejectStatic, value);
    }

    // Same pattern for all/race/allSettled/any — value-form invocation must
    // validate `this` is Object before delegating to the iteration helper.
    private MethodBuilder? _allStatic;
    public MethodBuilder AllStatic
    {
        get => Require(_allStatic);
        internal set => Set(ref _allStatic, value);
    }

    private MethodBuilder? _allKeyedStatic;
    public MethodBuilder AllKeyedStatic
    {
        get => Require(_allKeyedStatic);
        internal set => Set(ref _allKeyedStatic, value);
    }

    private MethodBuilder? _raceStatic;
    public MethodBuilder RaceStatic
    {
        get => Require(_raceStatic);
        internal set => Set(ref _raceStatic, value);
    }

    private MethodBuilder? _allSettledStatic;
    public MethodBuilder AllSettledStatic
    {
        get => Require(_allSettledStatic);
        internal set => Set(ref _allSettledStatic, value);
    }

    private MethodBuilder? _allSettledKeyedStatic;
    public MethodBuilder AllSettledKeyedStatic
    {
        get => Require(_allSettledKeyedStatic);
        internal set => Set(ref _allSettledKeyedStatic, value);
    }

    private MethodBuilder? _anyStatic;
    public MethodBuilder AnyStatic
    {
        get => Require(_anyStatic);
        internal set => Set(ref _anyStatic, value);
    }

    private MethodBuilder? _all;
    public MethodBuilder All
    {
        get => Require(_all);
        internal set => Set(ref _all, value);
    }

    private MethodBuilder? _allPrimitive;
    public MethodBuilder AllPrimitive
    {
        get => Require(_allPrimitive);
        internal set => Set(ref _allPrimitive, value);
    }

    private MethodBuilder? _allKeyed;
    public MethodBuilder AllKeyed
    {
        get => Require(_allKeyed);
        internal set => Set(ref _allKeyed, value);
    }

    private MethodBuilder? _race;
    public MethodBuilder Race
    {
        get => Require(_race);
        internal set => Set(ref _race, value);
    }

    private MethodBuilder? _then;
    public MethodBuilder Then
    {
        get => Require(_then);
        internal set => Set(ref _then, value);
    }

    private MethodBuilder? _thenObjectPrimitive;
    /// <summary>$Runtime.PromiseThenObjectPrimitive(Task&lt;object?&gt;, Func&lt;object,object&gt;) -> Task&lt;object?&gt; — fulfillment-only direct Promise.all reaction whose boxed callback result is statically primitive.</summary>
    public MethodBuilder ThenObjectPrimitive
    {
        get => Require(_thenObjectPrimitive);
        internal set => Set(ref _thenObjectPrimitive, value);
    }

    private MethodBuilder? _thenPrimitive;
    /// <summary>$Runtime.PromiseThenPrimitive(Task&lt;object?&gt;, Func&lt;double,double&gt;) -> Task&lt;object?&gt; — stable intrinsic fulfillment-only numeric continuation whose primitive callback result cannot require thenable adoption.</summary>
    public MethodBuilder ThenPrimitive
    {
        get => Require(_thenPrimitive);
        internal set => Set(ref _thenPrimitive, value);
    }

    private MethodBuilder? _thenPrimitiveWithRejection;
    /// <summary>$Runtime.PromiseThenPrimitiveWithRejection(Task&lt;object?&gt;, Func&lt;double,double&gt;, Func&lt;object,double&gt;) -> Task&lt;object?&gt; — stable intrinsic numeric continuation with a typed rejection handler and no thenable-result adoption.</summary>
    public MethodBuilder ThenPrimitiveWithRejection
    {
        get => Require(_thenPrimitiveWithRejection);
        internal set => Set(ref _thenPrimitiveWithRejection, value);
    }

    private MethodBuilder? _catch;
    public MethodBuilder Catch
    {
        get => Require(_catch);
        internal set => Set(ref _catch, value);
    }

    private MethodBuilder? _finally;
    public MethodBuilder Finally
    {
        get => Require(_finally);
        internal set => Set(ref _finally, value);
    }

    private MethodBuilder? _trackTopLevelPromiseReaction;
    /// <summary>Keeps standalone event-loop execution alive until a discarded top-level Promise reaction settles, without pumping it before the current script job completes.</summary>
    public MethodBuilder TrackTopLevelPromiseReaction
    {
        get => Require(_trackTopLevelPromiseReaction);
        internal set => Set(ref _trackTopLevelPromiseReaction, value);
    }

    private MethodBuilder? _allSettled;
    public MethodBuilder AllSettled
    {
        get => Require(_allSettled);
        internal set => Set(ref _allSettled, value);
    }

    private MethodBuilder? _allSettledKeyed;
    public MethodBuilder AllSettledKeyed
    {
        get => Require(_allSettledKeyed);
        internal set => Set(ref _allSettledKeyed, value);
    }

    private MethodBuilder? _keyedMapResult;
    public MethodBuilder KeyedMapResult
    {
        get => Require(_keyedMapResult);
        internal set => Set(ref _keyedMapResult, value);
    }

    private MethodBuilder? _any;
    public MethodBuilder Any
    {
        get => Require(_any);
        internal set => Set(ref _any, value);
    }

    private MethodBuilder? _fromExecutor;
    public MethodBuilder FromExecutor
    {
        get => Require(_fromExecutor);
        internal set => Set(ref _fromExecutor, value);
    }

    private MethodBuilder? _fromDirectExecutor;
    /// <summary>$Runtime.PromiseFromDirectExecutor(Func&lt;object,object,object&gt;) -> Task&lt;object?&gt; — compiler-only fast path for an inline, two-argument Promise executor arrow whose CLR signature is known. Avoids materializing a $TSFunction and redispatching the executor through InvokeMethodValue.</summary>
    public MethodBuilder FromDirectExecutor
    {
        get => Require(_fromDirectExecutor);
        internal set => Set(ref _fromDirectExecutor, value);
    }

    private MethodBuilder? _withResolvers;
    public MethodBuilder WithResolvers
    {
        get => Require(_withResolvers);
        internal set => Set(ref _withResolvers, value);
    }

    private MethodBuilder? _unwrapPromiseReceiverMethod;
    /// <summary>$Runtime.UnwrapPromiseReceiver(object) -> Task&lt;object?&gt; — $Promise (incl. #242 subclasses) → .Task; anything else is cast to Task&lt;object?&gt;. Used by then/catch/finally emission so promise-typed receivers work regardless of representation.</summary>
    public MethodBuilder UnwrapPromiseReceiverMethod
    {
        get => Require(_unwrapPromiseReceiverMethod);
        internal set => Set(ref _unwrapPromiseReceiverMethod, value);
    }

    private MethodBuilder? _normalizePromiseListMethod;
    /// <summary>$Runtime.NormalizePromiseList(object, object, object, int, bool) -> object — incrementally resolves and wires Promise combinator elements while preserving observable iterator order. Kind 3 is Promise.all; the final flag selects a compiler-proven stable primitive input.</summary>
    public MethodBuilder NormalizePromiseListMethod
    {
        get => Require(_normalizePromiseListMethod);
        internal set => Set(ref _normalizePromiseListMethod, value);
    }

    private MethodBuilder? _wrapDerivedPromiseResultMethod;
    /// <summary>$Runtime.WrapDerivedPromiseResult(Task&lt;object?&gt; result, object receiver) -> object — completes species-based result construction for subclass then/catch/finally results after ObservePromiseConstructor has performed the synchronous own-constructor access (#242). For a $Promise SUBCLASS species, constructs a receiver-typed promise around the result task via the subclass's (object executor) constructor (PromiseFromExecutor adopts the task); for a general non-Promise species, routes to NewPromiseCapabilityResult (#349); for %Promise% (or no subclass receiver) returns the task unchanged.</summary>
    public MethodBuilder WrapDerivedPromiseResultMethod
    {
        get => Require(_wrapDerivedPromiseResultMethod);
        internal set => Set(ref _wrapDerivedPromiseResultMethod, value);
    }

    private MethodBuilder? _observePromiseConstructorMethod;
    /// <summary>$Runtime.ObservePromiseConstructor(object receiver) -> void — synchronously performs the observable own <c>constructor</c> getter step before a then/catch/finally reaction is scheduled. WrapDerivedPromiseResult handles the remaining species/result construction after the reaction task exists.</summary>
    public MethodBuilder ObservePromiseConstructorMethod
    {
        get => Require(_observePromiseConstructorMethod);
        internal set => Set(ref _observePromiseConstructorMethod, value);
    }

    private MethodBuilder? _newPromiseCapabilityResultMethod;
    /// <summary>$Runtime.NewPromiseCapabilityResult(object species, Task&lt;object?&gt; result) -> object — the general NewPromiseCapability (#349/#390): constructs <c>new species(executor)</c> through ConstructDynamicValue (a Type class species → Activator, a function-valued species → the JS new protocol, a non-constructor → TypeError per §7.3.22 step 5), capturing the resolve/reject the executor is handed via a <see cref="CapabilityType"/> holder, then adopts <c>result</c> into that capability (settlement scheduled on the event-loop SynchronizationContext) and returns the constructed (non-Promise) object. Body emitted late (after ConstructDynamicValue); the stub is pre-declared so WrapDerivedPromiseResult can call it.</summary>
    public MethodBuilder NewPromiseCapabilityResultMethod
    {
        get => Require(_newPromiseCapabilityResultMethod);
        internal set => Set(ref _newPromiseCapabilityResultMethod, value);
    }

    private MethodBuilder? _preparePromiseCapabilityMethod;
    /// <summary>$Runtime.PreparePromiseCapability(object constructor) -> object — synchronously performs NewPromiseCapability through construction and callable resolve/reject validation, returning the opaque capability holder. Promise static wrappers call this before starting their operation so constructor side effects and validation have spec order.</summary>
    public MethodBuilder PreparePromiseCapabilityMethod
    {
        get => Require(_preparePromiseCapabilityMethod);
        internal set => Set(ref _preparePromiseCapabilityMethod, value);
    }

    private MethodBuilder? _adoptPromiseCapabilityMethod;
    /// <summary>$Runtime.AdoptPromiseCapability(object capability, Task&lt;object?&gt; result) -> object — schedules settlement of a prepared capability from <c>result</c> and returns its constructed promise object.</summary>
    public MethodBuilder AdoptPromiseCapabilityMethod
    {
        get => Require(_adoptPromiseCapabilityMethod);
        internal set => Set(ref _adoptPromiseCapabilityMethod, value);
    }

    private MethodBuilder? _adoptCompletedPromiseCapabilityMethod;
    public MethodBuilder AdoptCompletedPromiseCapabilityMethod
    {
        get => Require(_adoptCompletedPromiseCapabilityMethod);
        internal set => Set(ref _adoptCompletedPromiseCapabilityMethod, value);
    }

    private MethodBuilder? _resolvePreparedPromiseCapabilityMethod;
    public MethodBuilder ResolvePreparedPromiseCapabilityMethod
    {
        get => Require(_resolvePreparedPromiseCapabilityMethod);
        internal set => Set(ref _resolvePreparedPromiseCapabilityMethod, value);
    }

    private MethodBuilder? _getPromiseCapabilityResolveMethod;
    public MethodBuilder GetPromiseCapabilityResolveMethod
    {
        get => Require(_getPromiseCapabilityResolveMethod);
        internal set => Set(ref _getPromiseCapabilityResolveMethod, value);
    }

    private MethodBuilder? _getPromiseCapabilityRejectMethod;
    public MethodBuilder GetPromiseCapabilityRejectMethod
    {
        get => Require(_getPromiseCapabilityRejectMethod);
        internal set => Set(ref _getPromiseCapabilityRejectMethod, value);
    }

    private MethodBuilder? _resolveValueMethod;
    public MethodBuilder ResolveValueMethod
    {
        get => Require(_resolveValueMethod);
        internal set => Set(ref _resolveValueMethod, value);
    }

    private MethodBuilder? _adoptPromiseCombinatorResultMethod;
    public MethodBuilder AdoptPromiseCombinatorResultMethod
    {
        get => Require(_adoptPromiseCombinatorResultMethod);
        internal set => Set(ref _adoptPromiseCombinatorResultMethod, value);
    }

    private MethodBuilder? _settlePromiseCombinatorResultMethod;
    public MethodBuilder SettlePromiseCombinatorResultMethod
    {
        get => Require(_settlePromiseCombinatorResultMethod);
        internal set => Set(ref _settlePromiseCombinatorResultMethod, value);
    }

    private MethodBuilder? _markNonAutoAwaitPromiseMethod;
    public MethodBuilder MarkNonAutoAwaitPromiseMethod
    {
        get => Require(_markNonAutoAwaitPromiseMethod);
        internal set => Set(ref _markNonAutoAwaitPromiseMethod, value);
    }

    private MethodBuilder? _shouldAutoAwaitPromiseMethod;
    public MethodBuilder ShouldAutoAwaitPromiseMethod
    {
        get => Require(_shouldAutoAwaitPromiseMethod);
        internal set => Set(ref _shouldAutoAwaitPromiseMethod, value);
    }

    private MethodBuilder? _coerceAwaitableToTaskMethod;
    /// <summary>$Runtime.CoerceAwaitableToTask(object value) -> Task&lt;object?&gt; — the await coercion for a value that is neither a $Promise nor a Task&lt;object?&gt;: an ordinary thenable (a value whose <c>then</c> member is callable) is adopted by invoking <c>then(resolve, reject)</c> into a fresh capability (#349); anything else becomes Task.FromResult(value). Called at every state-machine await's wrap-value site.</summary>
    public MethodBuilder CoerceAwaitableToTaskMethod
    {
        get => Require(_coerceAwaitableToTaskMethod);
        internal set => Set(ref _coerceAwaitableToTaskMethod, value);
    }

    private TypeBuilder? _capabilityType;
    /// <summary>$PromiseCapability — the host executor handed to a general (non-Promise) species constructor by NewPromiseCapabilityResult (#349): captures the resolve/reject functions (Capture, exposed as a Func&lt;object[],object&gt;) and drives them when the source task settles (Settle, an Action&lt;Task&lt;object&gt;&gt; continuation).</summary>
    public TypeBuilder CapabilityType
    {
        get => Require(_capabilityType);
        internal set => Set(ref _capabilityType, value);
    }

    private ConstructorBuilder? _capabilityCtor;
    public ConstructorBuilder CapabilityCtor
    {
        get => Require(_capabilityCtor);
        internal set => Set(ref _capabilityCtor, value);
    }

    private FieldBuilder? _capabilityResolveField;
    public FieldBuilder CapabilityResolveField
    {
        get => Require(_capabilityResolveField);
        internal set => Set(ref _capabilityResolveField, value);
    }

    private FieldBuilder? _capabilityRejectField;
    public FieldBuilder CapabilityRejectField
    {
        get => Require(_capabilityRejectField);
        internal set => Set(ref _capabilityRejectField, value);
    }

    private FieldBuilder? _capabilityInstanceField;
    public FieldBuilder CapabilityInstanceField
    {
        get => Require(_capabilityInstanceField);
        internal set => Set(ref _capabilityInstanceField, value);
    }

    private MethodBuilder? _capabilityCaptureMethod;
    public MethodBuilder CapabilityCaptureMethod
    {
        get => Require(_capabilityCaptureMethod);
        internal set => Set(ref _capabilityCaptureMethod, value);
    }

    private MethodBuilder? _capabilitySettleMethod;
    public MethodBuilder CapabilitySettleMethod
    {
        get => Require(_capabilitySettleMethod);
        internal set => Set(ref _capabilitySettleMethod, value);
    }

    private TypeBuilder? _resolveCallbackType;
    public TypeBuilder ResolveCallbackType
    {
        get => Require(_resolveCallbackType);
        internal set => Set(ref _resolveCallbackType, value);
    }

    private ConstructorBuilder? _resolveCallbackCtor;
    public ConstructorBuilder ResolveCallbackCtor
    {
        get => Require(_resolveCallbackCtor);
        internal set => Set(ref _resolveCallbackCtor, value);
    }

    private MethodBuilder? _resolveCallbackInvoke;
    public MethodBuilder ResolveCallbackInvoke
    {
        get => Require(_resolveCallbackInvoke);
        internal set => Set(ref _resolveCallbackInvoke, value);
    }

    private TypeBuilder? _rejectCallbackType;
    public TypeBuilder RejectCallbackType
    {
        get => Require(_rejectCallbackType);
        internal set => Set(ref _rejectCallbackType, value);
    }

    private ConstructorBuilder? _rejectCallbackCtor;
    public ConstructorBuilder RejectCallbackCtor
    {
        get => Require(_rejectCallbackCtor);
        internal set => Set(ref _rejectCallbackCtor, value);
    }

    private MethodBuilder? _rejectCallbackInvoke;
    public MethodBuilder RejectCallbackInvoke
    {
        get => Require(_rejectCallbackInvoke);
        internal set => Set(ref _rejectCallbackInvoke, value);
    }

    // Promise callback helpers (direct $TSFunction.Invoke without reflection)
    private MethodBuilder? _invokeCallback;
    public MethodBuilder InvokeCallback
    {
        get => Require(_invokeCallback);
        internal set => Set(ref _invokeCallback, value);
    }

    private MethodBuilder? _invokeCallbackNoArgs;
    public MethodBuilder InvokeCallbackNoArgs
    {
        get => Require(_invokeCallbackNoArgs);
        internal set => Set(ref _invokeCallbackNoArgs, value);
    }

    // Promise type - emitted for standalone assemblies
    // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSPromise
    private Type? _type;
    public Type Type
    {
        get => Require(_type);
        internal set => Set(ref _type, value);
    }

    private ConstructorBuilder? _ctor;
    public ConstructorBuilder Ctor
    {
        get => Require(_ctor);
        internal set => Set(ref _ctor, value);
    }

    private MethodBuilder? _taskGetter;
    public MethodBuilder TaskGetter
    {
        get => Require(_taskGetter);
        internal set => Set(ref _taskGetter, value);
    }

    private MethodBuilder? _typeResolve;
    public MethodBuilder TypeResolve
    {
        get => Require(_typeResolve);
        internal set => Set(ref _typeResolve, value);
    }

    private MethodBuilder? _typeReject;
    public MethodBuilder TypeReject
    {
        get => Require(_typeReject);
        internal set => Set(ref _typeReject, value);
    }

    private MethodBuilder? _getValueAsync;
    public MethodBuilder GetValueAsync
    {
        get => Require(_getValueAsync);
        internal set => Set(ref _getValueAsync, value);
    }

    private MethodBuilder? _observeDiscardedPromiseResult;
    /// <summary>
    /// Observes a Task/$Promise returned from a callback whose result is otherwise
    /// discarded, enabling process unhandled-rejection lifecycle events.
    /// </summary>
    public MethodBuilder ObserveDiscardedPromiseResult
    {
        get => Require(_observeDiscardedPromiseResult);
        internal set => Set(ref _observeDiscardedPromiseResult, value);
    }

    private MethodBuilder? _notifyPromiseRejectionHandler;
    /// <summary>
    /// Marks a source promise handled when a callable rejection reaction is
    /// attached through then/catch.
    /// </summary>
    public MethodBuilder NotifyPromiseRejectionHandler
    {
        get => Require(_notifyPromiseRejectionHandler);
        internal set => Set(ref _notifyPromiseRejectionHandler, value);
    }

    // Promise rejected exception
    private Type? _rejectedExceptionType;
    public Type RejectedExceptionType
    {
        get => Require(_rejectedExceptionType);
        internal set => Set(ref _rejectedExceptionType, value);
    }

    private ConstructorBuilder? _rejectedExceptionCtor;
    public ConstructorBuilder RejectedExceptionCtor
    {
        get => Require(_rejectedExceptionCtor);
        internal set => Set(ref _rejectedExceptionCtor, value);
    }

    private MethodBuilder? _rejectedExceptionReasonGetter;
    public MethodBuilder RejectedExceptionReasonGetter
    {
        get => Require(_rejectedExceptionReasonGetter);
        internal set => Set(ref _rejectedExceptionReasonGetter, value);
    }

    private MethodBuilder? _wrapTaskAsPromise;
    public MethodBuilder WrapTaskAsPromise
    {
        get => Require(_wrapTaskAsPromise);
        internal set => Set(ref _wrapTaskAsPromise, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Promise metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Promise metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = PrototypeField;
        _ = PrototypePopulateMethod;
        _ = ThenHelperMethod;
        _ = CatchHelperMethod;
        _ = FinallyHelperMethod;
        _ = Resolve;
        _ = Reject;
        _ = ResolveStatic;
        _ = RejectStatic;
        _ = AllStatic;
        _ = AllKeyedStatic;
        _ = RaceStatic;
        _ = AllSettledStatic;
        _ = AllSettledKeyedStatic;
        _ = AnyStatic;
        _ = All;
        _ = AllPrimitive;
        _ = AllKeyed;
        _ = Race;
        _ = Then;
        _ = ThenObjectPrimitive;
        _ = ThenPrimitive;
        _ = ThenPrimitiveWithRejection;
        _ = Catch;
        _ = Finally;
        _ = TrackTopLevelPromiseReaction;
        _ = AllSettled;
        _ = AllSettledKeyed;
        _ = KeyedMapResult;
        _ = Any;
        _ = FromExecutor;
        _ = FromDirectExecutor;
        _ = WithResolvers;
        _ = UnwrapPromiseReceiverMethod;
        _ = NormalizePromiseListMethod;
        _ = WrapDerivedPromiseResultMethod;
        _ = ObservePromiseConstructorMethod;
        _ = NewPromiseCapabilityResultMethod;
        _ = PreparePromiseCapabilityMethod;
        _ = AdoptPromiseCapabilityMethod;
        _ = AdoptCompletedPromiseCapabilityMethod;
        _ = ResolvePreparedPromiseCapabilityMethod;
        _ = GetPromiseCapabilityResolveMethod;
        _ = GetPromiseCapabilityRejectMethod;
        _ = ResolveValueMethod;
        _ = AdoptPromiseCombinatorResultMethod;
        _ = SettlePromiseCombinatorResultMethod;
        _ = MarkNonAutoAwaitPromiseMethod;
        _ = ShouldAutoAwaitPromiseMethod;
        _ = CoerceAwaitableToTaskMethod;
        _ = CapabilityType;
        _ = CapabilityCtor;
        _ = CapabilityResolveField;
        _ = CapabilityRejectField;
        _ = CapabilityInstanceField;
        _ = CapabilityCaptureMethod;
        _ = CapabilitySettleMethod;
        _ = ResolveCallbackType;
        _ = ResolveCallbackCtor;
        _ = ResolveCallbackInvoke;
        _ = RejectCallbackType;
        _ = RejectCallbackCtor;
        _ = RejectCallbackInvoke;
        _ = InvokeCallback;
        _ = InvokeCallbackNoArgs;
        _ = Type;
        _ = Ctor;
        _ = TaskGetter;
        _ = TypeResolve;
        _ = TypeReject;
        _ = GetValueAsync;
        _ = ObserveDiscardedPromiseResult;
        _ = NotifyPromiseRejectionHandler;
        _ = RejectedExceptionType;
        _ = RejectedExceptionCtor;
        _ = RejectedExceptionReasonGetter;
        _ = WrapTaskAsPromise;
        IsComplete = true;
    }
}
