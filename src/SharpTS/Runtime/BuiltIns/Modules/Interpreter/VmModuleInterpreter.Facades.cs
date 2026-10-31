using System.Runtime.CompilerServices;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

public static partial class VmModuleInterpreter
{
    private sealed record VmFacadeReceipt(VmExecutionHost Host, string Name, int Length);
    private static ConditionalWeakTable<object, VmFacadeReceipt>? _vmFacadeCallables;

    // Called only by the literal VM export/Script/compileFunction facade producers.
    // The facade stores its actual creator host, not an evaluator or arbitrary CLR callable.
    internal static void RecordVmFacadeCallable(object value, VmExecutionHost? host)
    {
        if (host is null || host.IsDisposed || value is not
            (BuiltInMethod or BuiltInAsyncMethod or VmScriptConstructor
                or VmSourceTextModuleConstructor or VmSyntheticModuleConstructor)) return;
        var metadata = value switch
        {
            BuiltInMethod method => (method.FunctionName, method.SpecLength),
            BuiltInAsyncMethod method => (method.FunctionName, method.Arity()),
            VmScriptConstructor => ("Script", 1),
            VmSourceTextModuleConstructor => ("SourceTextModule", 1),
            VmSyntheticModuleConstructor => ("SyntheticModule", 2),
            _ => throw new InvalidOperationException("Unrecognized literal VM facade producer.")
        };
        LazyInitializer.EnsureInitialized(ref _vmFacadeCallables).GetValue(value, _ => new(host, metadata.Item1, metadata.Item2));
    }

    private static bool IsVmFacadeCallable(object? value) => value is not null
        && _vmFacadeCallables?.TryGetValue(value, out _) == true;

    /// <summary>Reads only the immutable native facade producer's name/length snapshot.</summary>
    public static object?[] GetVmFacadeCallableMetadata(object value)
        => _vmFacadeCallables?.TryGetValue(value, out var receipt) == true && !receipt.Host.IsDisposed
            ? [receipt.Name, (double)receipt.Length]
            : throw new ArgumentException("Expected a live native VM facade callable.", nameof(value));

    private static object? InvokeVmFacadeCallable(object value, object?[] arguments)
    {
        if (_vmFacadeCallables is not { } table || !table.TryGetValue(value, out var receipt) || receipt.Host.IsDisposed)
            throw new ArgumentException("Expected a live VM facade callable.", nameof(value));
        var host = receipt.Host;
        using var scope = host.Enter();
        var args = arguments.Select(argument => VmContext.WrapVmCompiledCallable(host.ToVm(argument))).ToList();
        var result = ((ISharpTSCallable)value).Call(null!, args);
        if (result is SharpTSPromise promise) RecordVmPromise(promise, null);
        return host.FromVm(result);
    }

    internal static object FinishVmFacadeObject(Dictionary<string, object?> facade)
    {
        if (VmExecutionHost.Current is not { } host) return facade;
        foreach (var name in facade.Keys.ToArray())
        {
            var value = facade[name];
            if (value is not ISharpTSCallable) continue;
            RecordVmFacadeCallable(value, host);
            facade[name] = host.FromVm(value);
        }
        return facade;
    }
}
