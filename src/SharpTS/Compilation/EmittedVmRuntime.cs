using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional VM declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedVmRuntime
{
    internal EmittedVmRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _runInNewContext;
    public MethodBuilder RunInNewContext
    {
        get => Require(_runInNewContext);
        internal set => Set(ref _runInNewContext, value);
    }

    private MethodBuilder? _runInThisContext;
    public MethodBuilder RunInThisContext
    {
        get => Require(_runInThisContext);
        internal set => Set(ref _runInThisContext, value);
    }

    private MethodBuilder? _runInContext;
    public MethodBuilder RunInContext
    {
        get => Require(_runInContext);
        internal set => Set(ref _runInContext, value);
    }

    private MethodBuilder? _createContext;
    public MethodBuilder CreateContext
    {
        get => Require(_createContext);
        internal set => Set(ref _createContext, value);
    }

    private MethodBuilder? _isContext;
    public MethodBuilder IsContext
    {
        get => Require(_isContext);
        internal set => Set(ref _isContext, value);
    }

    private MethodBuilder? _getScriptConstructor;
    public MethodBuilder GetScriptConstructor
    {
        get => Require(_getScriptConstructor);
        internal set => Set(ref _getScriptConstructor, value);
    }

    private MethodBuilder? _newScript;
    public MethodBuilder NewScript
    {
        get => Require(_newScript);
        internal set => Set(ref _newScript, value);
    }

    private MethodBuilder? _compileFunction;
    public MethodBuilder CompileFunction
    {
        get => Require(_compileFunction);
        internal set => Set(ref _compileFunction, value);
    }

    private MethodBuilder? _getConstants;
    public MethodBuilder GetConstants
    {
        get => Require(_getConstants);
        internal set => Set(ref _getConstants, value);
    }

    private MethodBuilder? _measureMemory;
    public MethodBuilder MeasureMemory
    {
        get => Require(_measureMemory);
        internal set => Set(ref _measureMemory, value);
    }

    private MethodBuilder? _newSourceTextModule;
    public MethodBuilder NewSourceTextModule
    {
        get => Require(_newSourceTextModule);
        internal set => Set(ref _newSourceTextModule, value);
    }

    private MethodBuilder? _newSyntheticModule;
    public MethodBuilder NewSyntheticModule
    {
        get => Require(_newSyntheticModule);
        internal set => Set(ref _newSyntheticModule, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"VM metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("VM metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = RunInNewContext;
        _ = RunInThisContext;
        _ = RunInContext;
        _ = CreateContext;
        _ = IsContext;
        _ = GetScriptConstructor;
        _ = NewScript;
        _ = CompileFunction;
        _ = GetConstants;
        _ = MeasureMemory;
        _ = NewSourceTextModule;
        _ = NewSyntheticModule;
        IsComplete = true;
    }
}
