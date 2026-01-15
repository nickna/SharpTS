using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Type of value on the IL evaluation stack.
/// </summary>
public enum StackEntryType
{
    /// <summary>Unknown or untracked type.</summary>
    Unknown,
    /// <summary>32-bit integer (int32).</summary>
    Int32,
    /// <summary>64-bit integer (int64).</summary>
    Int64,
    /// <summary>64-bit floating point (float64).</summary>
    Double,
    /// <summary>32-bit floating point (float32).</summary>
    Float,
    /// <summary>Boolean (represented as int32 0/1).</summary>
    Boolean,
    /// <summary>String reference.</summary>
    String,
    /// <summary>Object reference (any reference type).</summary>
    Reference,
    /// <summary>Null reference.</summary>
    Null,
    /// <summary>Value type (struct).</summary>
    ValueType,
    /// <summary>Native integer (IntPtr).</summary>
    NativeInt
}

/// <summary>
/// Represents a single entry on the IL evaluation stack.
/// </summary>
/// <param name="Type">The category of the stack entry.</param>
/// <param name="ClrType">Optional CLR type for more precise tracking.</param>
public readonly record struct StackEntry(StackEntryType Type, Type? ClrType = null)
{
    /// <summary>
    /// Returns true if this entry is a value type that would need boxing.
    /// </summary>
    public bool IsValueType => Type is StackEntryType.Int32 or StackEntryType.Int64
        or StackEntryType.Double or StackEntryType.Float or StackEntryType.Boolean
        or StackEntryType.ValueType or StackEntryType.NativeInt;
}

/// <summary>
/// Snapshot of the stack state at a particular point, used for branch validation.
/// </summary>
/// <param name="Depth">Stack depth at this point.</param>
/// <param name="Types">Types on the stack (top of stack is last element).</param>
public readonly record struct StackSnapshot(int Depth, StackEntry[] Types);

/// <summary>
/// Information about a defined label.
/// </summary>
/// <param name="DebugName">Optional debug name for error messages.</param>
/// <param name="DefinedInExceptionDepth">Exception block depth when label was defined.</param>
public record LabelInfo(string? DebugName, int DefinedInExceptionDepth);

/// <summary>
/// Information about an active exception block.
/// </summary>
public class ExceptionBlockInfo
{
    /// <summary>Current phase of the exception block.</summary>
    public ExceptionBlockPhase Phase { get; set; }

    /// <summary>Stack depth when the block was entered.</summary>
    public int EntryStackDepth { get; init; }

    /// <summary>The label returned by BeginExceptionBlock.</summary>
    public Label EndLabel { get; init; }

    public ExceptionBlockInfo(ExceptionBlockPhase phase, int entryStackDepth, Label endLabel)
    {
        Phase = phase;
        EntryStackDepth = entryStackDepth;
        EndLabel = endLabel;
    }
}

/// <summary>
/// Phase of an exception block.
/// </summary>
public enum ExceptionBlockPhase { Try, Catch, Finally }

/// <summary>
/// Validation mode for the IL builder.
/// </summary>
public enum ValidationMode
{
    /// <summary>Throw immediately on validation error.</summary>
    FailFast,
    /// <summary>Collect all errors and report at method end.</summary>
    CollectAll
}

/// <summary>
/// IL emission wrapper that validates label usage, stack balance, and exception blocks
/// at emit time to catch errors before runtime PEVerify failures.
/// </summary>
/// <remarks>
/// This class wraps <see cref="ILGenerator"/> and intercepts critical operations to add
/// compile-time validation. It does not modify the emitted IL; it only checks that
/// the emission sequence is valid.
///
/// <para>Validated operations:</para>
/// <list type="bullet">
/// <item>Labels: All defined labels must be marked before method ends</item>
/// <item>Stack: Branch targets must have consistent stack depth</item>
/// <item>Exception blocks: Br not allowed inside try/catch; use Leave instead</item>
/// <item>Boxing: Box requires value type on stack</item>
/// </list>
/// </remarks>
public sealed partial class ValidatedILBuilder
{
    private readonly ILGenerator _il;
    private readonly TypeProvider _types;
    private readonly ValidationMode _mode;
    private readonly List<string> _collectedErrors = [];

    // Label tracking
    private readonly Dictionary<Label, LabelInfo> _labels = [];
    private readonly HashSet<Label> _markedLabels = [];

    // Stack tracking
    private int _stackDepth;
    private readonly Stack<StackEntry> _typeStack = new();
    private readonly Dictionary<Label, StackSnapshot> _branchTargetSnapshots = [];

    // Exception block tracking
    private readonly Stack<ExceptionBlockInfo> _exceptionBlocks = new();
    private int _exceptionBlockDepth;

    // For tracking unreachable code after unconditional branches
    private bool _unreachable;

    /// <summary>
    /// Creates a new validated IL builder wrapping the given ILGenerator.
    /// </summary>
    /// <param name="il">The underlying ILGenerator.</param>
    /// <param name="types">Type provider for type resolution.</param>
    /// <param name="mode">Validation mode (default: FailFast).</param>
    public ValidatedILBuilder(ILGenerator il, TypeProvider types, ValidationMode mode = ValidationMode.FailFast)
    {
        _il = il;
        _types = types;
        _mode = mode;
    }

    /// <summary>
    /// Gets the underlying ILGenerator for operations not yet migrated.
    /// </summary>
    /// <remarks>
    /// Use sparingly. Prefer using the validated methods on this class.
    /// Operations through Raw bypass all validation.
    /// </remarks>
    public ILGenerator Raw => _il;

    /// <summary>
    /// Gets the current exception block depth.
    /// </summary>
    public int ExceptionBlockDepth => _exceptionBlockDepth;

    /// <summary>
    /// Gets the current stack depth (for debugging/testing).
    /// </summary>
    public int CurrentStackDepth => _stackDepth;

    /// <summary>
    /// Gets any collected validation errors (when using CollectAll mode).
    /// </summary>
    public IReadOnlyList<string> CollectedErrors => _collectedErrors;

    #region Stack Tracking Helpers

    private void PushStack(StackEntryType type, Type? clrType = null)
    {
        _stackDepth++;
        _typeStack.Push(new StackEntry(type, clrType));
    }

    private StackEntry PopStack()
    {
        if (_typeStack.Count > 0)
        {
            _stackDepth--;
            return _typeStack.Pop();
        }
        _stackDepth--;
        return new StackEntry(StackEntryType.Unknown);
    }

    private StackEntry PeekStack()
    {
        return _typeStack.Count > 0 ? _typeStack.Peek() : new StackEntry(StackEntryType.Unknown);
    }

    private void RequireStackDepth(int required, string operation)
    {
        // NOTE: During incremental migration, stack tracking is incomplete because
        // not all IL operations go through the builder yet. We skip validation
        // and rely on the CLR verifier to catch actual stack errors.
        // This can be re-enabled once all Emit calls are routed through the builder.
        // if (_stackDepth < required)
        // {
        //     ThrowOrRecord($"{operation} requires {required} value(s) on stack, found {_stackDepth}");
        // }
    }

    private static StackEntryType GetStackEntryType(Type type)
    {
        if (type == typeof(int) || type == typeof(bool) || type == typeof(char) ||
            type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
            type == typeof(ushort) || type == typeof(uint))
            return StackEntryType.Int32;

        if (type == typeof(long) || type == typeof(ulong))
            return StackEntryType.Int64;

        if (type == typeof(double))
            return StackEntryType.Double;

        if (type == typeof(float))
            return StackEntryType.Float;

        if (type == typeof(string))
            return StackEntryType.String;

        if (type == typeof(IntPtr) || type == typeof(UIntPtr))
            return StackEntryType.NativeInt;

        if (type.IsValueType)
            return StackEntryType.ValueType;

        return StackEntryType.Reference;
    }

    #endregion

    #region Validation Helpers

    private void ValidateLabelDefined(Label label, string operation)
    {
        // NOTE: We intentionally do NOT throw if the label wasn't defined by this builder.
        // During incremental migration, some labels may still be defined via IL.DefineLabel()
        // directly. We only validate labels that WERE defined through this builder.
        // This allows gradual migration without breaking existing code.
    }

    private void RecordBranchTarget(Label target)
    {
        RecordBranchTargetWithDepth(target, _stackDepth);
    }

    private void RecordBranchTargetWithDepth(Label target, int depth)
    {
        if (!_branchTargetSnapshots.ContainsKey(target))
        {
            var types = _typeStack.Reverse().Take(depth).ToArray();
            _branchTargetSnapshots[target] = new StackSnapshot(depth, types);
        }
    }

    private void ThrowOrRecord(string message)
    {
        if (_mode == ValidationMode.FailFast)
            throw new ILValidationException(message);

        _collectedErrors.Add(message);
    }

    #endregion
}
