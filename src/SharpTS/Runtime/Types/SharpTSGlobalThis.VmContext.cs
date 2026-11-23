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

    private bool DefineSandboxDataAttributes(string name, SharpTSPropertyDescriptor request)
    {
        var current = GetOwnPropertyDescriptor(name)!;
        var completed = CompleteSandboxDefinition(name, current, request);
        if (completed is null) return false;
        bool changed = current.Writable != completed.Writable || current.Enumerable != completed.Enumerable
            || current.Configurable != completed.Configurable;
        if (!changed) return DefineEmptySandboxProperty(name, current);

        // Contextification forwards an omitted value as undefined. When the
        // newly defined internal binding is writable, its value is also written
        // back to the sandbox. A read-only binding keeps that value privately.
        var published = new SharpTSPropertyDescriptor(completed.Writable ? completed.Value : SharpTSUndefined.Instance,
            writable: completed.Writable, enumerable: completed.Enumerable, configurable: completed.Configurable)
        { HasValue = true };
        if (!_vmContext!.Define(name, published)) return false;
        RememberInternalDefinition(name, completed);
        return true;
    }

    private static SharpTSPropertyDescriptor? CompleteSandboxDefinition(string name,
        SharpTSPropertyDescriptor current, SharpTSPropertyDescriptor request)
    {
        // Validate against the visible descriptor before changing either store.
        var slot = new SharpTSObject([]);
        slot.MirrorCompletedPropertyDefinition(name, current);
        return slot.DefineProperty(name, request) ? slot.GetOwnPropertyDescriptor(name) : null;
    }

    private void RememberInternalDefinition(string name, SharpTSPropertyDescriptor completed)
    {
        _properties.MirrorCompletedPropertyDefinition(name, completed);
        _deletedProperties.Remove(name);
        _vmWrittenProperties?.Add(name);
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
