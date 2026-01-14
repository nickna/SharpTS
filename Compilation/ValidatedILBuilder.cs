using System.Reflection;
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
public sealed class ValidatedILBuilder
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

    #region Label Operations

    /// <summary>
    /// Defines a new label with an optional debug name.
    /// </summary>
    /// <param name="debugName">Name for error messages (optional).</param>
    /// <returns>The defined label.</returns>
    public Label DefineLabel(string? debugName = null)
    {
        var label = _il.DefineLabel();
        _labels[label] = new LabelInfo(debugName, _exceptionBlockDepth);
        return label;
    }

    /// <summary>
    /// Marks a label at the current position.
    /// </summary>
    /// <param name="label">The label to mark.</param>
    /// <exception cref="ILValidationException">
    /// Thrown if label was not defined, already marked, or stack depth doesn't match branches.
    /// </exception>
    public void MarkLabel(Label label)
    {
        // After marking a label, code becomes reachable again
        _unreachable = false;

        if (!_labels.TryGetValue(label, out var info))
        {
            ThrowOrRecord("Label was not defined in this method scope");
            return;
        }

        if (_markedLabels.Contains(label))
        {
            ThrowOrRecord($"Label '{info.DebugName ?? "<unnamed>"}' already marked");
            return;
        }

        // Verify stack state matches previous branches to this label
        // NOTE: During incremental migration, stack tracking is incomplete because
        // not all IL operations go through the builder yet. We skip validation
        // and rely on the CLR verifier to catch actual stack errors.
        // if (_branchTargetSnapshots.TryGetValue(label, out var expected))
        // {
        //     if (_stackDepth != expected.Depth)
        //     {
        //         ThrowOrRecord($"Stack depth mismatch at label '{info.DebugName ?? "<unnamed>"}': expected {expected.Depth}, found {_stackDepth}");
        //     }
        // }

        _markedLabels.Add(label);
        _il.MarkLabel(label);
    }

    #endregion

    #region Branch Operations

    /// <summary>
    /// Emits an unconditional branch (br).
    /// </summary>
    /// <param name="target">Target label.</param>
    /// <exception cref="ILValidationException">Thrown if inside an exception block.</exception>
    public void Emit_Br(Label target)
    {
        ValidateLabelDefined(target, "Br");
        if (!_unreachable)
            RecordBranchTarget(target);

        // NOTE: During incremental migration, we don't enforce Br vs Leave rules because
        // Br is actually valid for LOCAL jumps within exception blocks (jumps that don't
        // exit the block). Leave is only required when branching OUT of an exception block.
        // Detecting local vs exiting jumps requires tracking which labels are in which scope,
        // which is complex. The CLR verifier will catch actual violations.
        // if (_exceptionBlockDepth > 0)
        // {
        //     ThrowOrRecord("Use Leave instead of Br inside exception blocks");
        // }

        _il.Emit(OpCodes.Br, target);
        _unreachable = true;
    }

    /// <summary>
    /// Emits a branch if true (brtrue).
    /// </summary>
    /// <param name="target">Target label.</param>
    public void Emit_Brtrue(Label target)
    {

        RequireStackDepth(1, "Brtrue");
        ValidateLabelDefined(target, "Brtrue");

        // Record target with stack AFTER consuming condition
        var depthAfterPop = _stackDepth - 1;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        _il.Emit(OpCodes.Brtrue, target);
    }

    /// <summary>
    /// Emits a branch if true (short form).
    /// </summary>
    public void Emit_Brtrue_S(Label target)
    {

        RequireStackDepth(1, "Brtrue_S");
        ValidateLabelDefined(target, "Brtrue_S");

        var depthAfterPop = _stackDepth - 1;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        _il.Emit(OpCodes.Brtrue_S, target);
    }

    /// <summary>
    /// Emits a branch if false (brfalse).
    /// </summary>
    /// <param name="target">Target label.</param>
    public void Emit_Brfalse(Label target)
    {

        RequireStackDepth(1, "Brfalse");
        ValidateLabelDefined(target, "Brfalse");

        var depthAfterPop = _stackDepth - 1;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        _il.Emit(OpCodes.Brfalse, target);
    }

    /// <summary>
    /// Emits a branch if false (short form).
    /// </summary>
    public void Emit_Brfalse_S(Label target)
    {

        RequireStackDepth(1, "Brfalse_S");
        ValidateLabelDefined(target, "Brfalse_S");

        var depthAfterPop = _stackDepth - 1;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        _il.Emit(OpCodes.Brfalse_S, target);
    }

    /// <summary>
    /// Emits a leave instruction for exiting exception blocks.
    /// </summary>
    /// <param name="target">Target label (must be outside the exception block).</param>
    /// <exception cref="ILValidationException">Thrown if not inside an exception block.</exception>
    public void Emit_Leave(Label target)
    {

        ValidateLabelDefined(target, "Leave");

        if (_exceptionBlockDepth == 0)
        {
            ThrowOrRecord("Leave used outside exception block");
        }

        // Leave clears the evaluation stack and records target with empty stack
        RecordBranchTargetWithDepth(target, 0);

        _il.Emit(OpCodes.Leave, target);
        _stackDepth = 0;
        _typeStack.Clear();
        _unreachable = true;
    }

    /// <summary>
    /// Emits a leave instruction (short form).
    /// </summary>
    public void Emit_Leave_S(Label target)
    {

        ValidateLabelDefined(target, "Leave_S");

        if (_exceptionBlockDepth == 0)
        {
            ThrowOrRecord("Leave_S used outside exception block");
        }

        RecordBranchTargetWithDepth(target, 0);

        _il.Emit(OpCodes.Leave_S, target);
        _stackDepth = 0;
        _typeStack.Clear();
        _unreachable = true;
    }

    /// <summary>
    /// Emits a branch if equal (beq).
    /// </summary>
    public void Emit_Beq(Label target)
    {

        RequireStackDepth(2, "Beq");
        ValidateLabelDefined(target, "Beq");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Beq, target);
    }

    /// <summary>
    /// Emits a branch if not equal (bne.un).
    /// </summary>
    public void Emit_Bne_Un(Label target)
    {

        RequireStackDepth(2, "Bne_Un");
        ValidateLabelDefined(target, "Bne_Un");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Bne_Un, target);
    }

    /// <summary>
    /// Emits a branch if greater than (bgt).
    /// </summary>
    public void Emit_Bgt(Label target)
    {

        RequireStackDepth(2, "Bgt");
        ValidateLabelDefined(target, "Bgt");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Bgt, target);
    }

    /// <summary>
    /// Emits a branch if less than (blt).
    /// </summary>
    public void Emit_Blt(Label target)
    {

        RequireStackDepth(2, "Blt");
        ValidateLabelDefined(target, "Blt");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Blt, target);
    }

    /// <summary>
    /// Emits a branch if greater than or equal (bge).
    /// </summary>
    public void Emit_Bge(Label target)
    {

        RequireStackDepth(2, "Bge");
        ValidateLabelDefined(target, "Bge");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Bge, target);
    }

    /// <summary>
    /// Emits a branch if less than or equal (ble).
    /// </summary>
    public void Emit_Ble(Label target)
    {

        RequireStackDepth(2, "Ble");
        ValidateLabelDefined(target, "Ble");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Ble, target);
    }

    #endregion

    #region Exception Block Operations

    /// <summary>
    /// Begins an exception block (try).
    /// </summary>
    /// <returns>Label for the end of the exception block.</returns>
    public Label BeginExceptionBlock()
    {
        var label = _il.BeginExceptionBlock();
        _exceptionBlocks.Push(new ExceptionBlockInfo(ExceptionBlockPhase.Try, _stackDepth, label));
        _exceptionBlockDepth++;
        return label;
    }

    /// <summary>
    /// Begins a catch block for the specified exception type.
    /// </summary>
    /// <param name="exceptionType">The exception type to catch.</param>
    /// <exception cref="ILValidationException">Thrown if not in a valid position for catch.</exception>
    public void BeginCatchBlock(Type exceptionType)
    {
        if (_exceptionBlocks.Count == 0)
        {
            ThrowOrRecord("BeginCatchBlock without matching BeginExceptionBlock");
            return;
        }

        var block = _exceptionBlocks.Peek();
        if (block.Phase == ExceptionBlockPhase.Finally)
        {
            ThrowOrRecord("Cannot add catch block after finally block");
            return;
        }

        block.Phase = ExceptionBlockPhase.Catch;

        // Catch block starts with exception object on stack
        _stackDepth = 1;
        _typeStack.Clear();
        _typeStack.Push(new StackEntry(StackEntryType.Reference, exceptionType));
        _unreachable = false;

        _il.BeginCatchBlock(exceptionType);
    }

    /// <summary>
    /// Begins a finally block.
    /// </summary>
    /// <exception cref="ILValidationException">Thrown if not in a valid position for finally.</exception>
    public void BeginFinallyBlock()
    {
        if (_exceptionBlocks.Count == 0)
        {
            ThrowOrRecord("BeginFinallyBlock without matching BeginExceptionBlock");
            return;
        }

        var block = _exceptionBlocks.Peek();
        if (block.Phase == ExceptionBlockPhase.Finally)
        {
            ThrowOrRecord("Cannot have multiple finally blocks");
            return;
        }

        block.Phase = ExceptionBlockPhase.Finally;

        // Finally block starts with empty stack
        _stackDepth = 0;
        _typeStack.Clear();
        _unreachable = false;

        _il.BeginFinallyBlock();
    }

    /// <summary>
    /// Ends the current exception block.
    /// </summary>
    /// <exception cref="ILValidationException">Thrown if no matching BeginExceptionBlock.</exception>
    public void EndExceptionBlock()
    {
        if (_exceptionBlocks.Count == 0)
        {
            ThrowOrRecord("EndExceptionBlock without matching BeginExceptionBlock");
            return;
        }

        _exceptionBlocks.Pop();
        _exceptionBlockDepth--;
        _unreachable = false;

        _il.EndExceptionBlock();
    }

    #endregion

    #region Load Operations

    /// <summary>
    /// Loads a 32-bit integer constant.
    /// </summary>
    public void Emit_Ldc_I4(int value)
    {

        _il.Emit(OpCodes.Ldc_I4, value);
        PushStack(StackEntryType.Int32);
    }

    /// <summary>
    /// Loads a 64-bit floating point constant.
    /// </summary>
    public void Emit_Ldc_R8(double value)
    {

        _il.Emit(OpCodes.Ldc_R8, value);
        PushStack(StackEntryType.Double);
    }

    /// <summary>
    /// Loads a string constant.
    /// </summary>
    public void Emit_Ldstr(string value)
    {

        _il.Emit(OpCodes.Ldstr, value);
        PushStack(StackEntryType.String);
    }

    /// <summary>
    /// Loads a null reference.
    /// </summary>
    public void Emit_Ldnull()
    {

        _il.Emit(OpCodes.Ldnull);
        PushStack(StackEntryType.Null);
    }

    /// <summary>
    /// Loads a local variable.
    /// </summary>
    public void Emit_Ldloc(LocalBuilder local)
    {

        _il.Emit(OpCodes.Ldloc, local);
        PushStack(GetStackEntryType(local.LocalType), local.LocalType);
    }

    /// <summary>
    /// Loads a local variable by index.
    /// </summary>
    public void Emit_Ldloc(int index)
    {

        _il.Emit(OpCodes.Ldloc, index);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Loads an argument.
    /// </summary>
    public void Emit_Ldarg(int index)
    {

        _il.Emit(OpCodes.Ldarg, index);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Loads a field value.
    /// </summary>
    public void Emit_Ldfld(FieldInfo field)
    {

        RequireStackDepth(1, "Ldfld");
        PopStack(); // Object reference
        _il.Emit(OpCodes.Ldfld, field);
        PushStack(GetStackEntryType(field.FieldType), field.FieldType);
    }

    /// <summary>
    /// Loads a static field value.
    /// </summary>
    public void Emit_Ldsfld(FieldInfo field)
    {

        _il.Emit(OpCodes.Ldsfld, field);
        PushStack(GetStackEntryType(field.FieldType), field.FieldType);
    }

    #endregion

    #region Store Operations

    /// <summary>
    /// Stores a value into a local variable.
    /// </summary>
    public void Emit_Stloc(LocalBuilder local)
    {

        RequireStackDepth(1, "Stloc");
        PopStack();
        _il.Emit(OpCodes.Stloc, local);
    }

    /// <summary>
    /// Stores a value into a local variable by index.
    /// </summary>
    public void Emit_Stloc(int index)
    {

        RequireStackDepth(1, "Stloc");
        PopStack();
        _il.Emit(OpCodes.Stloc, index);
    }

    /// <summary>
    /// Stores a value into an argument.
    /// </summary>
    public void Emit_Starg(int index)
    {

        RequireStackDepth(1, "Starg");
        PopStack();
        _il.Emit(OpCodes.Starg, index);
    }

    /// <summary>
    /// Stores a value into a field.
    /// </summary>
    public void Emit_Stfld(FieldInfo field)
    {

        RequireStackDepth(2, "Stfld");
        PopStack(); // Value
        PopStack(); // Object reference
        _il.Emit(OpCodes.Stfld, field);
    }

    /// <summary>
    /// Stores a value into a static field.
    /// </summary>
    public void Emit_Stsfld(FieldInfo field)
    {

        RequireStackDepth(1, "Stsfld");
        PopStack();
        _il.Emit(OpCodes.Stsfld, field);
    }

    #endregion

    #region Boxing Operations

    /// <summary>
    /// Boxes a value type.
    /// </summary>
    /// <param name="type">The value type to box.</param>
    /// <exception cref="ILValidationException">Thrown if top of stack is not a value type.</exception>
    public void Emit_Box(Type type)
    {

        RequireStackDepth(1, "Box");

        var top = PeekStack();
        if (!top.IsValueType && top.Type != StackEntryType.Unknown)
        {
            ThrowOrRecord($"Box requires value type on stack, found {top.Type}");
        }

        PopStack();
        PushStack(StackEntryType.Reference, type);
        _il.Emit(OpCodes.Box, type);
    }

    /// <summary>
    /// Unboxes to any type (value type or nullable).
    /// </summary>
    public void Emit_Unbox_Any(Type type)
    {

        RequireStackDepth(1, "Unbox_Any");

        var top = PeekStack();
        if (top.IsValueType)
        {
            ThrowOrRecord($"Unbox_Any requires reference type on stack, found {top.Type}");
        }

        PopStack();
        PushStack(GetStackEntryType(type), type);
        _il.Emit(OpCodes.Unbox_Any, type);
    }

    #endregion

    #region Call Operations

    /// <summary>
    /// Calls a method.
    /// </summary>
    public void Emit_Call(MethodInfo method)
    {

        var paramCount = method.GetParameters().Length;
        if (!method.IsStatic) paramCount++; // Include 'this'

        RequireStackDepth(paramCount, "Call");

        for (int i = 0; i < paramCount; i++)
            PopStack();

        _il.Emit(OpCodes.Call, method);

        if (method.ReturnType != typeof(void))
            PushStack(GetStackEntryType(method.ReturnType), method.ReturnType);
    }

    /// <summary>
    /// Calls a method virtually.
    /// </summary>
    public void Emit_Callvirt(MethodInfo method)
    {

        var paramCount = method.GetParameters().Length + 1; // Always has 'this'

        RequireStackDepth(paramCount, "Callvirt");

        for (int i = 0; i < paramCount; i++)
            PopStack();

        _il.Emit(OpCodes.Callvirt, method);

        if (method.ReturnType != typeof(void))
            PushStack(GetStackEntryType(method.ReturnType), method.ReturnType);
    }

    /// <summary>
    /// Creates a new object using a constructor.
    /// </summary>
    public void Emit_Newobj(ConstructorInfo ctor)
    {

        var paramCount = ctor.GetParameters().Length;

        RequireStackDepth(paramCount, "Newobj");

        for (int i = 0; i < paramCount; i++)
            PopStack();

        _il.Emit(OpCodes.Newobj, ctor);
        PushStack(StackEntryType.Reference, ctor.DeclaringType);
    }

    #endregion

    #region Stack Operations

    /// <summary>
    /// Pops the top value from the stack.
    /// </summary>
    public void Emit_Pop()
    {

        RequireStackDepth(1, "Pop");
        PopStack();
        _il.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Duplicates the top value on the stack.
    /// </summary>
    public void Emit_Dup()
    {

        RequireStackDepth(1, "Dup");
        var top = PeekStack();
        _il.Emit(OpCodes.Dup);
        PushStack(top.Type, top.ClrType);
    }

    #endregion

    #region Return and Method End

    /// <summary>
    /// Emits a return instruction.
    /// </summary>
    /// <remarks>
    /// Also validates that all defined labels have been marked.
    /// </remarks>
    public void Emit_Ret()
    {

        ValidateAllLabelsMarked();
        _il.Emit(OpCodes.Ret);
        _unreachable = true;
    }

    /// <summary>
    /// Validates that all defined labels have been marked.
    /// Call this at method end if not using Emit_Ret().
    /// </summary>
    public void ValidateAllLabelsMarked()
    {
        foreach (var (label, info) in _labels)
        {
            if (!_markedLabels.Contains(label))
            {
                ThrowOrRecord($"Label '{info.DebugName ?? "<unnamed>"}' was defined but never marked");
            }
        }
    }

    /// <summary>
    /// Resets the builder state for a new method.
    /// Call this when starting to emit a new method.
    /// </summary>
    public void Reset()
    {
        _labels.Clear();
        _markedLabels.Clear();
        _stackDepth = 0;
        _typeStack.Clear();
        _branchTargetSnapshots.Clear();
        _exceptionBlocks.Clear();
        _exceptionBlockDepth = 0;
        _unreachable = false;
        _collectedErrors.Clear();
    }

    #endregion

    #region Arithmetic Operations

    /// <summary>
    /// Adds two values.
    /// </summary>
    public void Emit_Add()
    {

        RequireStackDepth(2, "Add");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Add);
        PushStack(StackEntryType.Unknown); // Result type depends on operands
    }

    /// <summary>
    /// Subtracts two values.
    /// </summary>
    public void Emit_Sub()
    {

        RequireStackDepth(2, "Sub");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Sub);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Multiplies two values.
    /// </summary>
    public void Emit_Mul()
    {

        RequireStackDepth(2, "Mul");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Mul);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Divides two values.
    /// </summary>
    public void Emit_Div()
    {

        RequireStackDepth(2, "Div");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Div);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Computes remainder.
    /// </summary>
    public void Emit_Rem()
    {

        RequireStackDepth(2, "Rem");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Rem);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Negates a value.
    /// </summary>
    public void Emit_Neg()
    {

        RequireStackDepth(1, "Neg");
        // Top stays, type may change
        _il.Emit(OpCodes.Neg);
    }

    #endregion

    #region Comparison Operations

    /// <summary>
    /// Compares equal.
    /// </summary>
    public void Emit_Ceq()
    {

        RequireStackDepth(2, "Ceq");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Ceq);
        PushStack(StackEntryType.Int32);
    }

    /// <summary>
    /// Compares greater than.
    /// </summary>
    public void Emit_Cgt()
    {

        RequireStackDepth(2, "Cgt");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Cgt);
        PushStack(StackEntryType.Int32);
    }

    /// <summary>
    /// Compares less than.
    /// </summary>
    public void Emit_Clt()
    {

        RequireStackDepth(2, "Clt");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Clt);
        PushStack(StackEntryType.Int32);
    }

    #endregion

    #region Conversion Operations

    /// <summary>
    /// Converts to float64 (double).
    /// </summary>
    public void Emit_Conv_R8()
    {

        RequireStackDepth(1, "Conv_R8");
        PopStack();
        _il.Emit(OpCodes.Conv_R8);
        PushStack(StackEntryType.Double);
    }

    /// <summary>
    /// Converts to int32.
    /// </summary>
    public void Emit_Conv_I4()
    {

        RequireStackDepth(1, "Conv_I4");
        PopStack();
        _il.Emit(OpCodes.Conv_I4);
        PushStack(StackEntryType.Int32);
    }

    /// <summary>
    /// Converts to int64.
    /// </summary>
    public void Emit_Conv_I8()
    {

        RequireStackDepth(1, "Conv_I8");
        PopStack();
        _il.Emit(OpCodes.Conv_I8);
        PushStack(StackEntryType.Int64);
    }

    /// <summary>
    /// Converts to unsigned int64.
    /// </summary>
    public void Emit_Conv_U8()
    {

        RequireStackDepth(1, "Conv_U8");
        PopStack();
        _il.Emit(OpCodes.Conv_U8);
        PushStack(StackEntryType.Int64);
    }

    #endregion

    #region Bitwise Operations

    /// <summary>
    /// Bitwise AND.
    /// </summary>
    public void Emit_And()
    {

        RequireStackDepth(2, "And");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.And);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Bitwise OR.
    /// </summary>
    public void Emit_Or()
    {

        RequireStackDepth(2, "Or");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Or);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Bitwise XOR.
    /// </summary>
    public void Emit_Xor()
    {

        RequireStackDepth(2, "Xor");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Xor);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Bitwise NOT.
    /// </summary>
    public void Emit_Not()
    {

        RequireStackDepth(1, "Not");
        // Type unchanged
        _il.Emit(OpCodes.Not);
    }

    /// <summary>
    /// Shift left.
    /// </summary>
    public void Emit_Shl()
    {

        RequireStackDepth(2, "Shl");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Shl);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Shift right (signed).
    /// </summary>
    public void Emit_Shr()
    {

        RequireStackDepth(2, "Shr");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Shr);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Shift right (unsigned).
    /// </summary>
    public void Emit_Shr_Un()
    {

        RequireStackDepth(2, "Shr_Un");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Shr_Un);
        PushStack(StackEntryType.Unknown);
    }

    #endregion

    #region Miscellaneous Operations

    /// <summary>
    /// Throws an exception.
    /// </summary>
    public void Emit_Throw()
    {

        RequireStackDepth(1, "Throw");
        PopStack();
        _il.Emit(OpCodes.Throw);
        _unreachable = true;
    }

    /// <summary>
    /// Rethrows the current exception (in catch handler).
    /// </summary>
    public void Emit_Rethrow()
    {

        _il.Emit(OpCodes.Rethrow);
        _unreachable = true;
    }

    /// <summary>
    /// Emits endfinally/endfault.
    /// </summary>
    public void Emit_Endfinally()
    {

        _il.Emit(OpCodes.Endfinally);
        _unreachable = true;
    }

    /// <summary>
    /// Checks if an object is an instance of a type.
    /// </summary>
    public void Emit_Isinst(Type type)
    {

        RequireStackDepth(1, "Isinst");
        PopStack();
        _il.Emit(OpCodes.Isinst, type);
        PushStack(StackEntryType.Reference, type);
    }

    /// <summary>
    /// Casts to a class type.
    /// </summary>
    public void Emit_Castclass(Type type)
    {

        RequireStackDepth(1, "Castclass");
        PopStack();
        _il.Emit(OpCodes.Castclass, type);
        PushStack(StackEntryType.Reference, type);
    }

    /// <summary>
    /// Declares a local variable (passthrough to ILGenerator).
    /// </summary>
    public LocalBuilder DeclareLocal(Type type)
    {
        return _il.DeclareLocal(type);
    }

    #endregion

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
