using System.Runtime.CompilerServices;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

public static partial class VmModuleInterpreter
{
    private static ConditionalWeakTable<object, VmErrorReceipt>? _vmErrors;

    private sealed class VmErrorReceipt(Execution.Interpreter owner)
    {
        internal readonly Execution.Interpreter Owner = owner;
        internal VmEvaluationLease? RetainedLease;
    }

    // Only an actual parsed VM Error construction or actual VM throw boundary calls
    // this recorder. Brand checks are CLR identities, never guest name/shape reads.
    internal static void RecordVmErrorValue(object? value, Execution.Interpreter boundaryOwner)
    {
        var owner = value switch
        {
            SharpTSInstance { RuntimeClass: SharpTSErrorClass } instance => instance.OwningInterpreter,
            SharpTSError => boundaryOwner,
            _ => null
        };
        if (owner is null) return;
        LazyInitializer.EnsureInitialized(ref _vmErrors).GetValue(value!, _ => new VmErrorReceipt(owner));
    }

    internal static void RetainVmErrorValue(object? value)
    {
        if (value is null || _vmErrors is not { } table || !table.TryGetValue(value, out var receipt)) return;
        lock (receipt)
        {
            if (receipt.RetainedLease is null && receipt.Owner.VmEvaluation is { IsDisposed: false } lease)
            {
                lease.RetainEscapedValue();
                receipt.RetainedLease = lease;
            }
        }
    }

    /// <summary>Recognizes only actual VM-produced native Error values with a recorded owner.</summary>
    public static bool IsVmErrorValue(object? value) =>
        value is SharpTSError or SharpTSInstance { RuntimeClass: SharpTSErrorClass }
        && _vmErrors?.TryGetValue(value, out _) == true;

    /// <summary>Reads through the Error's real realm and descriptor/accessor rules.</summary>
    public static object? ReadVmErrorProperty(object value, string name)
    {
        if (!IsVmErrorValue(value) || !_vmErrors!.TryGetValue(value, out var receipt)
            || receipt.Owner.IsDisposed || receipt.RetainedLease is { IsDisposed: true })
            throw new ArgumentException("Expected an owned VM Error value.", nameof(value));
        using var scope = receipt.RetainedLease?.Enter();
        try
        {
            var result = receipt.Owner.GetPropertyValue(value, name);
            RecordVmBoundaryValue(result, receipt.Owner);
            return VmExecutionHost.Current is { } host ? host.FromVm(result) : result;
        }
        catch (Exceptions.ThrowException exception)
        {
            RecordVmErrorValue(exception.Value, receipt.Owner);
            RetainVmErrorValue(exception.Value);
            throw;
        }
        finally { receipt.RetainedLease?.Publish(); }
    }
}
