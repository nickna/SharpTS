using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Module declarations for one compilation, checked during emission and frozen at completion.</summary>
public sealed class EmittedModuleRuntime
{
    internal EmittedModuleRuntime() { }

    public bool IsComplete { get; private set; }

    private FieldBuilder? _registry;
    public FieldBuilder Registry
    {
        get => Require(_registry);
        internal set => Set(ref _registry, value);
    }

    private MethodBuilder? _initialize;
    public MethodBuilder Initialize
    {
        get => Require(_initialize);
        internal set => Set(ref _initialize, value);
    }

    private MethodBuilder? _register;
    public MethodBuilder Register
    {
        get => Require(_register);
        internal set => Set(ref _register, value);
    }

    public EmittedCommonJsRuntime? CommonJs { get; private set; }

    public EmittedCommonJsRuntime RequireCommonJs() => CommonJs
        ?? throw new InvalidOperationException("CommonJs module metadata was not enabled for this compilation.");

    internal void BeginCommonJsEmission()
    {
        EnsureMutable();
        if (CommonJs is not null)
            throw new InvalidOperationException("CommonJs module metadata emission has already started.");
        CommonJs = new EmittedCommonJsRuntime();
    }

    public EmittedDynamicImportRuntime? DynamicImport { get; private set; }

    public EmittedDynamicImportRuntime RequireDynamicImport() => DynamicImport
        ?? throw new InvalidOperationException("DynamicImport module metadata was not enabled for this compilation.");

    internal void BeginDynamicImportEmission()
    {
        EnsureMutable();
        if (DynamicImport is not null)
            throw new InvalidOperationException("DynamicImport module metadata emission has already started.");
        DynamicImport = new EmittedDynamicImportRuntime();
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Module metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Module metadata emission is already complete.");
    }

    internal void ValidateDeclarations()
    {
        _ = Registry;
        _ = Initialize;
        _ = Register;
        CommonJs?.ValidateDeclarations();
        DynamicImport?.ValidateDeclarations();
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        ValidateDeclarations();
        // Validate both optional groups before freezing either, so completion remains retryable.
        if (CommonJs is { IsComplete: false }) CommonJs.CompleteEmission();
        if (DynamicImport is { IsComplete: false }) DynamicImport.CompleteEmission();
        IsComplete = true;
    }
}
