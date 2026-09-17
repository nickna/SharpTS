using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required argument-context and later branded argument-object declarations.</summary>
public sealed class EmittedArgumentsRuntime
{
    internal EmittedArgumentsRuntime() { }
    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private ConstructorBuilder? _defaultCtor;
    public ConstructorBuilder DefaultCtor
    {
        get => Require(_defaultCtor);
        internal set => SetHandle(ref _defaultCtor, value);
    }

    private ConstructorBuilder? _enumerableCtor;
    public ConstructorBuilder EnumerableCtor
    {
        get => Require(_enumerableCtor);
        internal set => SetHandle(ref _enumerableCtor, value);
    }

    private FieldBuilder? _lengthField;
    /// <summary>$Arguments._length — JS-visible length (per ECMA-262 sloppy
    /// arguments, "length" doesn't auto-update on out-of-range indexed sets).</summary>
    public FieldBuilder LengthField
    {
        get => Require(_lengthField);
        internal set => SetHandle(ref _lengthField, value);
    }

    private FieldBuilder? _currentField;
    /// <summary>
    /// Thread-static field holding the current call's full argument array (pre-AdjustArgs).
    /// Set by <c>$TSFunction.Invoke</c>/<c>InvokeWithThis</c> around MethodInfo.Invoke so
    /// that a flagged function body can reconstruct the JS <c>arguments</c> object with
    /// every caller value — including extras the fixed method signature would otherwise
    /// drop (the lodash <c>overRest</c> pattern that motivates #64). Read once in the
    /// function prologue; null means "fall back to declared parameters" (the direct-call
    /// fast path in compiled code where arity is exact).
    /// </summary>
    public FieldBuilder CurrentField
    {
        get => Require(_currentField);
        internal set => SetHandle(ref _currentField, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Arguments metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Arguments metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Arguments metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = DefaultCtor;
        _ = EnumerableCtor;
        _ = LengthField;
        _ = CurrentField;
        IsComplete = true;
    }
}
