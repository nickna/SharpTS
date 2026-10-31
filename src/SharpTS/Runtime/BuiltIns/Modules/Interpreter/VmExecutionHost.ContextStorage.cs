namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

internal sealed partial class VmExecutionHost
{
    private VmContextStorageCapabilities? _contextStorage;

    internal void ConfigureContextStorage(VmContextStorageCapabilities storage)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_contextStorage is not null || _executionStarted || _leases.Count != 0 || _hasCompiledCallables)
                throw new InvalidOperationException("VM context storage must be configured once before execution.");
            _contextStorage = storage;
        }
    }

    internal VmContextStorageCapabilities? ContextStorage
    {
        get { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return _contextStorage; } }
    }
}