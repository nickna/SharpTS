using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Abstraction over IL emitters that allows type emitter strategies and call handlers
/// to work with all emitter types: ILEmitter (sync), AsyncMoveNextEmitter,
/// GeneratorMoveNextEmitter, AsyncArrowMoveNextEmitter, and AsyncGeneratorMoveNextEmitter.
/// </summary>
public interface IEmitterContext
{
    /// <summary>
    /// Gets the compilation context containing types, runtime methods, and other compilation state.
    /// </summary>
    CompilationContext Context { get; }

    /// <summary>
    /// Gets the current ILGenerator for emitting IL instructions.
    /// </summary>
    ILGenerator IL { get; }

    /// <summary>
    /// Emits IL for an expression, leaving the result on the evaluation stack.
    /// </summary>
    /// <param name="expr">The expression to emit.</param>
    void EmitExpression(Expr expr);

    /// <summary>
    /// Boxes the value on the stack if the expression type requires it.
    /// Value types (numbers, booleans) need boxing to become object references.
    /// </summary>
    /// <param name="expr">The expression whose result may need boxing.</param>
    void EmitBoxIfNeeded(Expr expr);

    /// <summary>
    /// Ensures the value on the stack is boxed (object reference).
    /// Unlike EmitBoxIfNeeded, this does not take an expression — it boxes based on current stack type.
    /// </summary>
    void EnsureBoxed();

    /// <summary>
    /// Emits an expression and ensures the result is an unboxed double on the stack.
    /// Used when a numeric value is required (e.g., Date setter arguments).
    /// </summary>
    /// <param name="expr">The expression to emit as a double.</param>
    void EmitExpressionAsDouble(Expr expr);

    /// <summary>
    /// Marks the stack as containing an unknown/object type.
    /// </summary>
    void SetStackUnknown();

    /// <summary>
    /// Marks the stack as containing a specific type.
    /// </summary>
    /// <param name="type">The stack type.</param>
    void SetStackType(StackType type);

    /// <summary>
    /// Attempts to emit a console method call (log, error, warn, info, debug, clear, time, timeEnd, timeLog, etc.).
    /// </summary>
    bool TryEmitConsoleMethod(Expr.Call call);

    /// <summary>
    /// Emits IL for the global fetch(url, options?) call.
    /// </summary>
    void EmitFetchCall(List<Expr> arguments);

    /// <summary>
    /// Evaluates <paramref name="args"/> and leaves a single <c>object[]</c> of the boxed argument
    /// values on the IL stack. No spread expansion — use <see cref="EmitArgsArrayWithSpread"/> when
    /// arguments may contain <see cref="Expr.Spread"/>. When <paramref name="argLocals"/> is supplied
    /// (await-safe pre-spilled args, #850) elements are loaded from those locals instead of
    /// re-evaluated.
    /// </summary>
    void EmitArgsArray(IReadOnlyList<Expr> args, LocalBuilder[]? argLocals = null);

    /// <summary>
    /// Evaluates <paramref name="args"/> and leaves a single <c>object[]</c> on the IL stack with
    /// any <see cref="Expr.Spread"/> arguments flattened in place (numeric <c>$Array</c> + arbitrary
    /// iterables). Use when a type-emitter strategy needs the full, spread-expanded argument list as
    /// a runtime array — e.g. the variadic <c>Math.max/min/hypot</c> adapters (#951).
    /// </summary>
    void EmitArgsArrayWithSpread(IReadOnlyList<Expr> args);

    /// <summary>
    /// Emits conversion from the current stack value to the target parameter type.
    /// Handles boxing for object, unboxing for value types, union types, and pass-through.
    /// </summary>
    void EmitConversionForParameter(Expr expr, Type targetType);

    /// <summary>
    /// Emits a default value for the given type (null for reference types, 0 for numbers, etc.).
    /// </summary>
    void EmitDefaultForType(Type type);

    /// <summary>
    /// Emits the value for a trailing parameter slot the call site omits: the <c>$Undefined</c>
    /// sentinel for an <c>object</c> slot (JS: an omitted argument is <c>undefined</c>), else the
    /// type's CLR default. Use this instead of <see cref="EmitDefaultForType"/> when padding omitted
    /// call arguments. (#739/#705)
    /// </summary>
    void EmitOmittedArgument(Type slotType);

    /// <summary>
    /// Emits arguments for a statically resolved user-class method. Surplus arguments are
    /// evaluated and discarded, omitted slots are padded with JavaScript undefined when
    /// representable, and a trailing rest slot is packed into its runtime list marker.
    /// </summary>
    void EmitStaticCallArguments(List<Expr> arguments, MethodBase targetMethod);

    /// <summary>
    /// True when evaluating any of <paramref name="args"/> can suspend the enclosing state machine
    /// (contains an <c>await</c>/<c>yield</c>). Type-emitter strategies use this to decide whether a
    /// fast path must spill arguments before touching the receiver: leaving the receiver (or a partly
    /// built array) on the IL stack across a suspension produces invalid IL (#850). Always false in the
    /// synchronous emitter.
    /// </summary>
    bool ArgsContainSuspension(IReadOnlyList<Expr> args);

    /// <summary>
    /// Evaluates <paramref name="arg"/>, boxes it, and spills it into an await-safe object local (one
    /// that persists across a MoveNext re-entry in state-machine emitters; a plain local in the
    /// synchronous emitter). Use to pre-evaluate call arguments that can suspend so the suspension
    /// happens with a clear stack. Stack: [] -&gt; [].
    /// </summary>
    LocalBuilder SpillBoxedArg(Expr arg);

    /// <summary>
    /// Pops the boxed object reference on top of the stack into an await-safe local (persisted across
    /// a MoveNext re-entry in state-machine emitters; a plain local in the synchronous emitter) and
    /// returns it. Use to move an already-evaluated receiver off the stack before spilling arguments
    /// that can suspend. Stack: [value] -&gt; [].
    /// </summary>
    LocalBuilder SpillStackToObjectLocal();

    /// <summary>
    /// Emits IL that constructs a delegate of the given type pointing at the
    /// arrow's compiled body. For non-capturing arrows: <c>new Func(null, ldftn staticMethod)</c>.
    /// For capturing arrows: allocate the display class instance, populate
    /// captured fields, then <c>new Func(displayInstance, ldftn instanceInvoke)</c>.
    /// On success, leaves the delegate on the stack and returns true. Returns
    /// false (without emitting any IL) when the emitter doesn't support this
    /// path (state-machine emitters) or the arrow shape isn't compatible
    /// (named function expression, async, generator, or other unsupported
    /// configuration). Callers fall through to the legacy <c>$TSFunction</c>
    /// path when this returns false.
    /// </summary>
    bool TryEmitArrowAsDelegate(Expr.ArrowFunction af, Type delegateType);

    /// <summary>
    /// Builds the display-class instance for a capturing arrow (<c>new DisplayClass()</c>
    /// + populate captured fields), leaving it on the stack. Used by the array-HOF boxed-
    /// adapter fast path (#861 L3) to bind an instance adapter to <c>(displayInstance, ldftn
    /// instanceAdapter)</c>. Returns false (without emitting) for unsupported emitters or when
    /// the arrow has no registered display class.
    /// </summary>
    bool TryEmitCapturingArrowDisplayInstance(Expr.ArrowFunction af);
}
