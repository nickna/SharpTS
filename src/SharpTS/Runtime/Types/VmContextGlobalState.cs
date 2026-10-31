namespace SharpTS.Runtime.Types;

// A context's global identity/storage is data, separate from each evaluator's
// queue and lifetime. Intrinsics are the actual existing resolver's results.
internal sealed class VmContextGlobalState
{
    internal readonly object Context;
    internal readonly SharpTSObject Properties;
    internal readonly IReadOnlyDictionary<string, object?> Intrinsics;
    internal readonly SharpTSGlobalThis GlobalObject;

    internal VmContextGlobalState(object context, IReadOnlyDictionary<string, object?> intrinsics)
    {
        Context = context;
        Properties = context is SharpTSObject record ? record : new SharpTSObject((Dictionary<string, object?>)context);
        Intrinsics = intrinsics;
        GlobalObject = new SharpTSGlobalThis(this);
    }

    internal static bool Supports(object context) => context is SharpTSObject or Dictionary<string, object?>;

    internal SharpTSPropertyDescriptor? GetDescriptor(string name) => Properties.GetOwnPropertyDescriptor(name);

    internal void Set(string name, object? value)
    {
        if (Execution.VmEvaluationLease.CurrentInterpreter is { } owner)
            BuiltIns.Modules.Interpreter.VmModuleInterpreter.RecordVmBoundaryValue(value, owner);
        var host = BuiltIns.Modules.Interpreter.VmExecutionHost.Current;
        Properties.SetProperty(name, host is null ? value : host.FromVm(value));
    }

    internal bool Delete(string name) => Properties.DeleteProperty(name);

    internal bool Define(string name, SharpTSPropertyDescriptor descriptor)
    {
        if (descriptor.HasValue && Execution.VmEvaluationLease.CurrentInterpreter is { } owner)
            BuiltIns.Modules.Interpreter.VmModuleInterpreter.RecordVmBoundaryValue(descriptor.Value, owner);
        return Properties.DefineProperty(name, descriptor);
    }
}
