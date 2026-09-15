using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Dynamic-import declarations for one compilation, checked during emission and frozen at completion.</summary>
public sealed class EmittedDynamicImportRuntime
{
    internal EmittedDynamicImportRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _importModule;
    public MethodBuilder ImportModule
    {
        get => Require(_importModule);
        internal set => Set(ref _importModule, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Dynamic-import metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Dynamic-import metadata emission is already complete.");
    }

    internal void ValidateDeclarations()
    {
        _ = ImportModule;
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        ValidateDeclarations();
        IsComplete = true;
    }
}
