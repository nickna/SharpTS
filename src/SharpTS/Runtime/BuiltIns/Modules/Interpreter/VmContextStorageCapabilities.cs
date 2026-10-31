using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

// A context marker retains only the original data target and these static output
// capabilities. Conversion and thrown-value adaptation belong to the active host.
internal sealed class VmContextStorageCapabilities
{
    internal readonly Type Domain;
    internal readonly Func<object, bool> Admits;
    private readonly Func<object, string[]> _ownNames;
    private readonly Func<object, string, object?[]?> _readDescriptor;
    private readonly Func<object, string, object?, bool> _set;
    private readonly Func<object, string, object?[], bool> _define;
    private readonly Func<object, string, bool> _delete;
    private readonly Func<object, bool> _isExtensible;
    private readonly Action<object> _preventExtensions;

    internal VmContextStorageCapabilities(object domain, Func<object, bool> admits,
        Func<object, string[]> ownNames, Func<object, string, object?[]?> readDescriptor,
        Func<object, string, object?, bool> set, Func<object, string, object?[], bool> define,
        Func<object, string, bool> delete, Func<object, bool> isExtensible, Action<object> preventExtensions)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (domain is not Type type || domain.GetType() != typeof(object).GetType())
            throw new ArgumentException("VM context storage requires an actual runtime type domain.", nameof(domain));
        foreach (var callback in new Delegate[] { admits, ownNames, readDescriptor, set, define, delete, isExtensible, preventExtensions })
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (callback.Target is not null || !callback.Method.IsStatic || callback.GetInvocationList().Length != 1)
                throw new ArgumentException("VM context storage requires single static callbacks.", nameof(callback));
        }
        Domain = type;
        Admits = admits;
        _ownNames = ownNames;
        _readDescriptor = readDescriptor;
        _set = set;
        _define = define;
        _delete = delete;
        _isExtensible = isExtensible;
        _preventExtensions = preventExtensions;
    }

    private static T Operate<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (Exception exception)
        {
            if (VmExecutionHost.Current is { } host && host.TryGetThrownValue(exception, out var value))
                throw new Exceptions.ThrowException(host.ToVm(value));
            throw;
        }
    }

    internal string[] OwnNames(object target) => Operate(() =>
    {
        var names = _ownNames(target);
        if (names is null || names.Any(name => name is null))
            throw new InvalidOperationException("Invalid VM context storage key snapshot.");
        return names;
    });

    internal SharpTSPropertyDescriptor? ReadDescriptor(object target, string name) => Operate(() =>
    {
        var wire = _readDescriptor(target, name);
        if (wire is null) return null;
        if (wire.Length != 7 || wire[0] is not int mask || (mask & ~63) != 0
            || wire[2] is not bool writable || wire[3] is not bool enumerable || wire[4] is not bool configurable
            || (mask & 3) != 0 && (mask & 48) != 0)
            throw new InvalidOperationException("Invalid VM context storage descriptor snapshot.");
        return new SharpTSPropertyDescriptor
        {
            HasValue = (mask & 1) != 0, Value = wire[1], HasWritable = (mask & 2) != 0, Writable = writable,
            HasEnumerable = (mask & 4) != 0, Enumerable = enumerable, HasConfigurable = (mask & 8) != 0,
            Configurable = configurable, HasGet = (mask & 16) != 0, RawGet = wire[5],
            HasSet = (mask & 32) != 0, RawSet = wire[6], Get = wire[5] as ISharpTSCallable, Set = wire[6] as ISharpTSCallable,
        };
    });

    internal bool Set(object target, string name, object? value) => Operate(() => _set(target, name, value));
    internal bool Define(object target, string name, SharpTSPropertyDescriptor descriptor)
    {
        int mask = (descriptor.HasValue ? 1 : 0) | (descriptor.HasWritable ? 2 : 0)
            | (descriptor.HasEnumerable ? 4 : 0) | (descriptor.HasConfigurable ? 8 : 0)
            | (descriptor.HasGet ? 16 : 0) | (descriptor.HasSet ? 32 : 0);
        var host = VmExecutionHost.Current;
        object? Out(object? value) => host is null ? value : host.FromVm(value);
        object?[] wire = [mask, Out(descriptor.Value), descriptor.Writable, descriptor.Enumerable, descriptor.Configurable,
            Out(descriptor.RawGet ?? descriptor.Get ?? (object)SharpTSUndefined.Instance),
            Out(descriptor.RawSet ?? descriptor.Set ?? (object)SharpTSUndefined.Instance)];
        return Operate(() => _define(target, name, wire));
    }
    internal bool Delete(object target, string name) => Operate(() => _delete(target, name));
    internal bool IsExtensible(object target) => Operate(() => _isExtensible(target));
    internal void PreventExtensions(object target) => Operate(() => { _preventExtensions(target); return true; });
}