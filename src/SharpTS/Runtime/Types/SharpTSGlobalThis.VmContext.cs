namespace SharpTS.Runtime.Types;

public sealed partial class SharpTSGlobalThis
{
    private readonly VmContextGlobalState? _vmContext;

    internal SharpTSGlobalThis(VmContextGlobalState state) => _vmContext = state;

    private object? ResolveIntrinsic(string name) => _vmContext is { } state
        ? state.Intrinsics.GetValueOrDefault(name) : _realmIntrinsicResolver?.Invoke(name);

    private SharpTSPropertyDescriptor? ContextDescriptor(string name) => _vmContext?.GetDescriptor(name);

    private bool DefineEmptySandboxProperty(string name, SharpTSPropertyDescriptor sandboxProperty)
    {
        // Node's contextified global forwards an empty descriptor to the sandbox
        // as { value: undefined }. The internal binding keeps its original value
        // and becomes visible again if the caller later deletes the sandbox slot.
        var internalProperty = _properties.GetOwnPropertyDescriptor(name);
        if (internalProperty is { HasValue: true, Configurable: false, Writable: false }
            || (internalProperty is null && IsNonConfigurableIntrinsic(name))
            || (!sandboxProperty.Configurable && !sandboxProperty.Writable))
            return true;

        return _vmContext!.Define(name, new SharpTSPropertyDescriptor
        {
            HasValue = true,
            Value = SharpTSUndefined.Instance
        });
    }

    internal bool TryGetContextIntrinsic(string name, out object? value)
    {
        value = null;
        return _vmContext?.Intrinsics.TryGetValue(name, out value) == true;
    }

    // Instantiation reserves the internal global binding without publishing an
    // uninitialized var onto the caller's ordinary context object.
    internal bool DefineGlobalVariable(string name, SharpTSPropertyDescriptor descriptor)
    {
        if (_vmContext is null) return DefineProperty(name, descriptor);
        if (!_properties.DefineProperty(name, descriptor)) return false;
        return true;
    }

    internal object AccessorReceiver => _vmContext?.Context ?? this;

    internal bool HasRestrictedVmLexicalProperty(string name) =>
        IsNonConfigurableIntrinsic(name)
        || _properties.GetOwnPropertyDescriptor(name) is { Configurable: false };

    internal bool RejectsVmInternalDataWrite(string name) => _vmContext is not null
        && _properties.GetOwnPropertyDescriptor(name) is { HasValue: true, Writable: false };

    internal bool IsInternalOnlyVmProperty(string name) => _vmContext is not null
        && ContextDescriptor(name) is null && _properties.HasProperty(name);

    internal bool NeedsVmGlobalVariable(string name) => _vmContext is not null && !HasInternalProperty(name);
}
