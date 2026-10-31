using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

internal sealed class VmInputPropertyAccess
{
    internal readonly Type Domain;
    internal readonly Func<object, bool> Admits;
    internal readonly Func<object, string, object?> Read;
    internal readonly Func<object, int> GetRole;
    internal readonly Func<object, string, object?, bool>? Write;
    internal readonly Func<object, object?, object?>? ReadToStringTag;
    internal VmInputPropertyAccess(object domain, Func<object, bool> admits, Func<object, string, object?> read, Func<object, int> getRole, Func<object, string, object?, bool>? write = null, Func<object, object?, object?>? readTag = null)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(admits);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(getRole);
        if (domain is not Type type || domain.GetType() != typeof(object).GetType())
            throw new ArgumentException("VM input properties require an actual runtime type domain.", nameof(domain));
        foreach (var callback in new Delegate[] { admits, read, getRole })
            if (callback.Target is not null || !callback.Method.IsStatic || callback.GetInvocationList().Length != 1)
                throw new ArgumentException("VM input properties require single static callbacks.", nameof(callback));
        if (write is not null && (write.Target is not null || !write.Method.IsStatic || write.GetInvocationList().Length != 1))
            throw new ArgumentException("VM input writes require a single static callback.", nameof(write));
        if (readTag is not null && (readTag.Target is not null || !readTag.Method.IsStatic || readTag.GetInvocationList().Length != 1))
            throw new ArgumentException("VM input tags require a single static callback.", nameof(readTag));
        Domain = type; Admits = admits; Read = read; GetRole = getRole; Write = write; ReadToStringTag = readTag;
    }
    internal VmInputPropertyAccess WithWrites(Func<object, string, object?, bool> write) => new(Domain, Admits, Read, GetRole, write, ReadToStringTag);
    internal VmInputPropertyAccess WithToStringTag(Func<object, object?, object?> readTag) => new(Domain, Admits, Read, GetRole, Write, readTag);
    internal bool Matches(VmInputPropertyAccess other) => ReferenceEquals(Domain, other.Domain)
        && Admits == other.Admits && Read == other.Read && GetRole == other.GetRole && Write == other.Write && ReadToStringTag == other.ReadToStringTag;
}

internal sealed partial class VmExecutionHost
{
    private VmInputPropertyAccess? _inputPropertyAccess;
    private bool _hasInputPropertyValues;
    internal VmInputPropertyAccess? InputPropertyAccess
    {
        get { lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); return _inputPropertyAccess; } }
    }
    internal void ConfigureInputPropertyAccess(VmInputPropertyAccess access)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_inputPropertyAccess is not null || _executionStarted || _leases.Count != 0
                || _hasCompiledCallables || _hasInputPropertyValues)
                throw new InvalidOperationException("VM input properties must be configured once before execution.");
            _inputPropertyAccess = access;
        }
    }
    internal void ConfigureInputPropertyWrites(Func<object, string, object?, bool> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_inputPropertyAccess is null || _inputPropertyAccess.Write is not null
                || _executionStarted || _leases.Count != 0 || _hasCompiledCallables || _hasInputPropertyValues)
                throw new InvalidOperationException("VM input writes require the configured reader before first execution or admission.");
            _inputPropertyAccess = _inputPropertyAccess.WithWrites(write);
        }
    }
    internal void ConfigureInputToStringTag(Func<object, object?, object?> readTag)
    {
        ArgumentNullException.ThrowIfNull(readTag);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_inputPropertyAccess is null || _inputPropertyAccess.ReadToStringTag is not null
                || _executionStarted || _leases.Count != 0 || _hasCompiledCallables || _hasInputPropertyValues)
                throw new InvalidOperationException("VM input tags require the configured reader before first execution or admission.");
            _inputPropertyAccess = _inputPropertyAccess.WithToStringTag(readTag);
        }
    }
    private object? AdaptInputPropertyValue(object? value)
    {
        if (value is null or VmInputPropertyView) return value;
        VmInputPropertyAccess? access;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _hasInputPropertyValues = true;
            access = _inputPropertyAccess;
        }
        if (access is null || !access.Admits(value)) return value;
        return VmInputPropertyView.Get(value, access);
    }
}

public static partial class VmModuleInterpreter
{
    /// <summary>Configures this output's actual well-known tag getter without retaining an active host.</summary>
    public static void ConfigureExecutionHostInputToStringTag(object host, Func<object, object?, object?> readTag)
    {
        if (host is not VmExecutionHost owner)
            throw new ArgumentException("Expected an owned VM execution host.", nameof(host));
        owner.ConfigureInputToStringTag(readTag);
    }
    /// <summary>Configures actual input writes before the reader admits any value.</summary>
    public static void ConfigureExecutionHostInputPropertyWrites(object host, Func<object, string, object?, bool> set)
    {
        if (host is not VmExecutionHost owner)
            throw new ArgumentException("Expected an owned VM execution host.", nameof(host));
        owner.ConfigureInputPropertyWrites(set);
    }
    /// <summary>Configures this output's exact input property readers before its host executes.</summary>
    public static void ConfigureExecutionHostInputPropertyAccess(object host, object domain,
        Func<object, bool> admits, Func<object, string, object?> read, Func<object, int> getRole)
    {
        if (host is not VmExecutionHost owner)
            throw new ArgumentException("Expected an owned VM execution host.", nameof(host));
        owner.ConfigureInputPropertyAccess(new VmInputPropertyAccess(domain, admits, read, getRole));
    }
}