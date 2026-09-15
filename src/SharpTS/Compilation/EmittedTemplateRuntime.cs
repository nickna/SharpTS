using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required template-literal declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedTemplateRuntime
{
    internal EmittedTemplateRuntime() { }

    public bool IsComplete { get; private set; }

    // The created strings-list type precedes the runtime's concatenation and invocation bodies.
    private Type? _stringsListType;
    public Type StringsListType
    {
        get => Require(_stringsListType);
        internal set => Set(ref _stringsListType, value);
    }

    private ConstructorInfo? _stringsListCtor;
    public ConstructorInfo StringsListCtor
    {
        get => Require(_stringsListCtor);
        internal set => Set(ref _stringsListCtor, value);
    }

    private MethodInfo? _rawGetter;
    public MethodInfo RawGetter
    {
        get => Require(_rawGetter);
        internal set => Set(ref _rawGetter, value);
    }

    private MethodBuilder? _concat;
    public MethodBuilder Concat
    {
        get => Require(_concat);
        internal set => Set(ref _concat, value);
    }

    private MethodBuilder? _invoke;
    public MethodBuilder Invoke
    {
        get => Require(_invoke);
        internal set => Set(ref _invoke, value);
    }

    private MethodBuilder? _invokeWithThis;
    public MethodBuilder InvokeWithThis
    {
        get => Require(_invokeWithThis);
        internal set => Set(ref _invokeWithThis, value);
    }

    private MethodBuilder? _raw;
    public MethodBuilder Raw
    {
        get => Require(_raw);
        internal set => Set(ref _raw, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"template metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("template metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = StringsListType;
        _ = StringsListCtor;
        _ = RawGetter;
        _ = Concat;
        _ = Invoke;
        _ = InvokeWithThis;
        _ = Raw;
        IsComplete = true;
    }
}
