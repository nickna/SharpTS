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
            case Expr.Call c:
                EmitCall(c);
                break;
            case Expr.Get g:
                EmitGet(g);
                break;
            case Expr.Set s:
                EmitSet(s);
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
            case Expr.Grouping g:
                EmitGrouping(g);
                break;
            case Expr.TypeAssertion ta:
                EmitTypeAssertion(ta);
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
            default:
                EmitUnknownExpression(expr);
                break;
        }
    }
    #endregion

    #region Abstract Expression Methods
    protected abstract void EmitLiteral(Expr.Literal lit);
    protected abstract void EmitVariable(Expr.Variable v);
    protected abstract void EmitAssign(Expr.Assign a);
    protected abstract void EmitBinary(Expr.Binary b);
    protected abstract void EmitLogical(Expr.Logical l);
    protected abstract void EmitUnary(Expr.Unary u);
    protected abstract void EmitCall(Expr.Call c);
    protected abstract void EmitGet(Expr.Get g);
    protected abstract void EmitSet(Expr.Set s);
    protected abstract void EmitGetIndex(Expr.GetIndex gi);
    protected abstract void EmitSetIndex(Expr.SetIndex si);
    protected abstract void EmitTernary(Expr.Ternary t);
    protected abstract void EmitNullishCoalescing(Expr.NullishCoalescing nc);
    protected abstract void EmitTemplateLiteral(Expr.TemplateLiteral tl);
    protected abstract void EmitArrayLiteral(Expr.ArrayLiteral al);
    protected abstract void EmitObjectLiteral(Expr.ObjectLiteral ol);
    protected abstract void EmitNew(Expr.New n);
    protected abstract void EmitThis();
    protected abstract void EmitSuper(Expr.Super s);
    protected abstract void EmitCompoundAssign(Expr.CompoundAssign ca);
    protected abstract void EmitCompoundSet(Expr.CompoundSet cs);
    protected abstract void EmitCompoundSetIndex(Expr.CompoundSetIndex csi);
    protected abstract void EmitPrefixIncrement(Expr.PrefixIncrement pi);
    protected abstract void EmitPostfixIncrement(Expr.PostfixIncrement poi);
    protected abstract void EmitArrowFunction(Expr.ArrowFunction af);
    protected abstract void EmitRegexLiteral(Expr.RegexLiteral re);
    protected abstract void EmitDynamicImport(Expr.DynamicImport di);
    protected abstract void EmitGrouping(Expr.Grouping g);
    protected abstract void EmitTypeAssertion(Expr.TypeAssertion ta);
    protected abstract void EmitNonNullAssertion(Expr.NonNullAssertion nna);
    protected abstract void EmitSpread(Expr.Spread sp);
    protected abstract void EmitUnknownExpression(Expr expr);
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
