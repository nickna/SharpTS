using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional promise timer and async interval metadata, enabled with Promise support.
/// Declarations support forward calls; completion validates and freezes every handle.
/// </summary>
public sealed class EmittedTimerPromiseRuntime
{
    internal EmittedTimerPromiseRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _setTimeoutPromise;
    public MethodBuilder SetTimeoutPromise
    {
        get => Require(_setTimeoutPromise);
        internal set => Set(ref _setTimeoutPromise, value);
    }

    private MethodBuilder? _setTimeoutPromiseWithSignal;
    public MethodBuilder SetTimeoutPromiseWithSignal
    {
        get => Require(_setTimeoutPromiseWithSignal);
        internal set => Set(ref _setTimeoutPromiseWithSignal, value);
    }

    private MethodBuilder? _setImmediatePromise;
    public MethodBuilder SetImmediatePromise
    {
        get => Require(_setImmediatePromise);
        internal set => Set(ref _setImmediatePromise, value);
    }

    private MethodBuilder? _setImmediatePromiseWithSignal;
    public MethodBuilder SetImmediatePromiseWithSignal
    {
        get => Require(_setImmediatePromiseWithSignal);
        internal set => Set(ref _setImmediatePromiseWithSignal, value);
    }

    private MethodBuilder? _setIntervalAsyncIterable;
    public MethodBuilder SetIntervalAsyncIterable
    {
        get => Require(_setIntervalAsyncIterable);
        internal set => Set(ref _setIntervalAsyncIterable, value);
    }

    private MethodBuilder? _setIntervalAsyncIterableWithSignal;
    public MethodBuilder SetIntervalAsyncIterableWithSignal
    {
        get => Require(_setIntervalAsyncIterableWithSignal);
        internal set => Set(ref _setIntervalAsyncIterableWithSignal, value);
    }

    private MethodBuilder? _extractTimerOptionsToken;
    public MethodBuilder ExtractTimerOptionsToken
    {
        get => Require(_extractTimerOptionsToken);
        internal set => Set(ref _extractTimerOptionsToken, value);
    }

    private TypeBuilder? _timerPromiseClosureType;
    public TypeBuilder TimerPromiseClosureType
    {
        get => Require(_timerPromiseClosureType);
        internal set => Set(ref _timerPromiseClosureType, value);
    }

    private FieldBuilder? _timerPromiseClosureValue;
    public FieldBuilder TimerPromiseClosureValue
    {
        get => Require(_timerPromiseClosureValue);
        internal set => Set(ref _timerPromiseClosureValue, value);
    }

    private FieldBuilder? _timerPromiseClosureToken;
    public FieldBuilder TimerPromiseClosureToken
    {
        get => Require(_timerPromiseClosureToken);
        internal set => Set(ref _timerPromiseClosureToken, value);
    }

    private ConstructorBuilder? _timerPromiseClosureCtor;
    public ConstructorBuilder TimerPromiseClosureCtor
    {
        get => Require(_timerPromiseClosureCtor);
        internal set => Set(ref _timerPromiseClosureCtor, value);
    }

    private MethodBuilder? _timerPromiseClosureOnComplete;
    public MethodBuilder TimerPromiseClosureOnComplete
    {
        get => Require(_timerPromiseClosureOnComplete);
        internal set => Set(ref _timerPromiseClosureOnComplete, value);
    }

    private TypeBuilder? _asyncIntervalClosureType;
    public TypeBuilder AsyncIntervalClosureType
    {
        get => Require(_asyncIntervalClosureType);
        internal set => Set(ref _asyncIntervalClosureType, value);
    }

    private FieldBuilder? _asyncIntervalClosureDelayMs;
    public FieldBuilder AsyncIntervalClosureDelayMs
    {
        get => Require(_asyncIntervalClosureDelayMs);
        internal set => Set(ref _asyncIntervalClosureDelayMs, value);
    }

    private FieldBuilder? _asyncIntervalClosureValue;
    public FieldBuilder AsyncIntervalClosureValue
    {
        get => Require(_asyncIntervalClosureValue);
        internal set => Set(ref _asyncIntervalClosureValue, value);
    }

    private FieldBuilder? _asyncIntervalClosureDone;
    public FieldBuilder AsyncIntervalClosureDone
    {
        get => Require(_asyncIntervalClosureDone);
        internal set => Set(ref _asyncIntervalClosureDone, value);
    }

    private FieldBuilder? _asyncIntervalClosureSelf;
    public FieldBuilder AsyncIntervalClosureSelf
    {
        get => Require(_asyncIntervalClosureSelf);
        internal set => Set(ref _asyncIntervalClosureSelf, value);
    }

    private FieldBuilder? _asyncIntervalClosureToken;
    public FieldBuilder AsyncIntervalClosureToken
    {
        get => Require(_asyncIntervalClosureToken);
        internal set => Set(ref _asyncIntervalClosureToken, value);
    }

    private ConstructorBuilder? _asyncIntervalClosureCtor;
    public ConstructorBuilder AsyncIntervalClosureCtor
    {
        get => Require(_asyncIntervalClosureCtor);
        internal set => Set(ref _asyncIntervalClosureCtor, value);
    }

    private MethodBuilder? _asyncIntervalClosureNext;
    public MethodBuilder AsyncIntervalClosureNext
    {
        get => Require(_asyncIntervalClosureNext);
        internal set => Set(ref _asyncIntervalClosureNext, value);
    }

    private MethodBuilder? _asyncIntervalClosureReturn;
    public MethodBuilder AsyncIntervalClosureReturn
    {
        get => Require(_asyncIntervalClosureReturn);
        internal set => Set(ref _asyncIntervalClosureReturn, value);
    }

    private MethodBuilder? _asyncIntervalClosureGetSelf;
    public MethodBuilder AsyncIntervalClosureGetSelf
    {
        get => Require(_asyncIntervalClosureGetSelf);
        internal set => Set(ref _asyncIntervalClosureGetSelf, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"TimerPromises metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("TimerPromises metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = SetTimeoutPromise;
        _ = SetTimeoutPromiseWithSignal;
        _ = SetImmediatePromise;
        _ = SetImmediatePromiseWithSignal;
        _ = SetIntervalAsyncIterable;
        _ = SetIntervalAsyncIterableWithSignal;
        _ = ExtractTimerOptionsToken;
        _ = TimerPromiseClosureType;
        _ = TimerPromiseClosureValue;
        _ = TimerPromiseClosureToken;
        _ = TimerPromiseClosureCtor;
        _ = TimerPromiseClosureOnComplete;
        _ = AsyncIntervalClosureType;
        _ = AsyncIntervalClosureDelayMs;
        _ = AsyncIntervalClosureValue;
        _ = AsyncIntervalClosureDone;
        _ = AsyncIntervalClosureSelf;
        _ = AsyncIntervalClosureToken;
        _ = AsyncIntervalClosureCtor;
        _ = AsyncIntervalClosureNext;
        _ = AsyncIntervalClosureReturn;
        _ = AsyncIntervalClosureGetSelf;
        IsComplete = true;
    }
}
