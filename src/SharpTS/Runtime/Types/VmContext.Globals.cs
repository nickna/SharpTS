using System.Runtime.CompilerServices;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;

namespace SharpTS.Runtime.Types;

public static partial class VmContext
{
    // Actual input callback values retain their existing typed/legacy wrapping
    // boundary. Weak keys keep these adapters out of persistent context data.
    private static ConditionalWeakTable<object, CompiledCallableAdapter>? _unhostedContextCallbacks;
    private static ConditionalWeakTable<object, ConditionalWeakTable<VmExecutionHost, CompiledCallableAdapter>>? _hostedContextCallbacks;

    internal static object? ReadContextValue(object? value)
    {
        VmModuleInterpreter.RecordVmInputValue(value);
        var host = VmExecutionHost.Current;
        value = host is null ? value : host.ToVm(value);
        VmModuleInterpreter.RecordVmInputValue(value);
        if (value is null or string or bool or double or SharpTSUndefined or SharpTSBigInt or SharpTSSymbol)
            return value;
        if (WrapCompiledCallable(value) is not CompiledCallableAdapter candidate) return value;
        if (host is null)
            return LazyInitializer.EnsureInitialized(ref _unhostedContextCallbacks).GetValue(value!, _ => candidate);
        var owners = LazyInitializer.EnsureInitialized(ref _hostedContextCallbacks)
            .GetValue(value!, _ => new ConditionalWeakTable<VmExecutionHost, CompiledCallableAdapter>());
        return owners.GetValue(host, _ => candidate);
    }

    internal static bool ApplyGlobalObjectState(object? context, Execution.Interpreter interpreter)
    {
        if (context is null || !VmContextGlobalState.Supports(context)) return false;
        var marker = _contexts.GetOrCreateValue(context);
        lock (marker)
        {
            marker.Globals ??= new VmContextGlobalState(context, interpreter.CaptureVmGlobalIntrinsics());
            interpreter.UseVmGlobalObject(marker.Globals.GlobalObject);
        }
        return true;
    }
}
