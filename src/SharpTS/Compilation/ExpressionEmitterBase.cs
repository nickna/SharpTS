using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Abstract base class for expression emission across different emitter types.
/// Provides unified dispatch logic and shared helper delegations.
/// </summary>
/// <remarks>
/// This base class centralizes the expression dispatch switch statement that was
/// duplicated across ILEmitter, AsyncMoveNextEmitter, GeneratorMoveNextEmitter,
/// AsyncArrowMoveNextEmitter, and AsyncGeneratorMoveNextEmitter.
///
/// All expression methods are abstract - subclasses must implement them.
/// EmitAwait and EmitYield are virtual and throw by default; async/generator
/// emitters override them with their implementations.
/// </remarks>
public abstract partial class ExpressionEmitterBase : IEmitterContext
{
    #region IEmitterContext Implementation

    CompilationContext IEmitterContext.Context => Ctx;
    ILGenerator IEmitterContext.IL => IL;
    void IEmitterContext.EmitExpression(Expr expr) => EmitExpression(expr);
    void IEmitterContext.EmitBoxIfNeeded(Expr expr) => EnsureBoxed();
    void IEmitterContext.EnsureBoxed() => EnsureBoxed();
    void IEmitterContext.EmitExpressionAsDouble(Expr expr) => EmitExpressionAsDouble(expr);
    void IEmitterContext.SetStackUnknown() => SetStackUnknown();
    void IEmitterContext.SetStackType(StackType type) => SetStackType(type);

    bool IEmitterContext.TryEmitConsoleMethod(Expr.Call call)
    {
        return _helpers.TryEmitConsoleMethod(
            call,
            arg => { EmitExpression(arg); EnsureBoxed(); },
            Ctx.Runtime!);
    }

    void IEmitterContext.EmitFetchCall(List<Expr> arguments) => EmitFetchCall(arguments);

    void IEmitterContext.EmitConversionForParameter(Expr expr, Type targetType) => EmitConversionForParameter(expr, targetType);

    void IEmitterContext.EmitDefaultForType(Type type) => EmitDefaultForType(type);
    bool IEmitterContext.TryEmitArrowAsDelegate(Expr.ArrowFunction af, Type delegateType)
        => TryEmitArrowAsDelegate(af, delegateType);

    /// <summary>
    /// Emits a synchronous unnamed arrow as a typed delegate. State-machine
    /// emitters populate capturing display classes through their variable-
    /// storage hooks; <see cref="ILEmitter"/> overrides this with its richer
    /// direct-local/display-chain implementation.
    /// </summary>
    protected virtual bool TryEmitArrowAsDelegate(Expr.ArrowFunction af, Type delegateType)
    {
        if (af.IsAsync || af.IsGenerator || af.Name != null
            || Ctx.ArrowMethods == null
            || !Ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            return false;
        }

        var delegateCtor = Types.TryGetConstructor(
            delegateType, typeof(object), typeof(IntPtr));
        if (delegateCtor == null)
            return false;

        if (Ctx.DisplayClassConstructors?.TryGetValue(af, out var displayCtor) == true)
        {
            EmitCapturingArrowDisplayViaHooks(af, displayCtor);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        IL.Emit(OpCodes.Ldftn, method);
        IL.Emit(OpCodes.Newobj, delegateCtor);
        SetStackUnknown();
        return true;
    }

    bool IEmitterContext.TryEmitCapturingArrowDisplayInstance(Expr.ArrowFunction af)
        => TryEmitCapturingArrowDisplayInstance(af);

    /// <summary>
    /// Default: only <see cref="ILEmitter"/> can build a capturing arrow's display
    /// instance (the display machinery lives there). Other contexts decline (#861 L3).
    /// </summary>
    protected virtual bool TryEmitCapturingArrowDisplayInstance(Expr.ArrowFunction af) => false;

    #endregion

    protected readonly StateMachineEmitHelpers _helpers;

    protected abstract ILGenerator IL { get; }
    protected abstract CompilationContext Ctx { get; }
    protected abstract TypeProvider Types { get; }
    protected abstract IVariableResolver Resolver { get; }

    /// <summary>
    /// Gets the hoisted field for a variable name, or null if not hoisted.
    /// Override in state machine emitters to check the builder's variable fields.
    /// </summary>
    protected virtual FieldBuilder? GetHoistedVariableField(string name) => null;

    /// <summary>
    /// Gets the state machine's function display class field (<c>&lt;&gt;__functionDC</c>), or
    /// null if this emitter has no function-level display class. Override in a state-machine
    /// emitter whose generator/async function lifts captured-and-mutated locals into a shared
    /// display class, so <see cref="EmitCapturingArrowViaHooks"/> can thread that reference into
    /// an arrow's <c>$functionDC</c> field and the arrow's writes reach shared storage (#674).
    /// </summary>
    protected virtual FieldBuilder? GetFunctionDCField() => null;

    protected ExpressionEmitterBase(StateMachineEmitHelpers helpers)
    {
        _helpers = helpers;
    }

    #region Stack Type Delegation
    protected StackType StackType
    {
        get => _helpers.StackType;
        set => _helpers.StackType = value;
    }

    protected void SetStackUnknown() => _helpers.SetStackUnknown();
    protected void SetStackType(StackType type) => _helpers.SetStackType(type);
    #endregion

    #region Boxing and Type Conversion Delegations
    protected void EnsureBoxed() => _helpers.EnsureBoxed();

    /// <summary>
    /// Boxes an already-evaluated call argument left on the stack. The base
    /// boxes from tracked stack state alone (state-machine emitters alias
    /// EmitBoxIfNeeded to EnsureBoxed); ILEmitter overrides with its richer
    /// EmitBoxIfNeeded, which adds a TypeMap reference-type skip and a
    /// literal fallback for when stack-type tracking misses. This seam is the
    /// single deliberate divergence inside the shared static-dispatch helpers
    /// — keep it explicit rather than folding either behavior into the other.
    /// </summary>
    protected virtual void EnsureBoxedArg(Expr arg) => EnsureBoxed();
    protected void EnsureDouble() => _helpers.EnsureDouble();
    protected void EnsureBoolean() => _helpers.EnsureBoolean();
    protected void EnsureString() => _helpers.EnsureString();
    #endregion

    #region Constant Emission Delegations
    protected void EmitNullConstant() => _helpers.EmitNullConstant();
    protected void EmitUndefinedConstant() => _helpers.EmitUndefinedConstant();
    protected void EmitDoubleConstant(double value) => _helpers.EmitDoubleConstant(value);
    protected void EmitBoolConstant(bool value) => _helpers.EmitBoolConstant(value);
    protected void EmitStringConstant(string value) => _helpers.EmitStringConstant(value);
    #endregion

    #region Common Helper Wrappers - Delegated to StateMachineEmitHelpers

    // Boxing
    protected void EmitBoxedDoubleConstant(double value) => _helpers.EmitBoxedDoubleConstant(value);
    protected void EmitBoxedBoolConstant(bool value) => _helpers.EmitBoxedBoolConstant(value);
    protected void EmitBoxDouble() => _helpers.EmitBoxDouble();
    protected void EmitBoxBool() => _helpers.EmitBoxBool();

    // Arithmetic
    protected void EmitAdd_Double() => _helpers.EmitAdd_Double();
    protected void EmitSub_Double() => _helpers.EmitSub_Double();
    protected void EmitMul_Double() => _helpers.EmitMul_Double();
    protected void EmitDiv_Double() => _helpers.EmitDiv_Double();
    protected void EmitRem_Double() => _helpers.EmitRem_Double();
    protected void EmitNeg_Double() => _helpers.EmitNeg_Double();

    // Comparison
    protected void EmitClt_Boolean() => _helpers.EmitClt_Boolean();
    protected void EmitCgt_Boolean() => _helpers.EmitCgt_Boolean();
    protected void EmitCeq_Boolean() => _helpers.EmitCeq_Boolean();
    protected void EmitLessOrEqual_Boolean() => _helpers.EmitLessOrEqual_Boolean();
    protected void EmitGreaterOrEqual_Boolean() => _helpers.EmitGreaterOrEqual_Boolean();

    // Method calls
    protected void EmitCallUnknown(MethodInfo method) => _helpers.EmitCallUnknown(method);
    protected void EmitCallvirtUnknown(MethodInfo method) => _helpers.EmitCallvirtUnknown(method);
    protected void EmitCallString(MethodInfo method) => _helpers.EmitCallString(method);
    protected void EmitCallBoolean(MethodInfo method) => _helpers.EmitCallBoolean(method);
    protected void EmitCallDouble(MethodInfo method) => _helpers.EmitCallDouble(method);
    protected void EmitCallAndBoxDouble(MethodInfo method) => _helpers.EmitCallAndBoxDouble(method);
    protected void EmitCallAndBoxBool(MethodInfo method) => _helpers.EmitCallAndBoxBool(method);

    // Variable loads
    protected void EmitLdlocUnknown(LocalBuilder local) => _helpers.EmitLdlocUnknown(local);
    protected void EmitLdargUnknown(int argIndex) => _helpers.EmitLdargUnknown(argIndex);
    protected void EmitLdfldUnknown(FieldInfo field) => _helpers.EmitLdfldUnknown(field);

    // Specialized
    protected void EmitNewobjUnknown(ConstructorInfo ctor) => _helpers.EmitNewobjUnknown(ctor);
    protected void EmitConvertToDouble() => _helpers.EmitConvertToDouble();
    protected void EmitConvR8AndBox() => _helpers.EmitConvR8AndBox();
    protected void EmitObjectEqualsBoxed() => _helpers.EmitObjectEqualsBoxed();
    protected void EmitObjectNotEqualsBoxed() => _helpers.EmitObjectNotEqualsBoxed();

    #endregion

    #region Core Expression Dispatch
    /// <summary>
    /// Dispatches expression emission to the appropriate handler method.
    /// </summary>
    public virtual void EmitExpression(Expr expr)
    {
        switch (expr)
        {
            case Expr.Comma c:
                EmitComma(c);
                break;
            case Expr.DestructuringAssign da:
                EmitDestructuringAssign(da);
                break;
            case Expr.Literal lit:
                EmitLiteral(lit);
                break;
            case Expr.Variable v:
                EmitVariable(v);
                break;
            case Expr.Assign a:
                EmitAssign(a);
                break;
            case Expr.Binary b:
                EmitBinary(b);
                break;
            case Expr.Logical l:
                EmitLogical(l);
                break;
            case Expr.Unary u:
                EmitUnary(u);
                break;
            case Expr.Delete del:
                EmitDelete(del);
                break;
            case Expr.Call c:
                if (c.Optional)
                    EmitOptionalCall(c);
                else
                    EmitCall(c);
                break;
            case Expr.Get g:
                EmitGet(g);
                break;
            case Expr.Set s:
                EmitSet(s);
                break;
            case Expr.GetPrivate gp:
                EmitGetPrivate(gp);
                break;
            case Expr.SetPrivate sp:
                EmitSetPrivate(sp);
                break;
            case Expr.CallPrivate cp:
                EmitCallPrivate(cp);
                break;
            case Expr.GetIndex gi:
                EmitGetIndex(gi);
                break;
            case Expr.SetIndex si:
                EmitSetIndex(si);
                break;
            case Expr.Ternary t:
                EmitTernary(t);
                break;
            case Expr.NullishCoalescing nc:
                EmitNullishCoalescing(nc);
                break;
            case Expr.TemplateLiteral tl:
                EmitTemplateLiteral(tl);
                break;
            case Expr.TaggedTemplateLiteral ttl:
                EmitTaggedTemplateLiteral(ttl);
                break;
            case Expr.ArrayLiteral al:
                EmitArrayLiteral(al);
                break;
            case Expr.ObjectLiteral ol:
                EmitObjectLiteral(ol);
                break;
            case Expr.New n:
                EmitNew(n);
                break;
            case Expr.This:
                EmitThis();
                break;
            case Expr.Super s:
                EmitSuper(s);
                break;
            case Expr.CompoundAssign ca:
                EmitCompoundAssign(ca);
                break;
            case Expr.CompoundSet cs:
                EmitCompoundSet(cs);
                break;
            case Expr.CompoundSetIndex csi:
                EmitCompoundSetIndex(csi);
                break;
            case Expr.LogicalAssign la:
                EmitLogicalAssign(la);
                break;
            case Expr.LogicalSet ls:
                EmitLogicalSet(ls);
                break;
            case Expr.LogicalSetIndex lsi:
                EmitLogicalSetIndex(lsi);
                break;
            case Expr.PrefixIncrement pi:
                EmitPrefixIncrement(pi);
                break;
            case Expr.PostfixIncrement poi:
                EmitPostfixIncrement(poi);
                break;
            case Expr.ArrowFunction af:
                EmitArrowFunction(af);
                break;
            case Expr.RegexLiteral re:
                EmitRegexLiteral(re);
                break;
            case Expr.DynamicImport di:
                EmitDynamicImport(di);
                break;
            case Expr.ImportMeta im:
                EmitImportMeta(im);
                break;
            case Expr.Grouping g:
                EmitGrouping(g);
                break;
            case Expr.TypeAssertion ta:
                EmitTypeAssertion(ta);
                break;
            case Expr.Satisfies sat:
                EmitSatisfies(sat);
                break;
            case Expr.NonNullAssertion nna:
                EmitNonNullAssertion(nna);
                break;
            case Expr.Spread sp:
                EmitSpread(sp);
                break;
            case Expr.Await aw:
                EmitAwait(aw);
                break;
            case Expr.Yield y:
                EmitYield(y);
                break;
            case Expr.ClassExpr ce:
                EmitClassExpression(ce);
                break;
            default:
                throw new CompileException($"Unhandled expression type in ILEmitter: {expr.GetType().Name}");
        }
    }
    #endregion

    #region Virtual Expression Methods with Default Implementations
    // These methods provide default implementations suitable for state machine emitters.
    // ILEmitter overrides most with stack-type-tracked and optimized versions.
    // AsyncArrowMoveNextEmitter overrides where capture indirection differs.

    /// <summary>
    /// Emits a literal value. Default uses helper methods (EmitNullConstant, EmitDoubleConstant, etc.).
    /// ILEmitter overrides for BigInteger/SharpTSUndefined support.
    /// AsyncArrowMoveNextEmitter overrides for eager boxing.
    /// </summary>
    protected virtual void EmitLiteral(Expr.Literal lit)
    {
        switch (lit.Value)
        {
            case null: EmitNullConstant(); break;
            case double d: EmitDoubleConstant(d); break;
            case bool b: EmitBoolConstant(b); break;
            case string s: EmitStringConstant(s); break;
            // The `undefined` keyword parses to a Literal holding the $Undefined sentinel
            // (Parser.Expressions.cs), as do array holes. State-machine emitters use this base
            // method, so without an explicit arm `undefined` would fall to `default` and emit
            // CLR null — making `=== undefined` collapse into `=== null` and any undefined operand
            // stringify as "null" inside async/generators (#600, #629). ILEmitter has the same arm.
            // (Requires the helper's runtime to be wired via SetRuntime; see the emitters' EmitMoveNext.)
            case Runtime.Types.SharpTSUndefined: EmitUndefinedConstant(); break;
            default: IL.Emit(OpCodes.Ldnull); SetStackUnknown(); break;
        }
    }

    /// <summary>
    /// Emits an expression boxed and spills it into a fresh object local.
    /// State-machine emitters (the only users of these base implementations) can suspend
    /// inside any subexpression (await); values left on the IL evaluation stack across a
    /// suspension produce invalid IL, so multi-operand emission must evaluate each operand
    /// into a local first — the same await-safe pattern as EmitFunctionValueCall.
    /// </summary>
    protected LocalBuilder SpillBoxed(Expr e)
    {
        EmitExpression(e);
        EnsureBoxed();
        // Register the temp so a suspension (await) inside a *later* operand of the same
        // expression persists this value to a field and rehydrates it on resume (#400).
        // In non-state-machine emitters this is just a plain local.
        return _helpers.SpillStoreObject();
    }

    // IEmitterContext spill seam (#850): exposes the await-safe spilling already used by the generic
    // call path so type-emitter strategies (ArrayEmitter, etc.) can pre-spill arguments instead of
    // evaluating them while the receiver sits on the IL stack. In the synchronous ILEmitter these
    // resolve to plain locals, so non-async codegen is unchanged.
    bool IEmitterContext.ArgsContainSuspension(IReadOnlyList<Expr> args) => AnyContainsSuspension(args);
    LocalBuilder IEmitterContext.SpillBoxedArg(Expr arg) => SpillBoxed(arg);
    LocalBuilder IEmitterContext.SpillStackToObjectLocal() => _helpers.SpillStoreObject();

    /// <summary>
    /// Emits a binary operator expression. Default boxes both operands and delegates to TryEmitBinaryOperator.
    /// ILEmitter overrides with stack-type-tracked fast paths.
    /// </summary>
    protected virtual void EmitBinary(Expr.Binary b)
    {
        // Bitwise and shift operators need int32 coercion of both operands per ECMA-262 ToInt32.
        if (IsBitwiseOrShiftOp(b.Operator.Type))
        {
            EmitBitwiseOrShiftBinary(b);
            return;
        }

        // Spill operands so an await inside Right doesn't suspend with Left on the stack.
        var leftLocal = SpillBoxed(b.Left);
        var rightLocal = SpillBoxed(b.Right);
        IL.Emit(OpCodes.Ldloc, leftLocal);
        IL.Emit(OpCodes.Ldloc, rightLocal);

        // `in` and `instanceof` are binary operators not covered by TryEmitBinaryOperator.
        switch (b.Operator.Type)
        {
            case TokenType.IN:
                IL.Emit(OpCodes.Call, Ctx.Runtime!.HasIn);
                IL.Emit(OpCodes.Box, typeof(bool));
                SetStackUnknown();
                return;
            case TokenType.INSTANCEOF:
                IL.Emit(OpCodes.Call, Ctx.Runtime!.InstanceOf);
                IL.Emit(OpCodes.Box, typeof(bool));
                SetStackUnknown();
                return;
        }

        if (!_helpers.TryEmitBinaryOperator(b.Operator.Type, Ctx.Runtime!.Add, Ctx.Runtime!.Equals))
        {
            // Unsupported operator: pop both operands (stack must stay balanced for the verifier)
            // and push a placeholder. Leaving one operand on the stack corrupts state-machine
            // spill accounting for subsequent yields.
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldnull);
            SetStackUnknown();
        }
    }

    private static bool IsBitwiseOrShiftOp(TokenType op) => op is
        TokenType.AMPERSAND or TokenType.PIPE or TokenType.CARET or
        TokenType.LESS_LESS or TokenType.GREATER_GREATER or TokenType.GREATER_GREATER_GREATER;

    /// <summary>
    /// Emits a bitwise (&amp;, |, ^) or shift (&lt;&lt;, &gt;&gt;, &gt;&gt;&gt;) binary operator.
    /// Per ECMA-262 ToInt32, both operands are coerced to int32, then the op applies.
    /// Mirrors ILEmitter.EmitBitwiseBinary but without stack-type tracking.
    /// </summary>
    private void EmitBitwiseOrShiftBinary(Expr.Binary b)
    {
        // Spill the coerced left operand so an await inside Right suspends with an empty stack.
        EmitExpression(b.Left);
        EnsureBoxed();
        IL.Emit(OpCodes.Call, Ctx.Runtime!.JsToInt32);
        var leftLocal = IL.DeclareLocal(typeof(int));
        IL.Emit(OpCodes.Stloc, leftLocal);

        EmitExpression(b.Right);
        EnsureBoxed();
        IL.Emit(OpCodes.Call, Ctx.Runtime!.JsToInt32);
        var rightLocal = IL.DeclareLocal(typeof(int));
        IL.Emit(OpCodes.Stloc, rightLocal);

        IL.Emit(OpCodes.Ldloc, leftLocal);
        IL.Emit(OpCodes.Ldloc, rightLocal);

        switch (b.Operator.Type)
        {
            case TokenType.AMPERSAND: IL.Emit(OpCodes.And); break;
            case TokenType.PIPE: IL.Emit(OpCodes.Or); break;
            case TokenType.CARET: IL.Emit(OpCodes.Xor); break;
            case TokenType.LESS_LESS:
                IL.Emit(OpCodes.Ldc_I4, 0x1F);
                IL.Emit(OpCodes.And);
                IL.Emit(OpCodes.Shl);
                break;
            case TokenType.GREATER_GREATER:
                IL.Emit(OpCodes.Ldc_I4, 0x1F);
                IL.Emit(OpCodes.And);
                IL.Emit(OpCodes.Shr);
                break;
            case TokenType.GREATER_GREATER_GREATER:
                IL.Emit(OpCodes.Ldc_I4, 0x1F);
                IL.Emit(OpCodes.And);
                IL.Emit(OpCodes.Shr_Un);
                IL.Emit(OpCodes.Conv_U8);
                IL.Emit(OpCodes.Conv_R8);
                IL.Emit(OpCodes.Box, typeof(double));
                SetStackUnknown();
                return;
        }

        // Integer-result ops: convert int32 result back to double and box to match JS number semantics.
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Box, typeof(double));
        SetStackUnknown();
    }

    /// <summary>
    /// Emits an index get expression (obj[index]). Default boxes both and calls Runtime.GetIndex.
    /// ILEmitter overrides with IndexTarget dispatch and stack type tracking.
    /// </summary>
    protected virtual void EmitGetIndex(Expr.GetIndex gi)
    {
        // Stable primitive Promise.all result: the analyzer proved this local
        // cannot escape and is observed only through numeric index reads and
        // length, so PromiseAllPrimitive returns an internal List<double>.
        // State-machine locals normally use object fields; cast that field
        // directly instead of routing every read through Runtime.GetIndex.
        if (!gi.Optional
            && gi.Object is Expr.Variable stableAllResult
            && Ctx.TypeMap?.IsStablePrimitivePromiseAllResultUse(stableAllResult) == true
            && Ctx.TypeMap.Get(gi.Index) is SharpTS.TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
                or SharpTS.TypeSystem.TypeInfo.NumberLiteral)
        {
            EmitExpressionAsDouble(gi.Index);
            var numericIndexLocal = IL.DeclareLocal(Types.Double);
            IL.Emit(OpCodes.Stloc, numericIndexLocal);
            var indexLocal = IL.DeclareLocal(Types.Int32);
            var listLocal = IL.DeclareLocal(Types.ListOfDouble);
            EmitExpression(gi.Object);
            EnsureBoxed();
            IL.Emit(OpCodes.Castclass, Types.ListOfDouble);
            IL.Emit(OpCodes.Stloc, listLocal);

            var outOfBoundsLabel = IL.DefineLabel();
            var doneLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, numericIndexLocal);
            IL.Emit(OpCodes.Ldc_R8, 0.0);
            IL.Emit(OpCodes.Blt_Un, outOfBoundsLabel);
            IL.Emit(OpCodes.Ldloc, numericIndexLocal);
            IL.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
            IL.Emit(OpCodes.Bgt_Un, outOfBoundsLabel);
            IL.Emit(OpCodes.Ldloc, numericIndexLocal);
            IL.Emit(OpCodes.Ldloc, numericIndexLocal);
            IL.Emit(OpCodes.Call, Types.GetMethod(Types.Math, "Truncate", Types.Double));
            IL.Emit(OpCodes.Bne_Un, outOfBoundsLabel);
            IL.Emit(OpCodes.Ldloc, numericIndexLocal);
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Stloc, indexLocal);
            IL.Emit(OpCodes.Ldloc, indexLocal);
            IL.Emit(OpCodes.Ldloc, listLocal);
            IL.Emit(OpCodes.Callvirt,
                Types.GetProperty(Types.ListOfDouble, "Count").GetGetMethod()!);
            IL.Emit(OpCodes.Bge_Un, outOfBoundsLabel);
            IL.Emit(OpCodes.Ldloc, listLocal);
            IL.Emit(OpCodes.Ldloc, indexLocal);
            IL.Emit(OpCodes.Callvirt,
                Types.GetMethod(Types.ListOfDouble, "get_Item", Types.Int32));
            IL.Emit(OpCodes.Box, Types.Double);
            IL.Emit(OpCodes.Br, doneLabel);
            IL.MarkLabel(outOfBoundsLabel);
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);
            IL.MarkLabel(doneLabel);
            SetStackUnknown();
            return;
        }

        // globalThis[key] → GlobalThisGetProperty(key)
        if (gi.Object is Expr.Variable gtGetIdx && gtGetIdx.Name.Lexeme == "globalThis")
        {
            EmitExpression(gi.Index);
            EnsureBoxed();
            IL.Emit(OpCodes.Callvirt, Types.GetMethodNoParams(Types.Object, "ToString"));
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GlobalThisGetProperty);
            SetStackUnknown();
            return;
        }

        // Spill the object so an await inside Index doesn't suspend with it on the stack.
        var objLocal = SpillBoxed(gi.Object);

        if (gi.Optional)
        {
            var nullishLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();

            // Check for null
            IL.Emit(OpCodes.Ldloc, objLocal);
            IL.Emit(OpCodes.Brfalse, nullishLabel);

            // Check for undefined
            IL.Emit(OpCodes.Ldloc, objLocal);
            IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
            IL.Emit(OpCodes.Brtrue, nullishLabel);

            // Not nullish — proceed with index access
            var idxLocal = SpillBoxed(gi.Index);
            IL.Emit(OpCodes.Ldloc, objLocal);
            IL.Emit(OpCodes.Ldloc, idxLocal);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetIndex);
            IL.Emit(OpCodes.Br, endLabel);

            IL.MarkLabel(nullishLabel);
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);

            IL.MarkLabel(endLabel);
            SetStackUnknown();
        }
        else
        {
            var idxLocal = SpillBoxed(gi.Index);
            // RequireObjectCoercible: `undefined[k]` throws a guest TypeError instead
            // of silently yielding undefined (#701), and `null[k]` too now that sloppy
            // `this` is the globalThis sentinel (#735). Optional `o?.[k]` short-circuited
            // above. Null-placeholder globals (e.g. `process`) are exempt.
            if (!IsNullPlaceholderGlobal(gi.Object))
                EmitThrowIfUndefinedIndexReceiver(objLocal, idxLocal);
            IL.Emit(OpCodes.Ldloc, objLocal);
            IL.Emit(OpCodes.Ldloc, idxLocal);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetIndex);
            SetStackUnknown();
        }
    }

    /// <summary>
    /// Emits an index set expression (obj[index] = value). Default saves value to local (SetIndex is void),
    /// emits obj + index, calls SetIndex, then pushes value back as expression result.
    /// ILEmitter overrides with IndexTarget dispatch.
    /// </summary>
    protected virtual void EmitSetIndex(Expr.SetIndex si)
    {
        // globalThis[key] = value → GlobalThisSetProperty(key, value)
        if (si.Object is Expr.Variable gtSetIdx && gtSetIdx.Name.Lexeme == "globalThis")
        {
            // Evaluate the reference (base + key expression) before the RHS;
            // ToPropertyKey remains deferred to the runtime store.
            EmitExpression(si.Index);
            EnsureBoxed();
            var indexTemp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, indexTemp);
            EmitExpression(si.Value);
            EnsureBoxed();
            var valueTemp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, valueTemp);
            IL.Emit(OpCodes.Ldloc, indexTemp);
            IL.Emit(OpCodes.Callvirt, Types.GetMethodNoParams(Types.Object, "ToString"));
            IL.Emit(OpCodes.Ldloc, valueTemp);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GlobalThisSetProperty);
            IL.Emit(OpCodes.Ldloc, valueTemp);
            SetStackUnknown();
            return;
        }

        // Evaluate the reference before the RHS and spill each component so a
        // suspension still occurs with an empty stack.
        var objLocal = SpillBoxed(si.Object);
        var idxLocal = SpillBoxed(si.Index);
        var valueLocal = SpillBoxed(si.Value);

        // RequireObjectCoercible (PutValue): a null/undefined base throws a guest
        // TypeError ("Cannot set properties of undefined|null (setting '<key>')")
        // instead of silently no-op'ing (#733). Null-placeholder globals are exempt.
        if (!IsNullPlaceholderGlobal(si.Object))
            EmitThrowIfUndefinedIndexReceiver(objLocal, idxLocal, isWrite: true);

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldloc, idxLocal);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.SetIndex);

        IL.Emit(OpCodes.Ldloc, valueLocal);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits 'this' expression. Default uses GetThisField() hook — loads from field if non-null,
    /// otherwise resolves to the globalThis sentinel (sloppy-mode `this` = globalThis) when an
    /// emitted runtime is available, so that value-position JS null stays distinct from a sloppy
    /// `this` receiver (#735/#733). ILEmitter overrides (Resolver.LoadThis). AsyncArrowMoveNextEmitter
    /// overrides (outer state machine capture).
    /// </summary>
    protected virtual void EmitThis()
    {
        var thisField = GetThisField();
        if (thisField != null)
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, thisField);
            SetStackUnknown();
        }
        else if (Ctx.Runtime?.GlobalThisSingletonField != null)
        {
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime.GlobalThisSingletonField);
            SetStackUnknown();
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }

    /// <summary>
    /// Emits a super method access. Default loads 'this' via EmitThis(), then calls GetSuperMethod.
    /// ILEmitter and AsyncArrowMoveNextEmitter override with their own this-loading logic.
    /// </summary>
    protected virtual void EmitSuper(Expr.Super s)
    {
        var thisField = GetThisField();
        if (thisField != null)
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, thisField);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        IL.Emit(OpCodes.Ldstr, s.Method?.Lexeme ?? "constructor");
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetSuperMethod);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a variable load. Default uses resolver fallback chain:
    /// resolver → TopLevelStaticVars → Functions → NamespaceFields → CapturedTopLevelVars → null.
    /// ILEmitter overrides (pseudo-variables, class types, inner functions, ReferenceError).
    /// AsyncArrowMoveNextEmitter overrides (capture indirection).
    /// </summary>
    protected virtual void EmitVariable(Expr.Variable v)
    {
        string name = v.Name.Lexeme;

        if (TryEmitDefaultParameterTdz(name))
            return;

        if (Ctx.LexicalInitializerTdzName == name)
        {
            IL.Emit(OpCodes.Ldstr, name);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.ThrowUndefinedVariable);
            IL.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        var stackType = Resolver.TryLoadVariable(name);
        if (stackType != null)
        {
            SetStackType(stackType.Value);
            return;
        }

        // CommonJS: bare `exports` → ldsfld $exports of the current CJS module.
        if (TryEmitCjsVariable(name)) return;

        if (TryEmitGlobalVariable(name)) return;

        // An unresolved identifier is a ReferenceError in every execution
        // context, including state-machine bodies.  Returning CLR null here
        // made `yield missingName` silently yield null while the synchronous
        // ILEmitter correctly threw.
        IL.Emit(OpCodes.Ldstr, name);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ThrowUndefinedVariable);
        // Unreachable stack value for verifier shape.
        IL.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    /// <summary>Emits the runtime ReferenceError required for a read from a parameter TDZ.</summary>
    protected bool TryEmitDefaultParameterTdz(string name)
    {
        if (Ctx.DefaultParameterTdzNames?.Contains(name) != true)
            return false;

        IL.Emit(OpCodes.Ldstr, name);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ThrowUndefinedVariable);
        // Unreachable, but keeps the evaluation stack valid for the expression path.
        IL.Emit(OpCodes.Ldnull);
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Emits a variable assignment. Default emits value, dups, then tries global store or resolver.
    /// ILEmitter overrides (pseudo-variables, class types, inner functions, ReferenceError).
    /// AsyncArrowMoveNextEmitter overrides (capture indirection).
    /// </summary>
    protected virtual void EmitAssign(Expr.Assign a)
    {
        // CommonJS: `exports = X` → stsfld $exports (mirrors TryEmitCjsSet for module.exports).
        if (TryEmitCjsAssign(a)) return;

        string name = a.Name.Lexeme;

        EmitExpression(a.Value);
        EnsureBoxed();
        if (Ctx.LexicalInitializerTdzName == name)
        {
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldstr, name);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.ThrowUndefinedVariable);
            IL.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }
        IL.Emit(OpCodes.Dup);

        if (TryEmitGlobalStore(name)) return;

        Resolver.TryStoreVariable(name);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a property set (obj.prop = value). Default checks static fields, TypeEmitterRegistry,
    /// then falls back to dynamic SetProperty.
    /// ILEmitter overrides (property descriptor, additional patterns).
    /// </summary>
    /// <summary>
    /// Built-in module live property reads (cluster.schedulingPolicy, #1170): opt-in
    /// members route through the module emitter at each access site instead of the
    /// import-time namespace-dict snapshot. Shared by EmitGet here and in ILEmitter
    /// (which overrides EmitGet without calling base).
    /// </summary>
    protected bool TryEmitBuiltInModuleLivePropertyGet(Expr.Get g)
    {
        if (g.Object is Expr.Variable modVar &&
            Ctx.BuiltInModuleNamespaces != null &&
            Ctx.BuiltInModuleNamespaces.TryGetValue(modVar.Name.Lexeme, out var moduleName) &&
            Ctx.BuiltInModuleEmitterRegistry?.GetEmitter(moduleName) is { } moduleEmitter &&
            moduleEmitter.HasLivePropertyGet(g.Name.Lexeme) &&
            moduleEmitter.TryEmitPropertyGet((IEmitterContext)this, g.Name.Lexeme))
        {
            SetStackUnknown();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Built-in module property writes (cluster.schedulingPolicy = x, #1170): route
    /// through the module emitter so the write reaches the runtime singleton rather
    /// than landing on the namespace-dict snapshot. The module emitter leaves the
    /// assigned value on the stack.
    /// </summary>
    protected bool TryEmitBuiltInModulePropertySet(Expr.Set s)
    {
        if (s.Object is Expr.Variable modVar &&
            Ctx.BuiltInModuleNamespaces != null &&
            Ctx.BuiltInModuleNamespaces.TryGetValue(modVar.Name.Lexeme, out var moduleName) &&
            Ctx.BuiltInModuleEmitterRegistry?.GetEmitter(moduleName) is { } moduleEmitter &&
            moduleEmitter.TryEmitPropertySet((IEmitterContext)this, s.Name.Lexeme, s.Value))
        {
            SetStackUnknown();
            return true;
        }
        return false;
    }

    protected virtual void EmitSet(Expr.Set s)
    {
        // CommonJS: `module.exports = X` → stsfld $exports.
        if (TryEmitCjsSet(s)) return;

        if (TryEmitBuiltInModulePropertySet(s)) return;

        // Handle globalThis.x = value
        if (s.Object is Expr.Variable gtVar && gtVar.Name.Lexeme == "globalThis")
        {
            EmitExpression(s.Value);
            EnsureBoxed();
            var gtResultTemp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, gtResultTemp);
            IL.Emit(OpCodes.Ldstr, s.Name.Lexeme);
            IL.Emit(OpCodes.Ldloc, gtResultTemp);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GlobalThisSetProperty);
            IL.Emit(OpCodes.Ldloc, gtResultTemp);
            SetStackUnknown();
            return;
        }

        // Handle static field assignment: Class.field = value
        if (s.Object is Expr.Variable classVar &&
            Ctx.Classes.TryGetValue(Ctx.ResolveClassName(classVar.Name.Lexeme), out var staticSetClassBuilder))
        {
            string resolvedClassName = Ctx.ResolveClassName(classVar.Name.Lexeme);
            if (Ctx.ClassRegistry!.TryGetOwnCallableStaticField(resolvedClassName, s.Name.Lexeme, staticSetClassBuilder, out var staticField))
            {
                EmitExpression(s.Value);
                EnsureBoxed();
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stsfld, staticField!);
                SetStackUnknown();
                return;
            }
        }

        // Type-first dispatch: property setter via TypeEmitterRegistry
        if (TryEmitTypeRegistryPropertySet(s)) return;

        // Default: dynamic property assignment.
        // Spill operands so an await inside Value doesn't suspend with the object on the stack.
        var objLocal = SpillBoxed(s.Object);
        var valueLocal = SpillBoxed(s.Value);

        // RequireObjectCoercible (PutValue): a null/undefined base throws a guest
        // TypeError ("Cannot set properties of undefined|null (setting 'X')") (#733).
        // Null-placeholder globals (e.g. `process`) are exempt.
        if (!IsNullPlaceholderGlobal(s.Object))
            EmitThrowIfReceiverUndefined(objLocal, s.Name.Lexeme, isWrite: true);

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.SetProperty);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a private field get (#field). Default uses ConditionalWeakTable pattern.
    /// ILEmitter overrides with its own implementation.
    /// </summary>
    protected virtual void EmitGetPrivate(Expr.GetPrivate gp)
    {
        string fieldName = gp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        string? className = Ctx.CurrentClassName;
        if (className == null)
        {
            IL.Emit(OpCodes.Ldstr, $"Cannot access private field '#{fieldName}' - class context not available");
            IL.Emit(OpCodes.Newobj, Types.InvalidOperationExceptionCtorString);
            IL.Emit(OpCodes.Throw);
            return;
        }
        className = Ctx.ResolvePrivateFieldOwner(className, fieldName);

        if (((gp.Object is Expr.Variable classVar &&
              classVar.Name.Lexeme == Ctx.CurrentClassShortName)
             || (gp.Object is Expr.This && !Ctx.IsInstanceMethod)) &&
            Ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var staticField))
        {
            IL.Emit(OpCodes.Ldsfld, staticField!);
            SetStackUnknown();
            return;
        }

        var storageField = Ctx.ClassRegistry!.GetPrivateFieldStorage(className);
        if (storageField != null)
        {
            var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
            var dictType = typeof(Dictionary<string, object?>);
            var dictLocal = IL.DeclareLocal(dictType);

            IL.Emit(OpCodes.Ldsfld, storageField);
            EmitExpression(gp.Object);
            EnsureBoxed();
            IL.Emit(OpCodes.Ldloca, dictLocal);
            var tryGetValueMethod = Types.GetMethod(cwtType, "TryGetValue", typeof(object), dictType.MakeByRefType());
            IL.Emit(OpCodes.Callvirt, tryGetValueMethod);

            var successLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue, successLabel);
            GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!,
                $"Cannot read private member #{fieldName} from an object whose class did not declare it");
            IL.MarkLabel(successLabel);

            IL.Emit(OpCodes.Ldloc, dictLocal);
            IL.Emit(OpCodes.Ldstr, fieldName);
            IL.Emit(OpCodes.Callvirt, dictType.GetMethod("get_Item", [typeof(string)])!);
            SetStackUnknown();
            return;
        }

        if (Ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var fallbackStaticField))
        {
            IL.Emit(OpCodes.Ldsfld, fallbackStaticField!);
            SetStackUnknown();
            return;
        }

        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!,
            $"Private field '#{fieldName}' not found in class '{className}'");
    }

    /// <summary>
    /// Emits a private field set (#field = value). Default uses ConditionalWeakTable pattern.
    /// ILEmitter overrides with its own implementation.
    /// </summary>
    protected virtual void EmitSetPrivate(Expr.SetPrivate sp)
    {
        string fieldName = sp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        string? className = Ctx.CurrentClassName;
        if (className == null)
        {
            IL.Emit(OpCodes.Ldstr, $"Cannot write private field '#{fieldName}' - class context not available");
            IL.Emit(OpCodes.Newobj, Types.InvalidOperationExceptionCtorString);
            IL.Emit(OpCodes.Throw);
            return;
        }
        className = Ctx.ResolvePrivateFieldOwner(className, fieldName);

        if (((sp.Object is Expr.Variable classVar &&
              classVar.Name.Lexeme == Ctx.CurrentClassShortName)
             || (sp.Object is Expr.This && !Ctx.IsInstanceMethod)) &&
            Ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var staticField))
        {
            EmitExpression(sp.Value);
            EnsureBoxed();
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stsfld, staticField!);
            SetStackUnknown();
            return;
        }

        var storageField = Ctx.ClassRegistry!.GetPrivateFieldStorage(className);
        if (storageField != null)
        {
            var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
            var dictType = typeof(Dictionary<string, object?>);
            var dictLocal = IL.DeclareLocal(dictType);
            var valueLocal = IL.DeclareLocal(typeof(object));

            IL.Emit(OpCodes.Ldsfld, storageField);
            EmitExpression(sp.Object);
            EnsureBoxed();
            IL.Emit(OpCodes.Ldloca, dictLocal);
            var tryGetValueMethod = Types.GetMethod(cwtType, "TryGetValue", typeof(object), dictType.MakeByRefType());
            IL.Emit(OpCodes.Callvirt, tryGetValueMethod);

            var successLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue, successLabel);
            GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!,
                $"Cannot write private member #{fieldName} to an object whose class did not declare it");
            IL.MarkLabel(successLabel);

            EmitExpression(sp.Value);
            EnsureBoxed();
            IL.Emit(OpCodes.Stloc, valueLocal);
            IL.Emit(OpCodes.Ldloc, dictLocal);
            IL.Emit(OpCodes.Ldstr, fieldName);
            IL.Emit(OpCodes.Ldloc, valueLocal);
            IL.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item", [typeof(string), typeof(object)])!);
            IL.Emit(OpCodes.Ldloc, valueLocal);
            SetStackUnknown();
            return;
        }

        if (Ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var fallbackStaticField))
        {
            EmitExpression(sp.Value);
            EnsureBoxed();
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stsfld, fallbackStaticField!);
            SetStackUnknown();
            return;
        }

        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!,
            $"Private field '#{fieldName}' not found in class '{className}'");
    }

    /// <summary>
    /// Emits a private method call (#method(args)). Default uses ConditionalWeakTable pattern.
    /// ILEmitter overrides with its own implementation.
    /// </summary>
    protected virtual void EmitCallPrivate(Expr.CallPrivate cp)
    {
        string methodName = cp.Name.Lexeme;
        if (methodName.StartsWith('#'))
            methodName = methodName[1..];

        string? className = Ctx.CurrentClassName;
        if (className == null)
        {
            IL.Emit(OpCodes.Ldstr, $"Cannot call private method '#{methodName}' - class context not available");
            IL.Emit(OpCodes.Newobj, Types.InvalidOperationExceptionCtorString);
            IL.Emit(OpCodes.Throw);
            return;
        }
        className = Ctx.ResolvePrivateMethodOwner(className, methodName);

        if (((cp.Object is Expr.Variable classVar &&
              classVar.Name.Lexeme == Ctx.CurrentClassShortName)
             || (cp.Object is Expr.This && !Ctx.IsInstanceMethod)) &&
            Ctx.ClassRegistry!.TryGetStaticPrivateMethod(className, methodName, out var staticMethod))
        {
            foreach (var arg in cp.Arguments)
            {
                EmitExpression(arg);
                EnsureBoxed();
            }
            EmitPrivateCallUndefinedPadding(cp.Arguments.Count, staticMethod!.GetParameters().Length);
            IL.Emit(OpCodes.Call, staticMethod!);
            SetStackUnknown();
            return;
        }

        if (Ctx.ClassRegistry!.TryGetPrivateMethod(className, methodName, out var instanceMethod))
        {
            var storageField = Ctx.ClassRegistry!.GetPrivateFieldStorage(className);
            if (storageField != null)
            {
                var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
                var dictType = typeof(Dictionary<string, object?>);
                var objLocal = IL.DeclareLocal(typeof(object));
                var dictLocal = IL.DeclareLocal(dictType);

                EmitExpression(cp.Object);
                EnsureBoxed();
                IL.Emit(OpCodes.Stloc, objLocal);

                IL.Emit(OpCodes.Ldsfld, storageField);
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Ldloca, dictLocal);
                var tryGetValueMethod = Types.GetMethod(cwtType, "TryGetValue", typeof(object), dictType.MakeByRefType());
                IL.Emit(OpCodes.Callvirt, tryGetValueMethod);

                var validLabel = IL.DefineLabel();
                IL.Emit(OpCodes.Brtrue, validLabel);
                GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!,
                    $"Cannot call private method #{methodName} on an object whose class did not declare it");
                IL.MarkLabel(validLabel);

                IL.Emit(OpCodes.Ldloc, objLocal);
                if (Ctx.ClassRegistry.TryGetClass(className, out var privateOwnerBuilder)
                    && privateOwnerBuilder != null)
                    IL.Emit(OpCodes.Castclass, privateOwnerBuilder);

                foreach (var arg in cp.Arguments)
                {
                    EmitExpression(arg);
                    EnsureBoxed();
                }

                EmitPrivateCallUndefinedPadding(cp.Arguments.Count, instanceMethod!.GetParameters().Length);
                IL.Emit(OpCodes.Callvirt, instanceMethod!);
                SetStackUnknown();
                return;
            }
            else
            {
                // No private field storage — skip brand check
                EmitExpression(cp.Object);
                EnsureBoxed();
                if (Ctx.ClassRegistry.TryGetClass(className, out var privateOwnerBuilder)
                    && privateOwnerBuilder != null)
                    IL.Emit(OpCodes.Castclass, privateOwnerBuilder);

                foreach (var arg in cp.Arguments)
                {
                    EmitExpression(arg);
                    EnsureBoxed();
                }

                EmitPrivateCallUndefinedPadding(cp.Arguments.Count, instanceMethod!.GetParameters().Length);
                IL.Emit(OpCodes.Callvirt, instanceMethod!);
                SetStackUnknown();
                return;
            }
        }

        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!,
            $"Private method '#{methodName}' not found in class '{className}'");
    }

    /// <summary>
    /// Pads the evaluation stack with <c>undefined</c> sentinels for any trailing parameters a
    /// private method declares but the call omits. Private methods are emitted with a fixed,
    /// all-<c>object</c> signature (see <c>ILCompiler.Classes.cs</c>), so a call supplying fewer
    /// arguments than the method declares would otherwise leave <c>call</c>/<c>callvirt</c> short
    /// of operands (StackUnderflow → <c>InvalidProgramException</c>). The padded slots read as
    /// <c>undefined</c> in the body and fire any default-parameter prologue
    /// (<see cref="ILEmitter.EmitDefaultParameters"/>). Mirrors the undefined-padding that
    /// <c>$TSFunction.AdjustArgs</c> applies on the value-call path. (#696)
    /// </summary>
    protected void EmitPrivateCallUndefinedPadding(int argCount, int paramCount)
    {
        for (int i = argCount; i < paramCount; i++)
            EmitUndefinedConstant();
    }

    /// <summary>
    /// Emits a template literal. Default uses two-phase pattern (eval to temps, then build string)
    /// which is safe across await/yield boundaries.
    /// ILEmitter overrides with array-based ConcatTemplate approach.
    /// </summary>
    protected virtual void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        if (tl.Strings.Count == 0 && tl.Expressions.Count == 0)
        {
            IL.Emit(OpCodes.Ldstr, "");
            SetStackType(StackType.String);
            return;
        }

        // Phase 1: Evaluate all expressions to temps (awaits/yields happen here).
        // SpillBoxed registers each temp so an await in a later interpolation persists
        // the earlier ones across the suspension (#400).
        var exprTemps = new List<LocalBuilder>();
        for (int i = 0; i < tl.Expressions.Count; i++)
            exprTemps.Add(SpillBoxed(tl.Expressions[i]));

        // Phase 2: Build string from temps (no awaits, stack safe)
        IL.Emit(OpCodes.Ldstr, tl.Strings[0]);

        for (int i = 0; i < exprTemps.Count; i++)
        {
            IL.Emit(OpCodes.Ldloc, exprTemps[i]);
            // StringifyCoerce: interpolation is an implicit ToString coercion —
            // Symbol parts throw TypeError (ECMA-262 §7.1.17).
            IL.Emit(OpCodes.Call, Ctx.Runtime!.StringifyCoerce);
            IL.Emit(OpCodes.Call, Types.StringConcat2);

            if (i + 1 < tl.Strings.Count)
            {
                IL.Emit(OpCodes.Ldstr, tl.Strings[i + 1]);
                IL.Emit(OpCodes.Call, Types.StringConcat2);
            }
        }
        SetStackType(StackType.String);
    }

    /// <summary>
    /// Emits a tagged template literal. Default uses two-phase pattern.
    /// ILEmitter overrides with its own implementation.
    /// </summary>
    protected virtual void EmitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral ttl)
    {
        // Phase 1: Evaluate tag and all expressions to temps. SpillBoxed registers each
        // so an await in a later interpolation persists the tag and earlier expressions
        // across the suspension (#400).
        var tagTemp = SpillBoxed(ttl.Tag);

        var exprTemps = new List<LocalBuilder>();
        for (int i = 0; i < ttl.Expressions.Count; i++)
            exprTemps.Add(SpillBoxed(ttl.Expressions[i]));

        // Phase 2: Build arrays and call from temps
        IL.Emit(OpCodes.Ldloc, tagTemp);

        IL.Emit(OpCodes.Ldc_I4, ttl.CookedStrings.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < ttl.CookedStrings.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            if (ttl.CookedStrings[i] != null)
                IL.Emit(OpCodes.Ldstr, ttl.CookedStrings[i]!);
            else
                IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        IL.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        IL.Emit(OpCodes.Newarr, typeof(string));
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        IL.Emit(OpCodes.Ldc_I4, ttl.Expressions.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < exprTemps.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, exprTemps[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        IL.Emit(OpCodes.Call, Ctx.Runtime!.InvokeTaggedTemplate);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits an array literal with spread support. Default handles simple arrays and spread-concatenation.
    /// ILEmitter overrides with stack-type-tracked array/concat APIs.
    /// </summary>
    protected virtual void EmitArrayLiteral(Expr.ArrayLiteral a)
    {
        if (Ctx.TypeMap?.IsStablePrimitivePromiseAllInputInitializer(a) == true)
        {
            IL.Emit(OpCodes.Newobj, Types.GetDefaultConstructor(Types.ListOfDouble));
            SetStackUnknown();
            return;
        }

        bool hasSpreads = a.Elements.Any(e => e is Expr.Spread);

        // Evaluate all elements into locals first so an await inside any element
        // suspends with an empty stack (holes need no evaluation).
        var elementLocals = new LocalBuilder?[a.Elements.Count];
        for (int i = 0; i < a.Elements.Count; i++)
        {
            if (a.IsHole(i))
                continue;
            elementLocals[i] = SpillBoxed(a.Elements[i] is Expr.Spread spread ? spread.Expression : a.Elements[i]);
        }

        if (!hasSpreads)
        {
            IL.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            IL.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                if (elementLocals[i] == null)
                {
                    // Elided position → true ECMA-262 hole, not an undefined element.
                    IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.ArrayHoleInstance);
                }
                else
                {
                    IL.Emit(OpCodes.Ldloc, elementLocals[i]!);
                }
                IL.Emit(OpCodes.Stelem_Ref);
            }

            IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateArray);
        }
        else
        {
            IL.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            IL.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);

                if (a.Elements[i] is Expr.Spread)
                {
                    IL.Emit(OpCodes.Ldloc, elementLocals[i]!);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_I4, 1);
                    IL.Emit(OpCodes.Newarr, typeof(object));
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4_0);
                    if (elementLocals[i] == null)
                    {
                        IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.ArrayHoleInstance);
                    }
                    else
                    {
                        IL.Emit(OpCodes.Ldloc, elementLocals[i]!);
                    }
                    IL.Emit(OpCodes.Stelem_Ref);
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateArray);
                }

                IL.Emit(OpCodes.Stelem_Ref);
            }

            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.SymbolIterator);
            IL.Emit(OpCodes.Ldtoken, Ctx.Runtime!.RuntimeType);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.ConcatArrays);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits an object literal with support for spreads, computed keys, and accessors.
    /// ILEmitter overrides with stack-type-tracked implementation.
    /// </summary>
    protected virtual void EmitObjectLiteral(Expr.ObjectLiteral o)
    {
        bool hasSpreads = o.Properties.Any(p => p.IsSpread);
        bool hasComputedKeys = o.Properties.Any(p => p.Key is Expr.ComputedKey);
        bool hasAccessors = o.Properties.Any(p => p.Kind is Expr.ObjectPropertyKind.Getter or Expr.ObjectPropertyKind.Setter);

        if (hasAccessors)
        {
            EmitObjectLiteralWithAccessors(o);
        }
        else if (!hasSpreads && !hasComputedKeys)
        {
            // Spill values first so an await inside any value suspends with an empty stack.
            var valueLocals = o.Properties.Select(p => SpillBoxed(p.Value)).ToList();

            IL.Emit(OpCodes.Newobj, Types.DictionaryStringObjectCtor);

            for (int i = 0; i < o.Properties.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                EmitStaticPropertyKey(o.Properties[i].Key!);
                IL.Emit(OpCodes.Ldloc, valueLocals[i]);
                IL.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectSetItem);
            }

            IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateObject);
        }
        else
        {
            // Spill computed keys and values first (evaluation order: key before value, per property).
            var keyLocals = new LocalBuilder?[o.Properties.Count];
            var valueLocals = new LocalBuilder[o.Properties.Count];
            for (int i = 0; i < o.Properties.Count; i++)
            {
                if (o.Properties[i].Key is Expr.ComputedKey ck)
                    keyLocals[i] = SpillBoxed(ck.Expression);
                valueLocals[i] = SpillBoxed(o.Properties[i].Value);
            }

            IL.Emit(OpCodes.Newobj, Types.DictionaryStringObjectNullableCtor);

            for (int i = 0; i < o.Properties.Count; i++)
            {
                var prop = o.Properties[i];
                IL.Emit(OpCodes.Dup);

                if (prop.IsSpread)
                {
                    IL.Emit(OpCodes.Ldloc, valueLocals[i]);
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.MergeIntoObject);
                }
                else if (prop.Key is Expr.ComputedKey)
                {
                    IL.Emit(OpCodes.Ldloc, keyLocals[i]!);
                    IL.Emit(OpCodes.Ldloc, valueLocals[i]);
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.SetIndex);
                }
                else
                {
                    EmitStaticPropertyKey(prop.Key!);
                    IL.Emit(OpCodes.Ldloc, valueLocals[i]);
                    IL.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectNullableSetItem);
                }
            }

            IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateObject);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits an object literal with getter/setter accessors using the $Object type.
    /// </summary>
    protected virtual void EmitObjectLiteralWithAccessors(Expr.ObjectLiteral o)
    {
        IL.Emit(OpCodes.Newobj, Types.DictionaryStringObjectNullableCtor);
        IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSObjectCtor);

        var objLocal = IL.DeclareLocal(Ctx.Runtime!.TSObjectType);
        IL.Emit(OpCodes.Stloc, objLocal);

        foreach (var prop in o.Properties)
        {
            // Spill the value first so an await inside it suspends with an empty stack.
            var valueLocal = SpillBoxed(prop.Value);

            if (prop.IsSpread)
            {
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Ldloc, valueLocal);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.MergeIntoTSObject);
                continue;
            }

            string propKey = GetPropertyKeyString(prop.Key!);

            IL.Emit(OpCodes.Ldloc, objLocal);
            IL.Emit(OpCodes.Ldstr, propKey);
            IL.Emit(OpCodes.Ldloc, valueLocal);

            switch (prop.Kind)
            {
                case Expr.ObjectPropertyKind.Getter:
                    IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSObjectDefineGetter);
                    break;

                case Expr.ObjectPropertyKind.Setter:
                    IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSObjectDefineSetter);
                    break;

                default:
                    IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSObjectSetProperty);
                    break;
            }
        }

        IL.Emit(OpCodes.Ldloc, objLocal);
        SetStackUnknown();
    }

    /// <summary>
    /// Extracts the string key from a property key expression.
    /// </summary>
    protected static string GetPropertyKeyString(Expr.PropertyKey key)
    {
        return key switch
        {
            Expr.IdentifierKey ik => ik.Name.Lexeme,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING => (string)lk.Literal.Literal!,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER => lk.Literal.Literal!.ToString()!,
            Expr.ComputedKey => throw new Diagnostics.Exceptions.CompileException("Computed keys not supported in accessor context"),
            _ => throw new Diagnostics.Exceptions.CompileException($"Unexpected property key type: {key.GetType().Name}")
        };
    }

    /// <summary>
    /// Emits a static property key (identifier, string literal, or number literal) as a string.
    /// </summary>
    protected virtual void EmitStaticPropertyKey(Expr.PropertyKey key)
    {
        switch (key)
        {
            case Expr.IdentifierKey ik:
                IL.Emit(OpCodes.Ldstr, ik.Name.Lexeme);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                IL.Emit(OpCodes.Ldstr, (string)lk.Literal.Literal!);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                IL.Emit(OpCodes.Ldstr, lk.Literal.Literal!.ToString()!);
                break;
            default:
                throw new Diagnostics.Exceptions.CompileException($"Unexpected static property key type: {key.GetType().Name}");
        }
    }

    /// <summary>
    /// Emits a new expression (constructor call). Default handles built-in, Intl, module-qualified,
    /// and user-class constructors with generic type support and Activator.CreateInstance fallback.
    /// ILEmitter overrides (60+ additional built-in cases, inner class constructors, enum constructors).
    /// </summary>
    protected virtual void EmitNew(Expr.New n)
    {
        var (namespaceParts, className) = ExtractQualifiedNameFromCallee(n.Callee);

        if (namespaceParts.Count == 0 && n.Callee is Expr.Variable && TryEmitBuiltInConstructor(className, n.Arguments))
            return;

        if (TryEmitIntlConstructor(namespaceParts, className, n.Arguments))
            return;

        if (TryEmitModuleQualifiedConstructor(namespaceParts, className, n.Arguments))
            return;

        EmitUserClassNew(namespaceParts, className, n);
    }

    /// <summary>
    /// Emits a user-class constructor call with generic type support, argument temps (safe across
    /// await/yield boundaries), and Activator.CreateInstance fallback.
    /// </summary>
    protected virtual void EmitUserClassNew(List<string> namespaceParts, string className, Expr.New n)
    {
        string resolvedClassName = ResolveClassNameForNew(namespaceParts, className);

        // Check class expression constructors (e.g., const C = class { }; new C())
        if (Ctx.VarToClassExpr != null &&
            Ctx.VarToClassExpr.TryGetValue(className, out var classExpr) &&
            Ctx.ClassExprConstructors != null &&
            Ctx.ClassExprConstructors.TryGetValue(classExpr, out var classExprCtor))
        {
            EmitClassExprConstruction(classExprCtor, n);
            return;
        }

        var ctorBuilder = Ctx.ClassRegistry?.GetConstructorByQualifiedName(resolvedClassName);
        if (Ctx.Classes.TryGetValue(resolvedClassName, out var typeBuilder) && ctorBuilder != null)
        {
            Type targetType = typeBuilder;
            ConstructorInfo targetCtor = ctorBuilder;

            // Handle generic class instantiation
            if (n.TypeArgs != null && n.TypeArgs.Count > 0 &&
                Ctx.ClassRegistry!.GetGenericParams(resolvedClassName) != null)
            {
                Type[] typeArgs = n.TypeArgs.Select(ResolveTypeArg).ToArray();
                targetType = EmitGenerics.MakeGenericType(typeBuilder, typeArgs);
                targetCtor = EmitterTypeHelpers.ResolveConstructor(targetType, ctorBuilder);
            }

            var ctorParams = ctorBuilder.GetParameters();
            int expectedParamCount = ctorParams.Length;

            // Pre-evaluate arguments to temps (safe across await/yield boundaries)
            List<LocalBuilder> argTemps = [];
            foreach (var arg in n.Arguments)
            {
                EmitExpression(arg);
                EnsureBoxed();
                var temp = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Stloc, temp);
                argTemps.Add(temp);
            }

            PublishConstructorArgumentsIfNeeded(targetCtor, argTemps);

            // Load only arguments represented by the fixed CLR signature.
            // JavaScript surplus arguments were evaluated above and, when the
            // constructor observes `arguments`, published as the exact caller list.
            for (int i = 0; i < Math.Min(argTemps.Count, ctorParams.Length); i++)
            {
                IL.Emit(OpCodes.Ldloc, argTemps[i]);
                if (i < ctorParams.Length)
                {
                    var targetParamType = ctorParams[i].ParameterType;
                    if (targetParamType.IsValueType && targetParamType != typeof(object))
                    {
                        IL.Emit(OpCodes.Unbox_Any, targetParamType);
                    }
                }
            }

            // Pad omitted trailing arguments (object slot → `undefined` sentinel). (#739/#705)
            for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            {
                EmitOmittedArgument(ctorParams[i].ParameterType);
            }

            IL.Emit(OpCodes.Newobj, targetCtor);
            SetStackUnknown();
        }
        else
        {
            // Dynamic-callee fallback (#224): the callee is not a known class
            // name, so evaluate it as a VALUE (aliased built-in constructor,
            // namespace-singleton member like `I.NumberFormat`, arbitrary
            // expression) and construct through ConstructDynamicValue, which
            // dispatches Type → Activator, callable → InvokeValue, and throws
            // TypeError for non-constructables. Replaces the old behavior of
            // silently yielding null when the name didn't resolve.
            EmitExpression(n.Callee);
            EnsureBoxed();
            var ctorTemp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, ctorTemp);

            List<LocalBuilder> argTemps = [];
            foreach (var arg in n.Arguments)
            {
                EmitExpression(arg);
                EnsureBoxed();
                var temp = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Stloc, temp);
                argTemps.Add(temp);
            }

            IL.Emit(OpCodes.Ldloc, ctorTemp);
            IL.Emit(OpCodes.Ldc_I4, n.Arguments.Count);
            IL.Emit(OpCodes.Newarr, Ctx.Types.Object);

            for (int i = 0; i < argTemps.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldloc, argTemps[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }

            IL.Emit(OpCodes.Call, Ctx.Runtime!.ConstructDynamicValue);
            SetStackUnknown();
        }
    }

    /// <summary>
    /// Resolves a TypeScript type argument name to a CLR type (stub — all map to object).
    /// </summary>
    protected virtual Type ResolveTypeArg(string typeArg) => typeof(object);

    /// <summary>
    /// Resolves class name considering namespace imports and imported class aliases.
    /// Overridden by ILEmitter for additional resolution paths (external types, etc.).
    /// </summary>
    protected virtual string ResolveClassNameForNew(List<string> namespaceParts, string className)
    {
        if (namespaceParts.Count > 0)
        {
            string nsAlias = namespaceParts[0];
            if (Ctx.NamespaceImports?.TryGetValue(nsAlias, out var modulePath) == true)
            {
                if (Ctx.ExportedClasses?.TryGetValue(modulePath, out var exportedClasses) == true &&
                    exportedClasses.TryGetValue(className, out var qualifiedName))
                    return qualifiedName;
            }

            string nsPath = string.Join("_", namespaceParts);
            return $"{nsPath}_{className}";
        }

        if (Ctx.ImportedClassAliases?.TryGetValue(className, out var importedClassName) == true)
            return importedClassName;

        return Ctx.ResolveClassName(className);
    }

    /// <summary>
    /// Emits class expression construction with argument temps (safe across await/yield).
    /// </summary>
    private void EmitClassExprConstruction(ConstructorBuilder classExprCtor, Expr.New n)
    {
        var ctorParams = classExprCtor.GetParameters();
        int expectedParamCount = ctorParams.Length;

        // Pre-evaluate arguments to temps (safe across await/yield boundaries)
        List<LocalBuilder> argTemps = [];
        foreach (var arg in n.Arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        PublishConstructorArgumentsIfNeeded(classExprCtor, argTemps);

        for (int i = 0; i < Math.Min(argTemps.Count, ctorParams.Length); i++)
        {
            IL.Emit(OpCodes.Ldloc, argTemps[i]);
            if (i < ctorParams.Length)
            {
                var targetParamType = ctorParams[i].ParameterType;
                if (targetParamType.IsValueType && targetParamType != typeof(object))
                {
                    IL.Emit(OpCodes.Unbox_Any, targetParamType);
                }
            }
        }

        for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            EmitOmittedArgument(ctorParams[i].ParameterType);

        IL.Emit(OpCodes.Newobj, classExprCtor);
        SetStackUnknown();
    }

    private void PublishConstructorArgumentsIfNeeded(
        MethodBase constructor,
        IReadOnlyList<LocalBuilder> argumentTemps)
    {
        if (Ctx.MethodsCapturingArguments?.Contains(constructor) != true
            || Ctx.Runtime?.CurrentArgumentsField == null)
        {
            return;
        }

        IL.Emit(OpCodes.Ldc_I4, argumentTemps.Count);
        IL.Emit(OpCodes.Newarr, Types.Object);
        for (int i = 0; i < argumentTemps.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, argumentTemps[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }
        IL.Emit(OpCodes.Stsfld, Ctx.Runtime.CurrentArgumentsField);
    }

    /// <summary>
    /// Emits an arrow function. Default looks up in ArrowMethods and creates TSFunction.
    /// ILEmitter overrides (display class, non-capturing optimization).
    /// AsyncMoveNextEmitter overrides (async arrow dispatch, display class capture).
    /// AsyncArrowMoveNextEmitter overrides (nested async arrows).
    /// </summary>
    protected virtual void EmitArrowFunction(Expr.ArrowFunction af)
    {
        if (Ctx.ArrowMethods == null || !Ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            IL.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        // Capturing arrow: its body is emitted as an instance method on a display class, so the
        // $TSFunction must be bound to a freshly-constructed display-class instance whose fields
        // hold the captured values. Emitting a null target here (the old behaviour) produced
        // "Non-static method requires a target" when the arrow was later invoked (#435/#669,
        // sync generator bodies). Captured values are read through the GetHoistedVariableField /
        // GetThisField hooks plus Ctx.Locals, which every emitter that reaches this base default
        // already implements. GeneratorMoveNextEmitter is currently the only such emitter — all
        // others (ILEmitter, Async/AsyncArrow/AsyncGenerator MoveNext) override EmitArrowFunction.
        if (Ctx.DisplayClasses?.ContainsKey(af) == true &&
            Ctx.DisplayClassConstructors?.TryGetValue(af, out var displayCtor) == true)
        {
            EmitCapturingArrowViaHooks(af, method, displayCtor);
            return;
        }

        // Non-capturing arrow: static method, null target.
        // The helper's trailing Castclass is load-bearing here: GetMethodFromHandle
        // returns MethodBase while $TSFunctionCtor expects MethodInfo, and without
        // it a path leading here from a state-machine label with a different stack
        // state trips ILVerify PathStackDepth into an InvalidProgramException.
        IL.Emit(OpCodes.Ldnull);
        Types.EmitLoadMethodInfoViaHandle(IL, method);
        IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a capturing arrow as a $TSFunction bound to a new display-class instance, populating
    /// the display class's captured fields from the emitter's storage (hoisted state-machine
    /// fields, <c>this</c>, or IL locals). Mirrors the per-emitter overrides in
    /// AsyncMoveNextEmitter / AsyncGeneratorMoveNextEmitter, but resolves storage through the
    /// shared GetHoistedVariableField / GetThisField hooks so a single implementation serves any
    /// emitter that uses the base <see cref="EmitArrowFunction"/>.
    /// </summary>
    /// <summary>
    /// Pivots the SOURCE name a capturing arrow reads its display-class field from (#767). When a
    /// nested-block <c>let</c>/<c>const</c> that shadows an enclosing binding is renamed to its own
    /// storage and an inner arrow captures it, the arrow's field must be populated from that storage,
    /// not the outer same-named binding. The default is identity (no shadow); GeneratorMoveNextEmitter
    /// overrides it. The async / async-generator / async-arrow emitters apply the same pivot inline via
    /// <see cref="PivotCaptureSource"/> in their own capture-population loops.
    /// </summary>
    protected virtual string ResolveCaptureSourceName(Expr.ArrowFunction af, string capturedVar) => capturedVar;

    /// <summary>
    /// Looks up the renamed generator storage for <paramref name="capturedVar"/> as captured by
    /// <paramref name="af"/> in <paramref name="captureRenames"/> (#767), or returns the name unchanged.
    /// Shared by every state-machine capture-population path.
    /// </summary>
    protected static string PivotCaptureSource(
        IReadOnlyDictionary<object, IReadOnlyDictionary<string, string>>? captureRenames,
        Expr.ArrowFunction af, string capturedVar) =>
        captureRenames != null
        && captureRenames.TryGetValue(af, out var names)
        && names.TryGetValue(capturedVar, out var storage)
            ? storage : capturedVar;

    private void EmitCapturingArrowViaHooks(Expr.ArrowFunction af, MethodBuilder method, ConstructorBuilder displayCtor)
    {
        EmitCapturingArrowDisplayViaHooks(af, displayCtor);
        Types.EmitLoadMethodInfoViaHandle(IL, method);
        IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    private void EmitCapturingArrowDisplayViaHooks(
        Expr.ArrowFunction af,
        ConstructorBuilder displayCtor)
    {
        IL.Emit(OpCodes.Newobj, displayCtor);

        // Thread the entry-point display class into the arrow's $entryPointDC field so the arrow
        // reads captured TOP-LEVEL (module-level) variables through shared storage. The generator
        // body itself reads top-level vars live via the static entry-point DC field; an arrow
        // nested in that body needs the same reference, or dereferencing the (null) $entryPointDC
        // field throws a NullReferenceException when the arrow runs (#732). Mirrors the per-emitter
        // override in AsyncMoveNextEmitter; the static field is the only entry-point DC source
        // reachable from a generator MoveNext (no local, no parent-arrow DC field).
        if (Ctx.ArrowEntryPointDCFields?.TryGetValue(af, out var entryPointDCField) == true &&
            Ctx.EntryPointDisplayClassStaticField != null)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldsfld, Ctx.EntryPointDisplayClassStaticField);
            IL.Emit(OpCodes.Stfld, entryPointDCField);
        }

        // Thread the enclosing state machine's function display class into the arrow's $functionDC
        // field so the arrow reads/writes captured-and-mutated locals through shared storage rather
        // than a by-value snapshot — the write case that was previously rejected (#674). The arrow's
        // own snapshot fields (populated below) deliberately omit these vars (see the function-DC
        // skip in CollectAndDefineArrowFunctions), so the two paths don't both materialize them.
        if (Ctx.ArrowFunctionDCFields?.TryGetValue(af, out var arrowFunctionDCField) == true &&
            GetFunctionDCField() is FieldBuilder stateMachineFunctionDC)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, stateMachineFunctionDC);
            IL.Emit(OpCodes.Stfld, arrowFunctionDCField);
        }

        if (Ctx.DisplayClassFields?.TryGetValue(af, out var fieldMap) == true)
        {
            foreach (var (capturedVar, field) in fieldMap)
            {
                IL.Emit(OpCodes.Dup);

                // #767: a captured nested-block shadow was renamed to its own generator storage; source
                // the field from that storage rather than the outer same-named binding. The arrow's own
                // DC field (`field`) keeps the original name. Identity for non-shadow captures.
                var sourceVar = ResolveCaptureSourceName(af, capturedVar);

                // Per-iteration cell capture (#650): snapshot the StrongBox REFERENCE so the
                // closure shares this iteration's cell (the cell local is repointed to a fresh
                // box each iteration). Checked first; cell bindings are never hoisted fields.
                if (Ctx.CellBindingLocals.TryGetValue(capturedVar, out var cellLocal))
                {
                    IL.Emit(OpCodes.Ldloc, cellLocal);
                }
                // `this` is checked before the hoisted-variable map: the generator's
                // hoisting analyzer can mint a state-machine field keyed "this" that the
                // stub never populates, so resolving it via the map would snapshot null
                // (NRE when the arrow dereferences `this`). The real receiver lives in the
                // builder's dedicated ThisField (set by the instance-method stub).
                else if (capturedVar == "this" && GetThisField() is FieldBuilder thisField)
                {
                    IL.Emit(OpCodes.Ldarg_0);
                    IL.Emit(OpCodes.Ldfld, thisField);
                }
                else if (GetHoistedVariableField(sourceVar) is FieldBuilder hoistedField)
                {
                    IL.Emit(OpCodes.Ldarg_0);
                    IL.Emit(OpCodes.Ldfld, hoistedField);
                }
                else if (Ctx.Locals.TryGetLocal(sourceVar, out var local))
                {
                    IL.Emit(OpCodes.Ldloc, local);
                }
                else if (!TryEmitGlobalVariable(sourceVar))
                {
                    IL.Emit(OpCodes.Ldnull);
                }

                IL.Emit(OpCodes.Stfld, field);
            }
        }

    }

    // EmitCall is virtual with a default implementation in ExpressionEmitterBase.CallHelpers.cs
    // EmitCompoundAssign, EmitLogicalAssign, EmitPrefixIncrement, EmitPostfixIncrement
    // are implemented in ExpressionEmitterBase.Operators.cs as virtual methods.
    // ILEmitter and AsyncArrowMoveNextEmitter override with their own implementations.
    protected abstract void EmitClassExpression(Expr.ClassExpr ce);
    protected abstract void EmitDelete(Expr.Delete del);
    #endregion

    #region Global Variable Resolution Helpers

    /// <summary>
    /// Emits the JavaScript global constants that are not real bindings: <c>NaN</c>,
    /// <c>Infinity</c>, and <c>undefined</c>. Every variable-emission path must consult this
    /// before falling back to a null load — otherwise a bare <c>NaN</c>/<c>Infinity</c>
    /// reference compiles to a null load, so e.g. <c>NaN === NaN</c> silently degrades to
    /// <c>null === null</c> → <c>true</c> (that was the #648 async-arrow gap). Callers must run
    /// their local/parameter/capture resolver first so a same-named user binding still shadows
    /// the global, matching ECMA-262 lexical lookup.
    /// </summary>
    /// <returns><c>true</c> if <paramref name="name"/> is a global constant and was emitted.</returns>
    protected bool TryEmitJsGlobalConstant(string name)
    {
        switch (name)
        {
            case "NaN": EmitDoubleConstant(double.NaN); return true;
            case "Infinity": EmitDoubleConstant(double.PositiveInfinity); return true;
            case "undefined": EmitUndefinedConstant(); return true;
            default: return false;
        }
    }

    /// <summary>
    /// Tries to emit a global variable load (after resolver fails).
    /// Checks TopLevelStaticVars → Functions → NamespaceFields → CapturedTopLevelVars.
    /// </summary>
    protected virtual bool TryEmitGlobalVariable(string name)
    {
        // JavaScript global constants — must be checked before user-defined variables. State
        // machine emitters (Async/Generator/AsyncGenerator MoveNextEmitter) reach this via the
        // base EmitVariable; ILEmitter and AsyncArrowMoveNextEmitter call the helper directly.
        if (TryEmitJsGlobalConstant(name)) return true;

        if (Ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            Ctx.EmitTopLevelLexicalTdzCheck(IL, name);
            IL.Emit(OpCodes.Ldsfld, topLevelField);
            SetStackUnknown();
            return true;
        }

        // A captured top-level variable (a real let/const/var binding closed over by this
        // body) must be resolved BEFORE Functions: a variable binding in scope shadows a
        // same-named top-level function from another module. The EntryPointDisplayClassFields
        // guard means only genuine variable bindings stored on the display class match here;
        // function references aren't in that map and correctly fall through to Functions below.
        // Without this ordering, a generator capturing e.g. `const composeDoc = require(...)`
        // whose name collides with a cross-module `composeDoc` function resolved to the
        // function and threw "object is not a function" (#541).
        if (Ctx.CapturedTopLevelVars?.Contains(name) == true &&
            Ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var capturedEntryField) == true &&
            Ctx.EntryPointDisplayClassStaticField != null)
        {
            Ctx.EmitTopLevelLexicalTdzCheck(IL, name);
            IL.Emit(OpCodes.Ldsfld, Ctx.EntryPointDisplayClassStaticField);
            IL.Emit(OpCodes.Ldfld, capturedEntryField);
            SetStackUnknown();
            return true;
        }

        if (Ctx.Functions.TryGetValue(Ctx.ResolveFunctionName(name), out var funcMethod))
        {
            // Stage 6r: route through GetOrCreate (MethodInfo-keyed instance cache)
            // so multiple references to the same function decl produce the SAME
            // $TSFunction wrapper. Mirrors ILEmitter.Expressions.cs.
            IL.Emit(OpCodes.Ldtoken, funcMethod);
            if (Ctx.ProgramType != null)
            {
                IL.Emit(OpCodes.Ldtoken, Ctx.ProgramType);
                IL.Emit(OpCodes.Call, Types.MethodBaseGetMethodFromHandleWithType);
            }
            else
            {
                IL.Emit(OpCodes.Call, Types.MethodBaseGetMethodFromHandle);
            }
            IL.Emit(OpCodes.Castclass, typeof(MethodInfo));
            int arity = Ctx.GetFunctionLength(funcMethod);
            IL.Emit(OpCodes.Ldstr, Ctx.GetFunctionName(funcMethod, name));
            IL.Emit(OpCodes.Ldc_I4, arity);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.TSFunctionGetOrCreate);
            SetStackUnknown();
            return true;
        }

        // ResolveNamespaceField walks enclosing namespace prefixes so a nested namespace's member
        // body can name a sibling/enclosing namespace by its simple name (#665). Shared with
        // ILEmitter's sync path so state-machine bodies resolve namespaces identically.
        if (Ctx.ResolveNamespaceField(name) is { } nsField)
        {
            IL.Emit(OpCodes.Ldsfld, nsField);
            SetStackUnknown();
            return true;
        }

        // User class identifiers used as values: emit the class's Type token
        // (the same representation ILEmitter's sync path produces) so
        // `x instanceof MyClass` works inside state-machine bodies. Before
        // the built-in arms so a user class shadowing a built-in name wins.
        if (Ctx.Classes.TryGetValue(Ctx.ResolveClassName(name), out var userClassType))
        {
            IL.Emit(OpCodes.Ldtoken, userClassType);
            IL.Emit(OpCodes.Call, Types.GetMethod(Types.Type, "GetTypeFromHandle", Types.RuntimeTypeHandle));
            SetStackUnknown();
            return true;
        }

        // Built-in constructor identifiers used as values (#232): without these
        // arms, state-machine bodies (async/generator MoveNext emitters, which
        // don't run ILEmitter's EmitVariable) emit null for `Error`, `Date`,
        // `Map`, … — so `e instanceof Error` inside an async function was
        // always false. Positioned after the user-variable checks so local
        // shadowing wins, mirroring ILEmitter's resolution order.
        if (TryEmitErrorTypeToken(name)) return true;
        if (TryEmitBuiltInClassType(name)) return true;
        if (TryEmitNamespaceSingleton(name)) return true;

        return false;
    }

    /// <summary>
    /// AbortSignal / Intl in value position (#224) — resolve to the lazily
    /// populated namespace singleton dicts. Direct static forms
    /// (`AbortSignal.abort(x)`, `new Intl.NumberFormat(...)`) are still
    /// intercepted at compile time before the receiver is evaluated, so these
    /// arms only serve aliasing/typeof/argument-passing uses. Shared by
    /// ILEmitter.EmitVariable and the state-machine emitters' global path.
    /// </summary>
    protected bool TryEmitNamespaceSingleton(string name)
    {
        if (name == "AbortSignal" && Ctx.Runtime!.AbortSignalNamespacePopulate != null)
        {
            IL.Emit(OpCodes.Call, Ctx.Runtime!.AbortSignalNamespacePopulate);
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.AbortSignalNamespaceField!);
            SetStackUnknown();
            return true;
        }

        if (name == "Intl" && Ctx.Runtime!.IntlNamespacePopulate != null)
        {
            IL.Emit(OpCodes.Call, Ctx.Runtime!.IntlNamespacePopulate);
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.IntlNamespaceField!);
            SetStackUnknown();
            return true;
        }

        if (name == "Reflect" && Ctx.Runtime!.ReflectSingletonPopulateMethod != null)
        {
            IL.Emit(OpCodes.Call, Ctx.Runtime.ReflectSingletonPopulateMethod);
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime.ReflectSingletonField!);
            SetStackUnknown();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves a built-in Error constructor name (Error, TypeError, …,
    /// AggregateError) by emitting the matching emitted-runtime Type token.
    /// The InstanceOf runtime helper uses Type.IsAssignableFrom against it.
    /// </summary>
    protected bool TryEmitErrorTypeToken(string name)
    {
        if (!Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(name)) return false;
        var errorType = name switch
        {
            "Error" => Ctx.Runtime!.TSErrorType,
            "TypeError" => Ctx.Runtime!.TSTypeErrorType,
            "RangeError" => Ctx.Runtime!.TSRangeErrorType,
            "ReferenceError" => Ctx.Runtime!.TSReferenceErrorType,
            "SyntaxError" => Ctx.Runtime!.TSSyntaxErrorType,
            "URIError" => Ctx.Runtime!.TSURIErrorType,
            "EvalError" => Ctx.Runtime!.TSEvalErrorType,
            "AggregateError" => Ctx.Runtime!.TSAggregateErrorType,
            _ => Ctx.Runtime!.TSErrorType
        };
        IL.Emit(OpCodes.Ldtoken, errorType);
        IL.Emit(OpCodes.Call, Types.GetMethod(Types.Type, "GetTypeFromHandle", Types.RuntimeTypeHandle));
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Resolves a bare reference to a built-in global class name (Date, Map,
    /// Set, etc.) by emitting the matching .NET Type token. The InstanceOf
    /// runtime helper then uses Type.IsAssignableFrom to check membership
    /// against `new X()` instances, so `instanceof Date` works whether the
    /// user is in script code or inside an embedded stdlib module.
    /// </summary>
    protected bool TryEmitBuiltInClassType(string name)
    {
        Type? t = name switch
        {
            "Date" => Ctx.Runtime!.TSDateType,
            "RegExp" => Ctx.Runtime!.TSRegExpType,
            "TextEncoder" => Ctx.Runtime!.TSTextEncoderType,
            "TextDecoder" => Ctx.Runtime!.TSTextDecoderType,
            "Buffer" => Ctx.Runtime!.TSBufferType,
            "Array" => Types.IListOfObject, // covers both List<object> and $Array
            "Map" => Types.DictionaryObjectObject,
            "Set" => Types.HashSetOfObject,
            "WeakMap" => Types.ConditionalWeakTableObjectObject,
            "WeakSet" => Types.ConditionalWeakTableObjectObject,
            // WeakRef and FinalizationRegistry are backed by BCL containers in
            // standalone output.  Their Type tokens provide the constructor
            // object in value position (typeof/isConstructor/prototype), while
            // `new` remains routed through the dedicated emitted factories.
            "WeakRef" => Types.WeakReferenceObject,
            "FinalizationRegistry" => Types.ObjectArray,
            "Promise" => Types.TaskOfObject,
            "Function" => Ctx.Runtime!.TSFunctionType,
            // Symbol (#234): the value-form $TSSymbol token. ILEmitter handles
            // bare Symbol in its own pseudo-variable arm; this entry covers the
            // state-machine emitters that resolve through this base path.
            "Symbol" => Ctx.Runtime!.TSSymbolType,
            // Pure-IL web-streams types (#224) — TypeBuilders are null when
            // UsesWebStreams is off, which falls through to ThrowUndefinedVariable.
            "ReadableStream" => Ctx.Runtime!.ReadableStreamType,
            "WritableStream" => Ctx.Runtime!.WritableStreamType,
            "TransformStream" => Ctx.Runtime!.TransformStreamType,
            // Real emitted types since #222 (always emitted).
            "MessageChannel" => Ctx.Runtime!.TSMessageChannelType,
            "MessagePort" => Ctx.Runtime!.TSMessagePortType,
            // Typed-array / buffer constructors in value position (#331) — the
            // bare-identifier form (`var x = Int8Array`, `typeof Uint8Array`,
            // `x instanceof ArrayBuffer`). `new Int8Array(...)` is intercepted
            // earlier as a New expression; these tokens only serve value uses.
            // Null (feature off) falls through to ThrowUndefinedVariable, exactly
            // like the web-streams entries above — and the same bare identifier
            // that reaches here flags HasAnyTypedArray, so the type is emitted.
            "ArrayBuffer" => Ctx.Runtime!.ArrayBufferType,
            "SharedArrayBuffer" => Ctx.Runtime!.SharedArrayBufferType,
            "DataView" => Ctx.Runtime!.DataViewType,
            "Int8Array" => Ctx.Runtime!.Int8ArrayType,
            "Uint8Array" => Ctx.Runtime!.Uint8ArrayType,
            "Uint8ClampedArray" => Ctx.Runtime!.Uint8ClampedArrayType,
            "Int16Array" => Ctx.Runtime!.Int16ArrayType,
            "Uint16Array" => Ctx.Runtime!.Uint16ArrayType,
            "Int32Array" => Ctx.Runtime!.Int32ArrayType,
            "Uint32Array" => Ctx.Runtime!.Uint32ArrayType,
            "Float32Array" => Ctx.Runtime!.Float32ArrayType,
            "Float64Array" => Ctx.Runtime!.Float64ArrayType,
            "BigInt64Array" => Ctx.Runtime!.BigInt64ArrayType,
            "BigUint64Array" => Ctx.Runtime!.BigUint64ArrayType,
            _ => null
        };
        if (t == null) return false;
        IL.Emit(OpCodes.Ldtoken, t);
        IL.Emit(OpCodes.Call, Types.GetMethod(Types.Type, "GetTypeFromHandle", Types.RuntimeTypeHandle));
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Tries to emit a global variable store.
    /// Checks CapturedTopLevelVars → TopLevelStaticVars.
    /// Returns true if handled (value consumed from stack).
    /// </summary>
    protected virtual bool TryEmitGlobalStore(string name)
    {
        if (Ctx.CapturedTopLevelVars?.Contains(name) == true &&
            Ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true &&
            Ctx.EntryPointDisplayClassStaticField != null)
        {
            Ctx.EmitTopLevelLexicalTdzCheck(IL, name);
            var temp = IL.DeclareLocal(Types.Object);
            IL.Emit(OpCodes.Stloc, temp);
            IL.Emit(OpCodes.Ldsfld, Ctx.EntryPointDisplayClassStaticField);
            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, entryPointField);
            SetStackUnknown();
            return true;
        }

        if (Ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            Ctx.EmitTopLevelLexicalTdzCheck(IL, name);
            IL.Emit(OpCodes.Stsfld, topLevelField);
            SetStackUnknown();
            return true;
        }

        return false;
    }

    #endregion

    #region Static Field Access

    /// <summary>
    /// Tries to emit static field access for Class.field patterns.
    /// Default checks ClassRegistry for static fields.
    /// </summary>
    protected virtual bool TryEmitStaticFieldAccess(Expr.Get g)
    {
        if (g.Object is Expr.Variable classVar &&
            Ctx.Classes.TryGetValue(Ctx.ResolveClassName(classVar.Name.Lexeme), out var staticFieldClassBuilder))
        {
            string resolvedClassName = Ctx.ResolveClassName(classVar.Name.Lexeme);
            if (Ctx.ClassRegistry!.TryGetCallableStaticField(resolvedClassName, g.Name.Lexeme, staticFieldClassBuilder, out var staticField))
            {
                EmitStaticFieldLoadWithShadow(resolvedClassName, staticFieldClassBuilder, g.Name.Lexeme, staticField!);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Emits a static class method used as a value through <c>this.method</c>.
    /// Static CLR bodies have no arg0 receiver, so the generic property path cannot
    /// first evaluate <c>this</c>. Resolve the current declaration or class expression
    /// from compiler metadata and materialize its JavaScript function wrapper directly.
    /// </summary>
    protected bool TryEmitStaticClassMethodValue(Expr.Get g)
    {
        if (g.Object is not Expr.This || Ctx.IsInstanceMethod || Ctx.CurrentClassBuilder == null)
            return false;

        System.Reflection.MethodInfo? method = null;
        if (Ctx.CurrentClassName is { } className)
        {
            Ctx.ClassRegistry!.TryGetCallableStaticMethod(
                className, g.Name.Lexeme, Ctx.CurrentClassBuilder, out method);
        }

        if (method == null && Ctx.ClassExprStaticMethods != null)
        {
            Parsing.Expr.ClassExpr? classExpr = Ctx.CurrentClassExpr;
            if (classExpr == null && Ctx.ClassExprBuilders != null)
            {
                classExpr = Ctx.ClassExprBuilders
                    .FirstOrDefault(pair => ReferenceEquals(pair.Value, Ctx.CurrentClassBuilder))
                    .Key;
            }

            if (classExpr != null
                && Ctx.ClassExprStaticMethods.TryGetValue(classExpr, out var methods)
                && methods.TryGetValue(g.Name.Lexeme, out var classExprMethod))
            {
                method = classExprMethod;
            }
        }

        if (method == null)
            return false;

        IL.Emit(OpCodes.Ldnull);
        Types.EmitLoadMethodInfoViaHandle(IL, method);
        IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSFunctionCtor);
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Loads a static data field read through <paramref name="resolvedClassName"/>. For a field the
    /// class declares itself this is a plain <c>Ldsfld</c>. For an <em>inherited</em> static field
    /// (declared on a base, resolved via the superclass walk) a subclass write creates a per-subclass
    /// own shadow in $PropertyDescriptorStore keyed by the subclass Type — so the read first consults
    /// that store (walking the Type chain) and only falls back to the declaring base's field when no
    /// shadow exists. This matches interpreter / JS own-shadow semantics (issue #339). The shadow probe
    /// is emitted only on the inherited path, leaving own-field reads a single load.
    /// </summary>
    protected void EmitStaticFieldLoadWithShadow(string resolvedClassName, System.Reflection.Emit.TypeBuilder classBuilder, string fieldName, System.Reflection.FieldInfo resolvedField)
    {
        // Own field: no subclass shadow can exist, emit a direct load.
        if (Ctx.ClassRegistry!.TryGetOwnStaticField(resolvedClassName, fieldName, out _))
        {
            IL.Emit(OpCodes.Ldsfld, resolvedField);
            SetStackUnknown();
            return;
        }

        // Inherited field: probe for a runtime own-shadow on the subclass (or a nearer ancestor) first.
        var shadowLocal = IL.DeclareLocal(Ctx.Runtime!.CompiledPropertyDescriptorType);
        var noShadowLabel = IL.DefineLabel();
        var doneLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldtoken, classBuilder);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Ldstr, fieldName);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.PDSGetStaticShadow);
        IL.Emit(OpCodes.Stloc, shadowLocal);

        IL.Emit(OpCodes.Ldloc, shadowLocal);
        IL.Emit(OpCodes.Brfalse, noShadowLabel);
        IL.Emit(OpCodes.Ldloc, shadowLocal);
        IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.CompiledPropertyDescriptorValue.GetGetMethod()!);
        IL.Emit(OpCodes.Br, doneLabel);

        IL.MarkLabel(noShadowLabel);
        IL.Emit(OpCodes.Ldsfld, resolvedField);

        IL.MarkLabel(doneLabel);
        SetStackUnknown();
    }

    #endregion

    #region Virtual Methods - Comma operator
    /// <summary>
    /// Emits a comma (sequence) expression: evaluates left for side effects, discards its value, returns right.
    /// </summary>
    protected virtual void EmitComma(Expr.Comma c)
    {
        EmitExpression(c.Left);
        EnsureBoxed();
        IL.Emit(OpCodes.Pop);
        EmitExpression(c.Right);
        EnsureBoxed();
    }

    /// <summary>
    /// Emits an assignment-destructuring expression (#754) by running its lowered statements and then
    /// leaving the result value on the stack. Overridden in <see cref="StatementEmitterBase"/> (the
    /// concrete emitters' base), which has access to <c>EmitStatement</c>; declared here only so the
    /// shared <see cref="EmitExpression"/> dispatch can reach it.
    /// </summary>
    protected virtual void EmitDestructuringAssign(Expr.DestructuringAssign da) =>
        throw new NotSupportedException(
            "DestructuringAssign requires statement emission; emit it from a StatementEmitterBase subclass.");
    #endregion

    #region Virtual Methods - Pass-through expressions
    /// <summary>
    /// Emits a grouping expression by evaluating its inner expression.
    /// </summary>
    protected virtual void EmitGrouping(Expr.Grouping g) => EmitExpression(g.Expression);

    /// <summary>
    /// Evaluates a class heritage expression at the class definition's source
    /// position and discards its value. The emitted CLR base type is selected
    /// statically, but ECMAScript still requires the expression's side effects
    /// and abrupt completions to occur at runtime.
    /// </summary>
    protected void EmitClassHeritageExpression(Expr? heritage, string? innerClassName)
    {
        if (heritage == null)
            return;

        EmitClassHeritageValue(heritage, innerClassName);
        EnsureBoxed();
        IL.Emit(OpCodes.Pop);
    }

    private void EmitClassHeritageValue(Expr heritage, string? innerClassName)
    {
        switch (heritage)
        {
            case Expr.Grouping grouping:
                EmitClassHeritageValue(grouping.Expression, innerClassName);
                return;
            case Expr.Comma comma:
                EmitExpression(comma.Left);
                EnsureBoxed();
                IL.Emit(OpCodes.Pop);
                EmitClassHeritageValue(comma.Right, innerClassName);
                return;
            case Expr.Variable variable when innerClassName != null
                && variable.Name.Lexeme == innerClassName:
                // A class's inner name is in TDZ while its extends expression is
                // evaluated, even if an outer binding has the same spelling.
                IL.Emit(OpCodes.Ldstr, innerClassName);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.ThrowUndefinedVariable);
                IL.Emit(OpCodes.Ldnull); // unreachable stack balance
                SetStackUnknown();
                return;
            default:
                EmitExpression(heritage);
                return;
        }
    }

    /// <summary>
    /// Emits a type assertion. Type assertions are compile-time only, just emit the inner expression.
    /// </summary>
    protected virtual void EmitTypeAssertion(Expr.TypeAssertion ta) => EmitExpression(ta.Expression);

    /// <summary>
    /// Emits a satisfies expression. Satisfies is compile-time only, just emit the inner expression.
    /// </summary>
    protected virtual void EmitSatisfies(Expr.Satisfies sat) => EmitExpression(sat.Expression);

    /// <summary>
    /// Emits a non-null assertion. These are compile-time only, just emit the inner expression.
    /// </summary>
    protected virtual void EmitNonNullAssertion(Expr.NonNullAssertion nna) => EmitExpression(nna.Expression);

    /// <summary>
    /// Emits a spread expression. Spread is handled contextually in array/object literals.
    /// When encountered standalone, just emit the expression.
    /// </summary>
    protected virtual void EmitSpread(Expr.Spread sp) => EmitExpression(sp.Expression);

    /// <summary>
    /// Emits a regex literal as a <c>$RegExp</c> instance. Lives in the shared base (not just
    /// <see cref="ILEmitter"/>) so regex literals work identically in every emission context —
    /// plain functions, arrows, and the four state-machine bodies (async / async-arrow /
    /// generator / async-generator). It needs no sync-only state: the per-site hoist fields
    /// hang off the shared <see cref="EmittedRuntime.RegexHoistFields"/>, reachable everywhere
    /// (#1105 — previously the base pushed <c>null</c>, silently miscompiling every regex inside
    /// an async or generator body).
    /// </summary>
    protected virtual void EmitRegexLiteral(Expr.RegexLiteral re)
    {
        // Hoisted literal (RegexLiteralHoistAnalyzer flagged this exact node as
        // used in a safe consuming position): load the per-site static $RegExp,
        // constructing it once on first evaluation. This removes the
        // per-evaluation allocation + $RegExp ctor for literals in hot loops.
        // Lazy init (vs. a .cctor) preserves SyntaxError throw-on-evaluation for
        // an invalid pattern, and a rare concurrent double-construct just yields
        // an equivalent instance (we only hoist where lastIndex is irrelevant).
        if (Ctx.Runtime?.RegexHoistFields is { } hoistFields
            && hoistFields.TryGetValue(re, out var field))
        {
            var done = IL.DefineLabel();
            IL.Emit(OpCodes.Ldsfld, field);          // [field]
            IL.Emit(OpCodes.Dup);                    // [field, field]
            IL.Emit(OpCodes.Brtrue, done);           // non-null → done with [field]
            IL.Emit(OpCodes.Pop);                    // []
            IL.Emit(OpCodes.Ldstr, re.Pattern);
            IL.Emit(OpCodes.Ldstr, re.Flags);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateRegExpWithFlags); // [new]
            IL.Emit(OpCodes.Dup);                    // [new, new]
            IL.Emit(OpCodes.Stsfld, field);          // [new]
            IL.MarkLabel(done);                      // [regex] on both paths
            SetStackUnknown();
            return;
        }

        IL.Emit(OpCodes.Ldstr, re.Pattern);
        IL.Emit(OpCodes.Ldstr, re.Flags);
        EmitCallUnknown(Ctx.Runtime!.CreateRegExpWithFlags);
    }
    #endregion

    #region Virtual Methods - Helper delegations
    /// <summary>
    /// Emits a logical AND/OR expression with short-circuit evaluation.
    /// </summary>
    protected virtual void EmitLogical(Expr.Logical l)
    {
        bool isAnd = l.Operator.Type == TokenType.AND_AND;
        _helpers.EmitLogical(
            isAnd,
            () => { EmitExpression(l.Left); EnsureBoxed(); },
            () => { EmitExpression(l.Right); EnsureBoxed(); },
            Ctx.Runtime!.IsTruthy);
    }

    /// <summary>
    /// Emits a unary expression (-, !, typeof, ~).
    /// </summary>
    protected virtual void EmitUnary(Expr.Unary u)
    {
        switch (u.Operator.Type)
        {
            case TokenType.MINUS:
                _helpers.EmitUnaryMinus(() => EmitExpression(u.Right));
                break;
            case TokenType.PLUS:
                _helpers.EmitUnaryPlus(() => EmitExpression(u.Right));
                break;
            case TokenType.BANG:
                _helpers.EmitUnaryNot(() => EmitExpression(u.Right), Ctx.Runtime!.IsTruthy);
                break;
            case TokenType.TYPEOF:
                // typeof never throws on undeclared variables - returns "undefined"
                if (u.Right is Expr.Variable { Name.Lexeme: "Proxy" })
                {
                    _helpers.IL.Emit(OpCodes.Ldstr, "function");
                    _helpers.SetStackUnknown();
                }
                else if (u.Right is Expr.Variable tv && !IsKnownVariable(tv.Name.Lexeme))
                {
                    _helpers.IL.Emit(OpCodes.Ldstr, "undefined");
                    _helpers.SetStackUnknown();
                }
                // Error constructors are functions
                else if (u.Right is Expr.Variable ev
                    && Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(ev.Name.Lexeme))
                {
                    _helpers.IL.Emit(OpCodes.Ldstr, "function");
                    _helpers.SetStackUnknown();
                }
                // process.stdin/stdout/stderr are marker strings in compiled mode but should return "object"
                else if (u.Right is Expr.Get tg && tg.Object is Expr.Variable tgv
                    && tgv.Name.Lexeme == "process" && tg.Name.Lexeme is "stdin" or "stdout" or "stderr")
                {
                    _helpers.IL.Emit(OpCodes.Ldstr, "object");
                    _helpers.SetStackUnknown();
                }
                else
                {
                    _helpers.EmitUnaryTypeOf(() => EmitExpression(u.Right), Ctx.Runtime!.TypeOf);
                }
                break;
            case TokenType.TILDE:
                _helpers.EmitUnaryBitwiseNot(() => EmitExpression(u.Right));
                break;
            case TokenType.VOID:
                // void operator: evaluate the operand for its side effects, discard the
                // result, and yield undefined. EmitExpression handles any nested
                // yield/await suspension in the operand, so this works inside the
                // generator/async state machines that inherit this base implementation.
                EmitExpression(u.Right);
                EnsureBoxed();
                IL.Emit(OpCodes.Pop);
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);
                SetStackUnknown();
                break;
            default:
                throw new NotImplementedException($"Unary operator {u.Operator.Type} not implemented");
        }
    }

    /// <summary>
    /// Checks whether a variable name is known at compile time (without emitting IL).
    /// Used by typeof to return "undefined" for unknown variables instead of throwing.
    /// Checks context-level lookups (pseudo-variables, globals, classes, functions, etc).
    /// Subclasses should override to also check resolver-level variables.
    /// </summary>
    protected virtual bool IsKnownVariable(string name)
    {
        // Check resolver (parameters, locals, captured variables, state machine fields)
        if (Resolver.HasVariable(name)) return true;
        // Check pseudo-variables and global constants
        if (name is "Math" or "JSON" or "process" or "globalThis" or "global" or "Symbol" or "BigInt" or "NaN" or "Infinity"
            or "undefined" or "fetch" or "crypto" or "__filename" or "__dirname"
            or "parseFloat" or "parseInt" or "isNaN" or "isFinite"
            or "encodeURIComponent" or "decodeURIComponent"
            or "setTimeout" or "clearTimeout" or "setInterval" or "clearInterval"
            or "queueMicrotask" or "structuredClone") return true;
        if (Ctx.TopLevelStaticVars?.ContainsKey(name) == true) return true;
        if (Ctx.Classes.ContainsKey(Ctx.ResolveClassName(name))) return true;
        if (Ctx.Functions.ContainsKey(Ctx.ResolveFunctionName(name))) return true;
        if (Ctx.InnerFunctionMethodsByName?.ContainsKey(name) == true) return true;
        if (Ctx.NamespaceFields?.ContainsKey(name) == true) return true;
        if (Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(name)) return true;
        // Built-in constructor types (Array/Date/Map/Set/RegExp/Promise/Buffer/etc.) —
        // without this, `typeof Array` returns `"undefined"` in compiled mode even though
        // these names resolve fine everywhere else via TryEmitBuiltInClassType.
        if (name is "Array" or "Date" or "RegExp" or "Map" or "Set"
            or "WeakMap" or "WeakSet" or "WeakRef" or "FinalizationRegistry"
            or "Promise" or "Buffer" or "Function"
            or "Object" or "String" or "Number" or "Boolean"
            or "TextEncoder" or "TextDecoder") return true;
        // Typed-array / buffer constructors as values (#331) — without this,
        // `typeof Int8Array` is "undefined" though TryEmitBuiltInClassType
        // resolves them. The same identifier flags HasAnyTypedArray, so the
        // backing type is emitted.
        if (name is "ArrayBuffer" or "SharedArrayBuffer" or "DataView"
            || Runtime.BuiltIns.BuiltInNames.IsTypedArrayName(name)) return true;
        // CJS magic names visible in CJS bodies: `module` is registered in TopLevelStaticVars
        // so the earlier check catches it; `exports` and `require` are handled by special
        // emitter paths (TryEmitCjsVariable, TryEmitCjsRequireCall) — without this,
        // `typeof exports === 'object'` short-circuits to `"undefined"`.
        if (InCjsContext && (name is "exports" or "require")) return true;
        return false;
    }

    /// <summary>
    /// Emits a ternary conditional expression (condition ? thenBranch : elseBranch).
    /// </summary>
    protected virtual void EmitTernary(Expr.Ternary t)
    {
        _helpers.EmitTernary(
            () => { EmitExpression(t.Condition); EnsureBoxed(); },
            () => { EmitExpression(t.ThenBranch); EnsureBoxed(); },
            () => { EmitExpression(t.ElseBranch); EnsureBoxed(); },
            Ctx.Runtime!.IsTruthy);
    }

    /// <summary>
    /// Emits a nullish coalescing expression (left ?? right).
    /// </summary>
    protected virtual void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        _helpers.EmitNullishCoalescing(
            () => { EmitExpression(nc.Left); EnsureBoxed(); },
            () => { EmitExpression(nc.Right); EnsureBoxed(); });
    }
    #endregion

    #region Virtual Methods - Property Access (EmitGet)

    /// <summary>
    /// Tries to emit a Symbol well-known property access (e.g., Symbol.iterator).
    /// Returns true if the expression was handled.
    /// </summary>
    protected bool TryEmitSymbolWellKnown(Expr.Get g)
    {
        if (g.Object is not Expr.Variable { Name.Lexeme: "Symbol" }) return false;
        switch (g.Name.Lexeme)
        {
            case "iterator":
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.SymbolIterator);
                break;
            case "asyncIterator":
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.SymbolAsyncIterator);
                break;
            case "toStringTag":
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.SymbolToStringTag);
                break;
            case "hasInstance":
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.SymbolHasInstance);
                break;
            case "isConcatSpreadable":
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.SymbolIsConcatSpreadable);
                break;
            case "toPrimitive":
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.SymbolToPrimitive);
                break;
            case "species":
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.SymbolSpecies);
                break;
            case "unscopables":
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.SymbolUnscopables);
                break;
            default:
                return false;
        }
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Attempts to emit a property set via TypeEmitterRegistry strategy dispatch.
    /// Call from EmitSet before falling through to dynamic SetProperty.
    /// </summary>
    protected bool TryEmitTypeRegistryPropertySet(Expr.Set s)
    {
        var objType = Ctx.TypeMap?.Get(s.Object);
        if (objType != null && Ctx.TypeEmitterRegistry != null)
        {
            var ctx = (IEmitterContext)this;
            var strategy = Ctx.TypeEmitterRegistry.GetStrategy(objType);
            if (strategy != null && strategy.TryEmitPropertySet(ctx, s.Object, s.Name.Lexeme, s.Value))
            {
                SetStackUnknown();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Emits a property access expression (obj.prop).
    /// Default implementation handles Symbol well-known properties, static field access,
    /// and falls back to dynamic property access via GetProperty.
    /// </summary>
    /// <summary>
    /// Emits a read of a namespace-level variable through its static backing field when
    /// <paramref name="g"/> is <c>N.x</c> (or nested <c>N.M.x</c>) and <c>x</c> is a known
    /// namespace var/let/const. This makes external access observe the live binding that member
    /// functions mutate, rather than the snapshot stored in the namespace object at declaration
    /// (#623). Returns false (leaving the normal property-get path) when the object is not a
    /// namespace path or the member is not a namespace variable. Shared by ILEmitter and the
    /// state-machine emitters so async/generator bodies see live bindings too (#656).
    /// </summary>
    protected bool TryEmitNamespaceVarGet(Expr.Get g)
    {
        if (Ctx.NamespaceVarFields == null) return false;

        string? nsPath = ResolveNamespacePathForGet(g.Object);
        if (nsPath == null) return false;

        if (Ctx.NamespaceVarFields.TryGetValue(nsPath, out var fields) &&
            fields.TryGetValue(g.Name.Lexeme, out var field))
        {
            IL.Emit(OpCodes.Ldsfld, field);
            SetStackUnknown();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves an expression to the dotted path of the namespace it denotes (e.g. <c>N</c> or
    /// <c>N.M</c>), or null if it is not a reference to a known namespace. Used by
    /// <see cref="TryEmitNamespaceVarGet"/> to locate a namespace var's backing field.
    /// </summary>
    private string? ResolveNamespacePathForGet(Expr obj)
    {
        switch (obj)
        {
            case Expr.Variable v when Ctx.NamespaceFields?.ContainsKey(v.Name.Lexeme) == true:
                return v.Name.Lexeme;
            case Expr.Get get when ResolveNamespacePathForGet(get.Object) is { } parent:
                string candidate = $"{parent}.{get.Name.Lexeme}";
                return Ctx.NamespaceFields?.ContainsKey(candidate) == true ? candidate : null;
            default:
                return null;
        }
    }

    protected virtual void EmitGet(Expr.Get g)
    {
        // CommonJS: `module.exports` reads → ldsfld $exports.
        if (TryEmitCjsGet(g)) return;

        // Namespace var live-binding redirect (#623): external `N.x` reads of a namespace
        // var/let/const go to the var's static backing field — the same field member functions
        // write — so external access observes the live binding, not the declaration-time
        // snapshot. Lives here in the shared base (not just ILEmitter) so state-machine bodies
        // (async/generator MoveNext emitters) observe live bindings too (#656).
        if (TryEmitNamespaceVarGet(g)) return;

        if (!g.Optional
            && g.Name.Lexeme == "length"
            && g.Object is Expr.Variable stableAllResult
            && Ctx.TypeMap?.IsStablePrimitivePromiseAllResultUse(stableAllResult) == true)
        {
            EmitExpression(g.Object);
            EnsureBoxed();
            IL.Emit(OpCodes.Castclass, Types.ListOfDouble);
            IL.Emit(OpCodes.Callvirt,
                Types.GetProperty(Types.ListOfDouble, "Count").GetGetMethod()!);
            IL.Emit(OpCodes.Conv_R8);
            SetStackType(StackType.Double);
            return;
        }

        if (TryEmitSymbolWellKnown(g)) return;
        if (TryEmitStaticClassMethodValue(g)) return;
        if (TryEmitStaticFieldAccess(g)) return;

        // Static type property dispatch via registry (Math.PI, Number.MAX_VALUE, Symbol.iterator, etc.)
        var ctx = (IEmitterContext)this;
        if (g.Object is Expr.Variable staticVar && Ctx.TypeEmitterRegistry != null)
        {
            var staticStrategy = Ctx.TypeEmitterRegistry.GetStaticStrategy(staticVar.Name.Lexeme);
            if (staticStrategy != null && staticStrategy.TryEmitStaticPropertyGet(ctx, g.Name.Lexeme))
            {
                SetStackUnknown();
                return;
            }
        }

        // Type-first dispatch: Use TypeEmitterRegistry for property getters (AbortController.signal, etc.)
        var objType = Ctx.TypeMap?.Get(g.Object);
        if (objType != null && Ctx.TypeEmitterRegistry != null)
        {
            var strategy = Ctx.TypeEmitterRegistry.GetStrategy(objType);
            if (strategy != null && strategy.TryEmitPropertyGet(ctx, g.Object, g.Name.Lexeme))
            {
                SetStackUnknown();
                return;
            }
        }

        if (TryEmitBuiltInModuleLivePropertyGet(g)) return;

        // Dynamic property access fallback
        EmitExpression(g.Object);
        EnsureBoxed();
        if (g.Optional)
        {
            // `o?.x`: short-circuit to undefined when the base is nullish. This base
            // emitter (used by the async/generator state-machine emitters) previously
            // leaned on GetProperty's leniency for a nullish base; the explicit
            // short-circuit is required now that the non-optional path throws on
            // $Undefined below, and is otherwise behaviour-preserving.
            var nullishLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Brfalse, nullishLabel);          // CLR null → nullish
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
            IL.Emit(OpCodes.Brtrue, nullishLabel);           // $Undefined → nullish
            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(nullishLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);
            IL.MarkLabel(endLabel);
        }
        else
        {
            // RequireObjectCoercible: a non-optional read on `undefined` throws a
            // guest TypeError instead of silently yielding undefined (#701), and on
            // a genuine value-null now that sloppy `this` is the globalThis sentinel
            // (#735). Null-placeholder globals (e.g. `process`) are exempt.
            if (!IsNullPlaceholderGlobal(g.Object))
                EmitThrowIfUndefinedReceiverOnStack(g.Name.Lexeme);
            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
        }
        SetStackUnknown();
    }

    #endregion

    #region Virtual Methods (override in async/generator emitters)
    /// <summary>
    /// Emits an await expression. Override in async emitters.
    /// </summary>
    protected virtual void EmitAwait(Expr.Await aw)
    {
        throw new CompileException("Await not supported in this context");
    }

    /// <summary>
    /// Emits a yield expression. Override in generator emitters.
    /// </summary>
    protected virtual void EmitYield(Expr.Yield y)
    {
        throw new CompileException("Yield not supported in this context");
    }
    #endregion
}
