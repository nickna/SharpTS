using System.Runtime.CompilerServices;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

public static partial class VmModuleInterpreter
{
    private static ConditionalWeakTable<SharpTSPromise, VmPromiseReceipt>? _vmPromises;
    private static ConditionalWeakTable<object, VmValueReceipt>? _vmValues;
    private sealed class VmValueReceipt(Execution.Interpreter owner)
    {
        internal readonly Execution.Interpreter Owner = owner;
        internal VmEvaluationLease? Lease;
    }
    private sealed class VmPromiseReceipt(Execution.Interpreter? owner)
    {
        internal readonly WeakReference<Execution.Interpreter>? Owner = owner is null ? null : new(owner);
    }

    internal static void RecordVmBoundaryValue(object? value, Execution.Interpreter owner)
    {
        if (value is CompiledCallableAdapter) return;
        if (value is not null && IsUnownedVmInput(value))
        {
            if (value is SharpTSPromise inputPromise) RecordVmPromise(inputPromise, null);
            return;
        }
        RecordVmErrorValue(value, owner);
        RetainVmErrorValue(value);
        if (value is SharpTSPromise promise) RecordVmPromise(promise, owner);
        if (IsCoreGuestObject(value) || IsCoreCallable(value)) RecordVmValue(value!, owner);
    }

    internal static void RecordVmPublishedValue(object? value)
    {
        if (value is SharpTSInstance { RuntimeClass: SharpTSErrorClass, OwningInterpreter: { } owner })
            RecordVmErrorValue(value, owner);
        RetainVmErrorValue(value);
        if (VmEvaluationLease.CurrentInterpreter is { } boundaryOwner)
            RecordVmBoundaryValue(value, boundaryOwner);
        else if (value is SharpTSPromise promise) RecordVmPromise(promise, null);
    }

    private static bool IsCoreCallable(object? value) => value is ISharpTSCallable
        && ReferenceEquals(value.GetType().Assembly, typeof(ISharpTSCallable).Assembly);

    private static bool IsCoreGuestObject(object? value) => value is
        SharpTSObject or SharpTSObjectPrototype or SharpTSFunctionPrototype or SharpTSPromisePrototype
        or SharpTSClassPrototype or SharpTSInstance or SharpTSArray or SharpTSPromise;

    private static void RecordVmValue(object value, Execution.Interpreter owner)
    {
        if (owner.IsDisposed) return;
        var receipt = LazyInitializer.EnsureInitialized(ref _vmValues).GetValue(value, _ => new VmValueReceipt(owner));
        lock (receipt)
        {
            if (receipt.Lease is null && owner.VmEvaluation is { IsDisposed: false } lease
                && ReferenceEquals(receipt.Owner, owner))
            {
                lease.RetainEscapedValue();
                receipt.Lease = lease;
            }
        }
    }

    private static VmValueReceipt GetVmValueReceipt(object value)
        => _vmValues?.TryGetValue(value, out var receipt) == true && !receipt.Owner.IsDisposed
            && receipt.Lease is not { IsDisposed: true } ? receipt
            : throw new ArgumentException("Expected a recorded live VM value.", nameof(value));

    /// <summary>Recognizes only values produced by recorded VM boundaries/property/prototype operations.</summary>
    public static bool IsVmValue(object? value) => value is not null
        && _vmValues?.TryGetValue(value, out _) == true;

    /// <summary>Recognizes actual Core callables produced by a recorded VM operation.</summary>
    public static bool IsVmCallableValue(object? value) => IsCoreCallable(value)
        && (IsVmValue(value) || IsVmFacadeCallable(value));

    private static object? RunVmValueOperation(VmValueReceipt receipt, Func<object?> operation)
    {
        using var scope = receipt.Lease?.Enter();
        try
        {
            var result = operation();
            RecordVmBoundaryValue(result, receipt.Owner);
            return VmExecutionHost.Current is { } host ? host.FromVm(result) : result;
        }
        catch (Exceptions.ThrowException exception)
        {
            RecordVmBoundaryValue(exception.Value, receipt.Owner);
            exception.Data["__tsValue"] = VmExecutionHost.Current is { } host ? host.FromVm(exception.Value) : exception.Value;
            throw;
        }
        finally { receipt.Lease?.Publish(); }
    }

    /// <summary>Reads the real recorded owner's descriptor surface without a CLR shape fallback.</summary>
    public static object? ReadVmValueProperty(object value, string name)
    {
        var receipt = GetVmValueReceipt(value);
        return RunVmValueOperation(receipt, () => value is SharpTSBuiltInConstructor
            ? receipt.Owner.GetProperty(value, name) : receipt.Owner.GetPropertyValue(value, name));
    }

    /// <summary>Reads the actual recorded value's JavaScript prototype.</summary>
    public static object? GetVmValuePrototype(object value)
    {
        var receipt = GetVmValueReceipt(value);
        return RunVmValueOperation(receipt, () => ObjectBuiltIns.PrototypeOf(receipt.Owner, value));
    }

    /// <summary>Invokes only the recorded actual Core callable with an exact receiver and arguments.</summary>
    public static object? InvokeVmCallable(object callable, object? receiver, object?[] arguments)
    {
        if (IsVmFacadeCallable(callable)) return InvokeVmFacadeCallable(callable, arguments);
        if (!IsVmCallableValue(callable))
            throw new ArgumentException("Expected a recorded VM callable.", nameof(callable));
        var receipt = GetVmValueReceipt(callable);
        return RunVmValueOperation(receipt, () =>
        {
            var host = VmExecutionHost.Current;
            var actualReceiver = host is null ? receiver : host.ToVm(receiver);
            var args = arguments.Select(value => VmContext.WrapVmCompiledCallable(host is null ? value : host.ToVm(value))).ToList();
            return FunctionBuiltIns.CallWithThis(receipt.Owner, (ISharpTSCallable)callable, actualReceiver, args);
        });
    }

    /// <summary>Checks the actual recorded value's constructor eligibility without invoking it.</summary>
    public static bool IsVmConstructorValue(object value)
    {
        _ = GetVmValueReceipt(value);
        return !ReflectBuiltIns.IsNotConstructor(value);
    }
    /// <summary>Constructs only a value produced by a recorded VM operation, preserving explicit newTarget.</summary>
    public static object? ConstructVmCallable(object constructor, object?[] arguments, object? newTarget)
    {
        var receipt = GetVmValueReceipt(constructor);
        if (newTarget is not null && !ReferenceEquals(newTarget, constructor))
            _ = GetVmValueReceipt(newTarget);
        return RunVmValueOperation(receipt, () =>
        {
            var host = VmExecutionHost.Current;
            var args = arguments.Select(value => VmContext.WrapVmCompiledCallable(host is null ? value : host.ToVm(value))).ToList();
            return receipt.Owner.Construct(constructor, args, newTarget ?? constructor);
        });
    }
    private static void RecordVmPromise(SharpTSPromise promise, Execution.Interpreter? owner)
    {
        var table = LazyInitializer.EnsureInitialized(ref _vmPromises);
        table.GetValue(promise, value =>
        {
            var receipt = new VmPromiseReceipt(owner);
            value.Task.ContinueWith(static (task, state) =>
            {
                if (!task.IsCompletedSuccessfully) return;
                var source = (VmPromiseReceipt)state!;
                if (source.Owner?.TryGetTarget(out var boundaryOwner) == true)
                    RecordVmBoundaryValue(task.Result, boundaryOwner);
                else RecordVmPublishedValue(task.Result);
            }, receipt, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return receipt;
        });
    }

    /// <summary>Recognizes the actual Core undefined singleton at a VM transport edge.</summary>
    public static bool IsVmUndefinedValue(object? value) => ReferenceEquals(value, SharpTSUndefined.Instance);

    /// <summary>Returns the actual Core undefined singleton for the paired VM transport.</summary>
    public static object GetVmUndefinedValue() => SharpTSUndefined.Instance;

    /// <summary>Restores only the actual emitted callback carried by the typed VM adapter.</summary>
    public static object? UnwrapVmCallback(object? value) => value is CompiledCallableAdapter callback ? callback.Original : value;

    /// <summary>Recognizes actual Core Promises recorded at a VM transport edge.</summary>
    public static bool IsVmPromiseValue(object? value) => value is SharpTSPromise promise
        && _vmPromises?.TryGetValue(promise, out _) == true;

    /// <summary>Returns the actual recorded Promise task; no new task/Promise identity is made.</summary>
    public static Task<object?> GetVmPromiseTask(object value) =>
        value is SharpTSPromise promise && IsVmPromiseValue(promise) ? promise.Task
            : throw new ArgumentException("Expected an owned VM Promise value.", nameof(value));

    /// <summary>Recognizes only the actual typed Core thrown/rejected-value carriers.</summary>
    public static bool IsVmExceptionValue(Exception exception) =>
        exception is Exceptions.ThrowException or SharpTSPromiseRejectedException;

    /// <summary>Reads the original typed value without consulting arbitrary CLR metadata.</summary>
    public static object? GetVmExceptionValue(Exception exception) => exception switch
    {
        Exceptions.ThrowException thrown => thrown.Value,
        SharpTSPromiseRejectedException rejected => rejected.Reason,
        _ => throw new ArgumentException("Expected a VM guest exception value.", nameof(exception))
    };
}
