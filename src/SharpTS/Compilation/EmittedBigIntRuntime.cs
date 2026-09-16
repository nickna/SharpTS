using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required BigInt declarations for one compilation.</summary>
public sealed class EmittedBigIntRuntime
{
    internal EmittedBigIntRuntime() { }

    public bool IsComplete { get; private set; }

    private FieldBuilder? _prototypeField;
    /// <summary>BigInt.prototype singleton used by value-position BigInt and primitive BigInteger symbol lookup.</summary>
    public FieldBuilder PrototypeField
    {
        get => Require(_prototypeField);
        internal set => Set(ref _prototypeField, value);
    }

    private MethodBuilder? _prototypePopulateMethod;
    /// <summary>Populates <see cref="PrototypeField"/> with $TSFunction wrappers; idempotent.</summary>
    public MethodBuilder PrototypePopulateMethod
    {
        get => Require(_prototypePopulateMethod);
        internal set => Set(ref _prototypePopulateMethod, value);
    }

    private MethodBuilder? _toNumber;
    /// <summary>$Runtime.BigIntToNumber(BigInteger) -> double — ECMA-262 NumberFromBigInt with ties-to-even binary64 rounding.</summary>
    public MethodBuilder ToNumber
    {
        get => Require(_toNumber);
        internal set => Set(ref _toNumber, value);
    }

    public EmittedBigIntImplementation? Implementation { get; private set; }

    public EmittedBigIntImplementation RequireImplementation() => Implementation
        ?? throw new InvalidOperationException("BigInt implementation was not enabled for this compilation.");

    internal void BeginImplementationEmission()
    {
        EnsureMutable();
        if (Implementation is not null)
            throw new InvalidOperationException("BigInt implementation emission has already started.");
        Implementation = new EmittedBigIntImplementation();
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Required BigInt metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Required BigInt metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = PrototypeField;
        _ = PrototypePopulateMethod;
        _ = ToNumber;
        Implementation?.CompleteEmission();
        IsComplete = true;
    }
}
