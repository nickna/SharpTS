using SharpTS.Execution;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

public static partial class VmModuleInterpreter
{
    /// <summary>Performs ToPrimitive through the live actual VM owner's conversion methods.</summary>
    public static object? ToPrimitiveVmValue(object value, string hint)
        => RunVmCoercion(value, owner => owner.ToPrimitiveForVmValue(value, hint));

    /// <summary>String-converts an actual recorded VM object without losing its primitive kind.</summary>
    public static string ToStringVmValue(object value)
        => (string)RunVmCoercion(value, owner => owner.ToStringForBuiltInArgument(
            owner.ToPrimitiveForVmValue(value, "string")), adaptResult: false)!;
    private static object? RunVmCoercion(object value, Func<Execution.Interpreter, object?> operation, bool adaptResult = true)
    {
        if (value is not null && _vmValues?.TryGetValue(value, out _) == true)
        {
            var receipt = GetVmValueReceipt(value);
            return RunVmValueOperation(receipt, () => operation(receipt.Owner), adaptResult);
        }
        if (value is not null && IsVmErrorValue(value) && _vmErrors!.TryGetValue(value, out var error)
            && !error.Owner.IsDisposed && error.RetainedLease is not { IsDisposed: true })
            return RunVmValueOperation(error.Owner, error.RetainedLease, () => operation(error.Owner), adaptResult);
        throw new ArgumentException("Expected a recorded live VM value.", nameof(value));
    }
}
