using SharpTS.Runtime.BuiltIns.Modules.Interpreter;

namespace SharpTS.Runtime.Types;

// A context's global identity/storage is data, separate from each evaluator's
// queue and lifetime. Intrinsics are the actual existing resolver's results.
internal sealed class VmContextGlobalState
{
    internal readonly object Context;
    private readonly SharpTSObject? _properties;
    private readonly VmContextStorageCapabilities? _storage;
    internal readonly IReadOnlyDictionary<string, object?> Intrinsics;
    internal readonly SharpTSGlobalThis GlobalObject;

    internal VmContextGlobalState(object context, IReadOnlyDictionary<string, object?> intrinsics,
        VmContextStorageCapabilities? storage = null)
    {
        Context = context;
        _storage = storage;
        if (storage is null)
            _properties = context is SharpTSObject record ? record : new SharpTSObject((Dictionary<string, object?>)context);
        Intrinsics = intrinsics;
        GlobalObject = new SharpTSGlobalThis(this);
    }

    internal static bool Supports(object context) => context is SharpTSObject or Dictionary<string, object?>;
    internal bool HasExternalStorage => _storage is not null;
    internal bool UsesExternalStorage(object context) => _storage is not null && ReferenceEquals(context, Context);
    internal bool MatchesStorage(VmContextStorageCapabilities storage) =>
        _storage is not null && ReferenceEquals(_storage.Domain, storage.Domain);
    internal IEnumerable<string> OwnNames() => _storage is { } storage ? storage.OwnNames(Context) : _properties!.OwnVisibleStringKeys();
    internal IEnumerable<string> OwnEnumerableKeys() => _storage is not null
        ? OwnNames().Where(name => GetDescriptor(name)?.Enumerable == true) : _properties!.OwnEnumerableKeys();

    internal SharpTSPropertyDescriptor? GetDescriptor(string name) =>
        _storage is { } storage ? storage.ReadDescriptor(Context, name) : _properties!.GetOwnPropertyDescriptor(name);

    internal bool Set(string name, object? value)
    {
        if (Execution.VmEvaluationLease.CurrentInterpreter is { } owner)
            VmModuleInterpreter.RecordVmBoundaryValue(value, owner);
        var host = VmExecutionHost.Current;
        var raw = host is null ? value : host.FromVm(value);
        return _storage is { } storage ? storage.Set(Context, name, raw) : _properties!.TrySetProperty(name, raw);
    }

    internal bool Delete(string name) => _storage is { } storage ? storage.Delete(Context, name) : _properties!.DeleteProperty(name);

    internal bool Define(string name, SharpTSPropertyDescriptor descriptor)
    {
        if (Execution.VmEvaluationLease.CurrentInterpreter is { } owner)
        {
            if (descriptor.HasValue) VmModuleInterpreter.RecordVmBoundaryValue(descriptor.Value, owner);
            if (_storage is not null)
            {
                if (descriptor.HasGet) VmModuleInterpreter.RecordVmBoundaryValue(descriptor.RawGet ?? descriptor.Get, owner);
                if (descriptor.HasSet) VmModuleInterpreter.RecordVmBoundaryValue(descriptor.RawSet ?? descriptor.Set, owner);
            }
        }
        return _storage is { } storage ? storage.Define(Context, name, descriptor) : _properties!.DefineProperty(name, descriptor);
    }
}