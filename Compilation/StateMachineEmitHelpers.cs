using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Shared IL emission helpers for state machine emitters (Async, Generator, AsyncGenerator, AsyncArrow).
/// Centralizes boxing, constants, arithmetic, comparisons, and method call emission to eliminate code duplication.
/// </summary>
public class StateMachineEmitHelpers
{
    private readonly ILGenerator _il;
    private readonly TypeProvider _types;
    private StackType _stackType = StackType.Unknown;

    /// <summary>
    /// Current stack type after the last emission. Used for conditional boxing.
    /// </summary>
    public StackType StackType
    {
        get => _stackType;
        set => _stackType = value;
    }

    /// <summary>
    /// The underlying ILGenerator for direct access when needed.
    /// </summary>
    public ILGenerator IL => _il;

    /// <summary>
    /// The type provider for runtime type references.
    /// </summary>
    public TypeProvider Types => _types;

    public StateMachineEmitHelpers(ILGenerator il, TypeProvider types)
    {
        _il = il;
        _types = types;
    }

    #region Boxing and Type Conversion

    /// <summary>
    /// Box the value on the stack if it's an unboxed value type (Double or Boolean).
    /// </summary>
    public void EnsureBoxed()
    {
        switch (_stackType)
        {
            case StackType.Double:
                _il.Emit(OpCodes.Box, _types.Double);
                break;
            case StackType.Boolean:
                _il.Emit(OpCodes.Box, _types.Boolean);
                break;
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Ensures the value on stack is an unboxed double.
    /// Only emits conversion IL if stack is not already a double.
    /// </summary>
    public void EnsureDouble()
    {
        if (_stackType != StackType.Double)
        {
            _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
            _stackType = StackType.Double;
        }
    }

    /// <summary>
    /// Ensures the value on stack is an unboxed boolean (int32 0 or 1).
    /// Only emits conversion IL if stack is not already a boolean.
    /// </summary>
    public void EnsureBoolean()
    {
        if (_stackType != StackType.Boolean)
        {
            _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToBoolean", [_types.Object]));
            _stackType = StackType.Boolean;
        }
    }

    /// <summary>
    /// Ensures the value on stack is a string.
    /// Converts to string if not already a string.
    /// </summary>
    public void EnsureString()
    {
        if (_stackType != StackType.String)
        {
            _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToString", [_types.Object]));
            _stackType = StackType.String;
        }
    }

    public void EmitBoxDouble()
    {
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    public void EmitBoxBool()
    {
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    public void SetStackUnknown()
    {
        _stackType = StackType.Unknown;
    }

    public void SetStackType(StackType type)
    {
        _stackType = type;
    }

    #endregion

    #region Literal/Constant Helpers

    public void EmitDoubleConstant(double value)
    {
        _il.Emit(OpCodes.Ldc_R8, value);
        _stackType = StackType.Double;
    }

    public void EmitBoxedDoubleConstant(double value)
    {
        _il.Emit(OpCodes.Ldc_R8, value);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    public void EmitBoolConstant(bool value)
    {
        _il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        _stackType = StackType.Boolean;
    }

    public void EmitBoxedBoolConstant(bool value)
    {
        _il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    public void EmitStringConstant(string value)
    {
        _il.Emit(OpCodes.Ldstr, value);
        _stackType = StackType.String;
    }

    public void EmitNullConstant()
    {
        _il.Emit(OpCodes.Ldnull);
        _stackType = StackType.Null;
    }

    #endregion

    #region Arithmetic Helpers

    public void EmitAdd_Double()
    {
        _il.Emit(OpCodes.Add);
        _stackType = StackType.Double;
    }

    public void EmitSub_Double()
    {
        _il.Emit(OpCodes.Sub);
        _stackType = StackType.Double;
    }

    public void EmitMul_Double()
    {
        _il.Emit(OpCodes.Mul);
        _stackType = StackType.Double;
    }

    public void EmitDiv_Double()
    {
        _il.Emit(OpCodes.Div);
        _stackType = StackType.Double;
    }

    public void EmitRem_Double()
    {
        _il.Emit(OpCodes.Rem);
        _stackType = StackType.Double;
    }

    public void EmitNeg_Double()
    {
        _il.Emit(OpCodes.Neg);
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits Add and boxes result.
    /// </summary>
    public void EmitAddAndBox()
    {
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits Sub and boxes result.
    /// </summary>
    public void EmitSubAndBox()
    {
        _il.Emit(OpCodes.Sub);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits Mul and boxes result.
    /// </summary>
    public void EmitMulAndBox()
    {
        _il.Emit(OpCodes.Mul);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits Div and boxes result.
    /// </summary>
    public void EmitDivAndBox()
    {
        _il.Emit(OpCodes.Div);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    #endregion

    #region Comparison Helpers

    public void EmitClt_Boolean()
    {
        _il.Emit(OpCodes.Clt);
        _stackType = StackType.Boolean;
    }

    public void EmitCgt_Boolean()
    {
        _il.Emit(OpCodes.Cgt);
        _stackType = StackType.Boolean;
    }

    public void EmitCeq_Boolean()
    {
        _il.Emit(OpCodes.Ceq);
        _stackType = StackType.Boolean;
    }

    public void EmitLessOrEqual_Boolean()
    {
        _il.Emit(OpCodes.Cgt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _stackType = StackType.Boolean;
    }

    public void EmitGreaterOrEqual_Boolean()
    {
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _stackType = StackType.Boolean;
    }

    #endregion

    #region Method Call Helpers

    public void EmitCallUnknown(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        SetStackUnknown();
    }

    public void EmitCallvirtUnknown(MethodInfo method)
    {
        _il.Emit(OpCodes.Callvirt, method);
        SetStackUnknown();
    }

    public void EmitCallString(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _stackType = StackType.String;
    }

    public void EmitCallBoolean(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _stackType = StackType.Boolean;
    }

    public void EmitCallDouble(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _stackType = StackType.Double;
    }

    public void EmitCallAndBoxDouble(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    public void EmitCallAndBoxBool(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    #endregion

    #region Variable Load Helpers

    public void EmitLdlocUnknown(LocalBuilder local)
    {
        _il.Emit(OpCodes.Ldloc, local);
        SetStackUnknown();
    }

    /// <summary>
    /// Loads a local variable and sets stack type based on local type.
    /// </summary>
    public void EmitLdloc(LocalBuilder local, Type localType)
    {
        _il.Emit(OpCodes.Ldloc, local);
        _stackType = _types.IsDouble(localType) ? StackType.Double
                   : _types.IsBoolean(localType) ? StackType.Boolean
                   : StackType.Unknown;
    }

    public void EmitLdargUnknown(int argIndex)
    {
        _il.Emit(OpCodes.Ldarg, argIndex);
        SetStackUnknown();
    }

    /// <summary>
    /// Loads argument 0 (this) with Unknown stack type.
    /// </summary>
    public void EmitLdarg0Unknown()
    {
        _il.Emit(OpCodes.Ldarg_0);
        SetStackUnknown();
    }

    public void EmitLdfldUnknown(FieldInfo field)
    {
        _il.Emit(OpCodes.Ldfld, field);
        SetStackUnknown();
    }

    /// <summary>
    /// Loads a static field with Unknown stack type.
    /// </summary>
    public void EmitLdsfldUnknown(FieldInfo field)
    {
        _il.Emit(OpCodes.Ldsfld, field);
        SetStackUnknown();
    }

    #endregion

    #region Specialized Helpers

    public void EmitNewobjUnknown(ConstructorInfo ctor)
    {
        _il.Emit(OpCodes.Newobj, ctor);
        SetStackUnknown();
    }

    public void EmitConvertToDouble()
    {
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _stackType = StackType.Double;
    }

    public void EmitConvR8AndBox()
    {
        _il.Emit(OpCodes.Conv_R8);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    public void EmitObjectEqualsBoxed()
    {
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", [_types.Object, _types.Object]));
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    public void EmitObjectNotEqualsBoxed()
    {
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", [_types.Object, _types.Object]));
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit a call to Runtime.IsTruthy. Leaves boolean on stack.
    /// </summary>
    public void EmitTruthyCheck(MethodInfo isTruthyMethod)
    {
        _il.Emit(OpCodes.Call, isTruthyMethod);
        _stackType = StackType.Boolean;
    }

    #endregion

    #region Expression Emission Helpers

    /// <summary>
    /// Emit logical AND (&&) or OR (||) with short-circuit evaluation.
    /// </summary>
    /// <param name="isAnd">True for &&, false for ||</param>
    /// <param name="emitLeft">Action to emit left operand (should leave boxed value on stack)</param>
    /// <param name="emitRight">Action to emit right operand (should leave boxed value on stack)</param>
    /// <param name="isTruthyMethod">Runtime.IsTruthy method</param>
    public void EmitLogical(bool isAnd, Action emitLeft, Action emitRight, MethodInfo isTruthyMethod)
    {
        var endLabel = _il.DefineLabel();

        emitLeft();
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);
        EmitTruthyCheck(isTruthyMethod);

        if (isAnd)
        {
            // Short-circuit: if left is falsy, return left
            _il.Emit(OpCodes.Brfalse, endLabel);
            _il.Emit(OpCodes.Pop);
            emitRight();
            EnsureBoxed();
        }
        else
        {
            // Short-circuit: if left is truthy, return left
            _il.Emit(OpCodes.Brtrue, endLabel);
            _il.Emit(OpCodes.Pop);
            emitRight();
            EnsureBoxed();
        }

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit ternary conditional expression (condition ? thenBranch : elseBranch).
    /// </summary>
    public void EmitTernary(Action emitCondition, Action emitThen, Action emitElse, MethodInfo isTruthyMethod)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        emitCondition();
        EnsureBoxed();
        EmitTruthyCheck(isTruthyMethod);
        _il.Emit(OpCodes.Brfalse, elseLabel);

        emitThen();
        EnsureBoxed();
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(elseLabel);
        emitElse();
        EnsureBoxed();

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit nullish coalescing expression (left ?? right).
    /// </summary>
    public void EmitNullishCoalescing(Action emitLeft, Action emitRight)
    {
        var rightLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        emitLeft();
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Brfalse, rightLabel);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(rightLabel);
        _il.Emit(OpCodes.Pop);
        emitRight();
        EnsureBoxed();

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit unary negation (-operand).
    /// </summary>
    public void EmitUnaryMinus(Action emitOperand)
    {
        emitOperand();
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _il.Emit(OpCodes.Neg);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit logical NOT (!operand).
    /// </summary>
    public void EmitUnaryNot(Action emitOperand, MethodInfo isTruthyMethod)
    {
        emitOperand();
        EnsureBoxed();
        EmitTruthyCheck(isTruthyMethod);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit typeof operator.
    /// </summary>
    public void EmitUnaryTypeOf(Action emitOperand, MethodInfo typeOfMethod)
    {
        emitOperand();
        EnsureBoxed();
        _il.Emit(OpCodes.Call, typeOfMethod);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit bitwise NOT (~operand).
    /// </summary>
    public void EmitUnaryBitwiseNot(Action emitOperand)
    {
        emitOperand();
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", [_types.Object]));
        _il.Emit(OpCodes.Not);
        _il.Emit(OpCodes.Conv_R8);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    /// <summary>
    /// Helper to convert stack top to double. Used in binary operations.
    /// Stack: [left, right] -> [left_double, right]
    /// </summary>
    public void EmitToDoubleSwapRight()
    {
        var rightLocal = _il.DeclareLocal(_types.Object);
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _il.Emit(OpCodes.Ldloc, rightLocal);
    }

    /// <summary>
    /// Emit numeric comparison with both operands converted to double.
    /// Stack: [left, right] -> [bool_boxed]
    /// </summary>
    public void EmitNumericComparison(OpCode compareOp)
    {
        var rightLocal = _il.DeclareLocal(_types.Object);
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _il.Emit(compareOp);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit less-than-or-equal comparison (a <= b).
    /// Stack: [left, right] -> [bool_boxed]
    /// </summary>
    public void EmitNumericComparisonLe()
    {
        var rightLocal = _il.DeclareLocal(_types.Object);
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        // a <= b is equivalent to !(a > b)
        _il.Emit(OpCodes.Cgt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit greater-than-or-equal comparison (a >= b).
    /// Stack: [left, right] -> [bool_boxed]
    /// </summary>
    public void EmitNumericComparisonGe()
    {
        var rightLocal = _il.DeclareLocal(_types.Object);
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        // a >= b is equivalent to !(a < b)
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit arithmetic binary operation (-, *, /, %).
    /// Stack: [left, right] -> [result_boxed]
    /// </summary>
    public void EmitArithmeticBinary(OpCode arithmeticOp)
    {
        EmitToDoubleSwapRight();
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _il.Emit(arithmeticOp);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit equality check using Runtime.Equals and box the result.
    /// Stack: [left, right] -> [bool_boxed]
    /// </summary>
    public void EmitRuntimeEquals(MethodInfo equalsMethod)
    {
        _il.Emit(OpCodes.Call, equalsMethod);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit inequality check using Runtime.Equals, negate, and box the result.
    /// Stack: [left, right] -> [bool_boxed]
    /// </summary>
    public void EmitRuntimeNotEquals(MethodInfo equalsMethod)
    {
        _il.Emit(OpCodes.Call, equalsMethod);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    #endregion

    #region Console Intrinsics

    /// <summary>
    /// Checks if the call expression is a console.log call (either Variable or Get pattern).
    /// </summary>
    public static bool IsConsoleLogCall(SharpTS.Parsing.Expr.Call call)
    {
        // Pattern 1: Parser transforms console.log to Variable "console.log"
        if (call.Callee is SharpTS.Parsing.Expr.Variable v && v.Name.Lexeme == "console.log")
            return true;

        // Pattern 2: Get expression console.log
        if (call.Callee is SharpTS.Parsing.Expr.Get g &&
            g.Object is SharpTS.Parsing.Expr.Variable consoleVar &&
            consoleVar.Name.Lexeme == "console" &&
            g.Name.Lexeme == "log")
            return true;

        return false;
    }

    /// <summary>
    /// Emits a console.log call if the expression matches. Returns true if handled.
    /// Supports 0, 1, or multiple arguments.
    /// </summary>
    /// <param name="call">The call expression to check</param>
    /// <param name="emitArgumentBoxed">Action to emit a single argument expression AND ensure it's boxed</param>
    /// <param name="consoleLogMethod">RuntimeTypes.ConsoleLog method for single argument</param>
    /// <param name="consoleLogMultipleMethod">RuntimeTypes.ConsoleLogMultiple method for multiple arguments (optional)</param>
    /// <returns>True if this was a console.log call and was handled</returns>
    public bool TryEmitConsoleLog(
        SharpTS.Parsing.Expr.Call call,
        Action<SharpTS.Parsing.Expr> emitArgumentBoxed,
        MethodInfo consoleLogMethod,
        MethodInfo? consoleLogMultipleMethod = null)
    {
        if (!IsConsoleLogCall(call))
            return false;

        if (call.Arguments.Count == 0)
        {
            // No arguments - just print newline
            _il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Console, "WriteLine"));
        }
        else if (call.Arguments.Count == 1)
        {
            // Single argument - use ConsoleLog
            emitArgumentBoxed(call.Arguments[0]);
            _il.Emit(OpCodes.Call, consoleLogMethod);
        }
        else if (consoleLogMultipleMethod != null)
        {
            // Multiple arguments - use ConsoleLogMultiple
            _il.Emit(OpCodes.Ldc_I4, call.Arguments.Count);
            _il.Emit(OpCodes.Newarr, _types.Object);
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);
                emitArgumentBoxed(call.Arguments[i]);
                _il.Emit(OpCodes.Stelem_Ref);
            }
            _il.Emit(OpCodes.Call, consoleLogMultipleMethod);
        }
        else
        {
            // Multiple arguments but no ConsoleLogMultiple available - just emit first arg
            emitArgumentBoxed(call.Arguments[0]);
            _il.Emit(OpCodes.Call, consoleLogMethod);
        }

        // console.log returns undefined (null)
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
        return true;
    }

    #endregion
}
