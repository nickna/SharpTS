using System.Runtime.CompilerServices;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;

namespace SharpTS.Runtime.Types;

// Only explicit actual-output capabilities create these views. The canonical
// cache and escaped view retain data/static methods, never an execution host.
internal sealed class VmInputPropertyView
{
    private static readonly ConditionalWeakTable<object, ConditionalWeakTable<Type, VmInputPropertyView>> Views = new();
    internal readonly object Original;
    private readonly VmInputPropertyAccess _access;
    internal readonly int NativeRole;
    private VmInputPropertyView(object original, VmInputPropertyAccess access, int role) { Original = original; _access = access; NativeRole = role; }

    internal static VmInputPropertyView Get(object original, VmInputPropertyAccess access)
    {
        int role = access.GetRole(original);
        if (role is not (1 or 2)) role = 0;
        var domains = Views.GetValue(original, static _ => new());
        var view = domains.GetValue(access.Domain, _ => new(original, access, role));
        if (!view._access.Matches(access) || view.NativeRole != role)
            throw new InvalidOperationException("VM input property capabilities changed for the same output.");
        return view;
    }

    public override string ToString() => Original.ToString()!;

    private sealed class MemberCall(CompiledCallableAdapter callable, VmInputPropertyView receiver) : ISharpTSNonConstructorCallable
    {
        public int Arity() => callable.Arity();
        public object? Call(SharpTS.Execution.Interpreter interpreter, List<object?> arguments) => callable.CallWithThis(receiver, arguments);
    }

    internal static ISharpTSCallable BindMember(CompiledCallableAdapter callable, VmInputPropertyView receiver) => new MemberCall(callable, receiver);

    internal object? Read(string name) => ReadWithHost(_ => _access.Read(Original, name));

    internal object? ReadToStringTag(object? receiver)
    {
        if (_access.ReadToStringTag is not { } readTag) return SharpTSUndefined.Instance;
        return ReadWithHost(host =>
        {
            if (SharpTS.Execution.VmEvaluationLease.CurrentInterpreter is { } owner)
                VmModuleInterpreter.RecordVmBoundaryValue(receiver, owner);
            return readTag(Original, host.FromVm(receiver));
        });
    }

    internal bool CanWrite => _access.Write is not null;

    internal bool Write(string name, object? value)
    {
        var write = _access.Write ?? throw new InvalidOperationException("VM input writes are not configured.");
        var host = ActiveHost();
        if (SharpTS.Execution.VmEvaluationLease.CurrentInterpreter is { } owner)
            VmModuleInterpreter.RecordVmBoundaryValue(value, owner);
        try { return write(Original, name, host.FromVm(value)); }
        catch (Exception exception)
        {
            if (host.TryGetThrownValue(exception, out var thrown))
                throw new Exceptions.ThrowException(host.ToVm(thrown));
            throw;
        }
    }

    private VmExecutionHost ActiveHost()
    {
        var host = VmExecutionHost.Current;
        if (host?.InputPropertyAccess is not { } active || !_access.Matches(active))
            throw new InvalidOperationException("VM input property access requires its active output host.");
        return host;
    }
    private object? ReadWithHost(Func<VmExecutionHost, object?> operation)
    {
        var host = ActiveHost();
        object? value;
        try { value = operation(host); }
        catch (Exception exception)
        {
            if (host.TryGetThrownValue(exception, out var thrown))
                throw new Exceptions.ThrowException(host.ToVm(thrown));
            throw;
        }
        value = host.ToVm(value);
        VmModuleInterpreter.RecordVmInputValue(value);
        return value;
    }
}