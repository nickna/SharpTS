using System.Runtime.CompilerServices;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

public static partial class VmModuleInterpreter
{
    private static ConditionalWeakTable<object, object>? _vmInputs;
    private static readonly object InputReceipt = new();

    // Actual context/argument boundary identities are not creation receipts.
    // Existing VM-produced values retain their original ownership record.
    internal static void RecordVmInputValue(object? value)
    {
        if (value is null || IsVmValue(value) || IsVmErrorValue(value)) return;
        LazyInitializer.EnsureInitialized(ref _vmInputs).GetValue(value, _ => InputReceipt);
    }

    private static bool IsUnownedVmInput(object value) => _vmInputs?.TryGetValue(value, out _) == true
        && !IsVmValue(value) && !IsVmErrorValue(value);

    internal static bool IsVmInputValue(object value) => IsUnownedVmInput(value);
}
