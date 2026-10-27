using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Opt-in source factories carrying invocation targets separately from guest method identity.</summary>
public sealed class EmittedNativeSourceFunctionRuntime
{
    internal EmittedNativeSourceFunctionRuntime() { }
    private ConstructorBuilder? _constructor;
    private ConstructorBuilder? _cachedConstructor;
    private MethodBuilder? _getOrCreate;
    public bool IsComplete { get; private set; }
    public ConstructorBuilder Constructor { get => Require(_constructor); internal set => Set(ref _constructor, value); }
    public ConstructorBuilder CachedConstructor { get => Require(_cachedConstructor); internal set => Set(ref _cachedConstructor, value); }
    public MethodBuilder GetOrCreate { get => Require(_getOrCreate); internal set => Set(ref _getOrCreate, value); }

    private static T Require<T>(T? value, [CallerMemberName] string name = "") where T : class =>
        value ?? throw new InvalidOperationException("Native source function metadata '" + name + "' is unavailable.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        if (IsComplete) throw new InvalidOperationException("Native source function metadata is complete.");
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null) throw new InvalidOperationException("Native source function metadata is already declared.");
        field = value;
    }

    internal void CompleteEmission()
    {
        if (IsComplete) throw new InvalidOperationException("Native source function metadata is already complete.");
        _ = Constructor;
        _ = CachedConstructor;
        _ = GetOrCreate;
        IsComplete = true;
    }
}
