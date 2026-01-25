using System.Reflection.Emit;
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
public abstract class ExpressionEmitterBase
{
    protected readonly StateMachineEmitHelpers _helpers;

    protected abstract ILGenerator IL { get; }
    protected abstract CompilationContext Ctx { get; }
    protected abstract TypeProvider Types { get; }
    protected abstract IVariableResolver Resolver { get; }

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

    #region Core Expression Dispatch
    /// <summary>
    /// Dispatches expression emission to the appropriate handler method.
    /// </summary>
    public virtual void EmitExpression(Expr expr)
    {
        switch (expr)
        {
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
                throw new InvalidOperationException($"Compilation Error: Unhandled expression type in ILEmitter: {expr.GetType().Name}");
        }
    }
    #endregion

    #region Abstract Expression Methods
    protected abstract void EmitLiteral(Expr.Literal lit);
    protected abstract void EmitVariable(Expr.Variable v);
    protected abstract void EmitAssign(Expr.Assign a);
    protected abstract void EmitBinary(Expr.Binary b);
    protected abstract void EmitCall(Expr.Call c);
    protected abstract void EmitGet(Expr.Get g);
    protected abstract void EmitSet(Expr.Set s);
    protected abstract void EmitGetPrivate(Expr.GetPrivate gp);
    protected abstract void EmitSetPrivate(Expr.SetPrivate sp);
    protected abstract void EmitCallPrivate(Expr.CallPrivate cp);
    protected abstract void EmitGetIndex(Expr.GetIndex gi);
    protected abstract void EmitSetIndex(Expr.SetIndex si);
    protected abstract void EmitTemplateLiteral(Expr.TemplateLiteral tl);
    protected abstract void EmitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral ttl);
    protected abstract void EmitArrayLiteral(Expr.ArrayLiteral al);
    protected abstract void EmitObjectLiteral(Expr.ObjectLiteral ol);
    protected abstract void EmitNew(Expr.New n);
    protected abstract void EmitThis();
    protected abstract void EmitSuper(Expr.Super s);
    protected abstract void EmitCompoundAssign(Expr.CompoundAssign ca);
    protected abstract void EmitCompoundSet(Expr.CompoundSet cs);
    protected abstract void EmitCompoundSetIndex(Expr.CompoundSetIndex csi);
    protected abstract void EmitLogicalAssign(Expr.LogicalAssign la);
    protected abstract void EmitLogicalSet(Expr.LogicalSet ls);
    protected abstract void EmitLogicalSetIndex(Expr.LogicalSetIndex lsi);
    protected abstract void EmitPrefixIncrement(Expr.PrefixIncrement pi);
    protected abstract void EmitPostfixIncrement(Expr.PostfixIncrement poi);
    protected abstract void EmitArrowFunction(Expr.ArrowFunction af);
    protected abstract void EmitDynamicImport(Expr.DynamicImport di);
    protected abstract void EmitImportMeta(Expr.ImportMeta im);
    protected abstract void EmitClassExpression(Expr.ClassExpr ce);
    protected abstract void EmitDelete(Expr.Delete del);
    #endregion

    #region Virtual Methods - Pass-through expressions
    /// <summary>
    /// Emits a grouping expression by evaluating its inner expression.
    /// </summary>
    protected virtual void EmitGrouping(Expr.Grouping g) => EmitExpression(g.Expression);

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
    /// Emits a regex literal. Default implementation pushes null - override in ILEmitter for actual regex creation.
    /// </summary>
    protected virtual void EmitRegexLiteral(Expr.RegexLiteral re)
    {
        IL.Emit(OpCodes.Ldnull);
        SetStackUnknown();
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
            case TokenType.BANG:
                _helpers.EmitUnaryNot(() => EmitExpression(u.Right), Ctx.Runtime!.IsTruthy);
                break;
            case TokenType.TYPEOF:
                _helpers.EmitUnaryTypeOf(() => EmitExpression(u.Right), Ctx.Runtime!.TypeOf);
                break;
            case TokenType.TILDE:
                _helpers.EmitUnaryBitwiseNot(() => EmitExpression(u.Right));
                break;
            default:
                throw new NotImplementedException($"Unary operator {u.Operator.Type} not implemented");
        }
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

    #region Virtual Methods (override in async/generator emitters)
    /// <summary>
    /// Emits an await expression. Override in async emitters.
    /// </summary>
    protected virtual void EmitAwait(Expr.Await aw)
    {
        throw new InvalidOperationException("Await not supported in this context");
    }

    /// <summary>
    /// Emits a yield expression. Override in generator emitters.
    /// </summary>
    protected virtual void EmitYield(Expr.Yield y)
    {
        throw new InvalidOperationException("Yield not supported in this context");
    }
    #endregion
}
