using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Shared IL emission helpers for state machine emitters (Async, Generator, AsyncGenerator, AsyncArrow).
/// Centralizes boxing, constants, arithmetic, comparisons, and method call emission to eliminate code duplication.
/// </summary>
public class StateMachineEmitHelpers
{
    private readonly ILGenerator _il;
    private readonly TypeProvider _types;
    private readonly ValidatedILBuilder? _builder;
    private readonly EmittedRuntime? _runtime;
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

    /// <summary>
    /// The validated IL builder, if available. Use this for label and branch operations
    /// to get compile-time validation of IL correctness.
    /// </summary>
    public ValidatedILBuilder? Builder => _builder;

    public StateMachineEmitHelpers(ILGenerator il, TypeProvider types, EmittedRuntime? runtime = null)
    {
        _il = il;
        _types = types;
        _builder = null;
        _runtime = runtime;
    }

    public StateMachineEmitHelpers(ILGenerator il, TypeProvider types, ValidatedILBuilder builder, EmittedRuntime? runtime = null)
    {
        _il = il;
        _types = types;
        _builder = builder;
        _runtime = runtime;
    }

    #region Label Operations

    /// <summary>
    /// Defines a new label. Uses validated builder if available.
    /// </summary>
    /// <param name="debugName">Optional debug name for error messages.</param>
    public Label DefineLabel(string? debugName = null)
    {
        return _builder != null
            ? _builder.DefineLabel(debugName)
            : _il.DefineLabel();
    }

    /// <summary>
    /// Marks a label at the current position. Uses validated builder if available.
    /// </summary>
    public void MarkLabel(Label label)
    {
        if (_builder != null)
            _builder.MarkLabel(label);
        else
            _il.MarkLabel(label);
    }

    #endregion

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

    public void EmitUndefinedConstant()
    {
        // Load $Undefined.Instance static field from the emitted runtime
        if (_runtime != null)
        {
            _il.Emit(OpCodes.Ldsfld, _runtime.UndefinedInstance);
        }
        else
        {
            // Fallback: emit null for standalone execution compatibility
            // This avoids a dependency on SharpTS.dll at runtime
            _il.Emit(OpCodes.Ldnull);
        }
        _stackType = StackType.Unknown;  // Treat as boxed object
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

    /// <summary>
    /// Emit Object.Equals call without boxing. Used for strict equality (===).
    /// </summary>
    public void EmitObjectEqualsBoxed_NoBox()
    {
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", [_types.Object, _types.Object]));
        _stackType = StackType.Boolean;
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
        var endLabel = DefineLabel("logical_end");

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

        MarkLabel(endLabel);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit ternary conditional expression (condition ? thenBranch : elseBranch).
    /// </summary>
    public void EmitTernary(Action emitCondition, Action emitThen, Action emitElse, MethodInfo isTruthyMethod)
    {
        var elseLabel = DefineLabel("ternary_else");
        var endLabel = DefineLabel("ternary_end");

        emitCondition();
        EnsureBoxed();
        EmitTruthyCheck(isTruthyMethod);
        _il.Emit(OpCodes.Brfalse, elseLabel);

        emitThen();
        EnsureBoxed();
        _il.Emit(OpCodes.Br, endLabel);

        MarkLabel(elseLabel);
        emitElse();
        EnsureBoxed();

        MarkLabel(endLabel);
        SetStackUnknown();
    }

    /// <summary>
    /// Emit nullish coalescing expression (left ?? right).
    /// </summary>
    public void EmitNullishCoalescing(Action emitLeft, Action emitRight)
    {
        var rightLabel = DefineLabel("nullish_right");
        var endLabel = DefineLabel("nullish_end");

        emitLeft();
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Brfalse, rightLabel);
        _il.Emit(OpCodes.Br, endLabel);

        MarkLabel(rightLabel);
        _il.Emit(OpCodes.Pop);
        emitRight();
        EnsureBoxed();

        MarkLabel(endLabel);
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

    /// <summary>
    /// Mapping of arithmetic operators to their IL opcodes.
    /// </summary>
    private static readonly Dictionary<TokenType, OpCode> ArithmeticOps = new()
    {
        { TokenType.MINUS, OpCodes.Sub },
        { TokenType.STAR, OpCodes.Mul },
        { TokenType.SLASH, OpCodes.Div },
        { TokenType.PERCENT, OpCodes.Rem }
    };

    /// <summary>
    /// Mapping of comparison operators to their IL opcodes.
    /// </summary>
    private static readonly Dictionary<TokenType, OpCode> ComparisonOps = new()
    {
        { TokenType.LESS, OpCodes.Clt },
        { TokenType.GREATER, OpCodes.Cgt }
    };

    /// <summary>
    /// Attempts to emit a binary operator. Returns true if the operator was handled.
    /// This method handles arithmetic (+, -, *, /, %), comparison (<, >, <=, >=),
    /// and equality (==, !=, ===, !==) operators.
    /// Stack: [left, right] -> [result_boxed]
    /// </summary>
    /// <param name="op">The operator token type.</param>
    /// <param name="runtimeAdd">Runtime.Add method for string concatenation support.</param>
    /// <param name="runtimeEquals">Runtime.Equals method for equality comparison.</param>
    /// <returns>True if the operator was handled, false otherwise.</returns>
    public bool TryEmitBinaryOperator(TokenType op, MethodInfo runtimeAdd, MethodInfo runtimeEquals)
    {
        // Handle PLUS via runtime (supports string concatenation)
        if (op == TokenType.PLUS)
        {
            EmitCallUnknown(runtimeAdd);
            return true;
        }

        // Handle arithmetic operators (-, *, /, %)
        if (ArithmeticOps.TryGetValue(op, out var arithOp))
        {
            EmitArithmeticBinary(arithOp);
            return true;
        }

        // Handle simple comparisons (< and >)
        if (ComparisonOps.TryGetValue(op, out var cmpOp))
        {
            EmitNumericComparison(cmpOp);
            return true;
        }

        // Handle <= (negated >)
        if (op == TokenType.LESS_EQUAL)
        {
            EmitNumericComparisonLe();
            return true;
        }

        // Handle >= (negated <)
        if (op == TokenType.GREATER_EQUAL)
        {
            EmitNumericComparisonGe();
            return true;
        }

        // Handle equality (== and ===)
        if (op is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL)
        {
            EmitRuntimeEquals(runtimeEquals);
            return true;
        }

        // Handle inequality (!= and !==)
        if (op is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL)
        {
            EmitRuntimeNotEquals(runtimeEquals);
            return true;
        }

        return false;
    }

    #endregion

    #region Compound Assignment Helpers

    /// <summary>
    /// Emits a compound assignment operation.
    /// Stack: [currentValue (object), operandValue (object)] -> [result_boxed]
    /// </summary>
    /// <param name="opType">The compound assignment operator type.</param>
    /// <param name="runtimeAdd">Runtime.Add method for PLUS_EQUAL (supports string concatenation).</param>
    public void EmitCompoundOperation(TokenType opType, MethodInfo runtimeAdd)
    {
        // PLUS_EQUAL uses runtime Add for string concatenation support
        if (opType == TokenType.PLUS_EQUAL)
        {
            EmitCallUnknown(runtimeAdd);
            return;
        }

        // Get the opcode for this operator
        var opcode = CompoundOperatorHelper.GetOpcode(opType);
        if (opcode.HasValue)
        {
            if (CompoundOperatorHelper.IsBitwise(opType))
            {
                // Bitwise operations need int32 conversion
                EmitBitwiseBinary(opcode.Value);
            }
            else
            {
                // Arithmetic operations use double conversion
                EmitArithmeticBinary(opcode.Value);
            }
        }
        else
        {
            // Fallback for unsupported operators - use Add
            EmitArithmeticBinary(OpCodes.Add);
        }
    }

    /// <summary>
    /// Emit bitwise binary operation with int32 conversion.
    /// Stack: [left (object), right (object)] -> [result_boxed]
    /// </summary>
    public void EmitBitwiseBinary(OpCode bitwiseOp)
    {
        // Convert left to int32
        var rightLocal = _il.DeclareLocal(_types.Object);
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", [_types.Object]));

        // Convert right to int32
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", [_types.Object]));

        // Apply bitwise operation
        _il.Emit(bitwiseOp);

        // Convert back to double and box
        _il.Emit(OpCodes.Conv_R8);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    #endregion

    #region Increment/Decrement Helpers

    /// <summary>
    /// Gets the increment delta (+1.0 for ++, -1.0 for --).
    /// </summary>
    public static double GetIncrementDelta(TokenType op)
        => op == TokenType.PLUS_PLUS ? 1.0 : -1.0;

    /// <summary>
    /// Emits the core increment arithmetic.
    /// Stack: [double] -> [double+delta]
    /// </summary>
    public void EmitIncrementArithmetic(TokenType op)
    {
        _il.Emit(OpCodes.Ldc_R8, GetIncrementDelta(op));
        _il.Emit(OpCodes.Add);
    }

    /// <summary>
    /// Unboxes stack top to double using Convert.ToDouble.
    /// Stack: [object] -> [double]
    /// </summary>
    public void EmitUnboxToDouble()
    {
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits prefix increment for a value already on stack (as object).
    /// The storeNewValue action should store the boxed value from stack to the target location.
    /// Stack before: [object value]
    /// Stack after storeNewValue: storeNewValue consumes the value, then we reload it
    /// Final stack: [boxed new value]
    /// </summary>
    /// <param name="op">The operator token type (PLUS_PLUS or MINUS_MINUS).</param>
    /// <param name="storeNewValue">Action to store the new value. Stack will have [boxed new value].</param>
    public void EmitPrefixIncrementCore(TokenType op, Action storeNewValue)
    {
        // Stack: [object value]
        EmitUnboxToDouble();              // Stack: [double]
        EmitIncrementArithmetic(op);      // Stack: [double+1]
        _il.Emit(OpCodes.Box, _types.Double);  // Stack: [boxed new value]
        _il.Emit(OpCodes.Dup);            // Stack: [boxed, boxed] (one for store, one for return)
        storeNewValue();                  // Stack: [boxed] (return value)
        SetStackUnknown();
    }

    /// <summary>
    /// Emits postfix increment for a value already on stack (as object).
    /// Returns old value on stack (boxed).
    /// </summary>
    /// <param name="op">The operator token type (PLUS_PLUS or MINUS_MINUS).</param>
    /// <param name="storeNewValue">Action to store the new value. Stack will have [boxed new value].</param>
    /// <param name="tempLocal">A local builder to store the old double value temporarily.</param>
    public void EmitPostfixIncrementCore(TokenType op, Action storeNewValue, LocalBuilder tempLocal)
    {
        // Stack: [object value]
        EmitUnboxToDouble();                    // Stack: [double]
        _il.Emit(OpCodes.Dup);                  // Stack: [double, double]
        _il.Emit(OpCodes.Stloc, tempLocal);     // Save old value; Stack: [double]
        EmitIncrementArithmetic(op);            // Stack: [double+1]
        _il.Emit(OpCodes.Box, _types.Double);   // Stack: [boxed new value]
        storeNewValue();                        // Stack: []
        _il.Emit(OpCodes.Ldloc, tempLocal);     // Stack: [old double]
        _il.Emit(OpCodes.Box, _types.Double);   // Stack: [boxed old value]
        SetStackUnknown();
    }

    /// <summary>
    /// Emits prefix increment for a variable that can be loaded and stored via actions.
    /// This is the simplest form where load/store are straightforward.
    /// Stack: [] -> [boxed new value]
    /// </summary>
    public void EmitPrefixIncrementVariable(TokenType op, Action loadVariable, Action storeVariable)
    {
        loadVariable();                         // Stack: [object value]
        EmitPrefixIncrementCore(op, storeVariable);
    }

    /// <summary>
    /// Emits postfix increment for a variable that can be loaded and stored via actions.
    /// Stack: [] -> [boxed old value]
    /// </summary>
    public void EmitPostfixIncrementVariable(TokenType op, Action loadVariable, Action storeVariable)
    {
        var tempLocal = _il.DeclareLocal(_types.Double);
        loadVariable();                         // Stack: [object value]
        EmitPostfixIncrementCore(op, storeVariable, tempLocal);
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

    /// <summary>
    /// Gets the console method name from a console.X call expression.
    /// Returns null if not a console method call.
    /// </summary>
    public static string? GetConsoleMethodName(SharpTS.Parsing.Expr.Call call)
    {
        // Pattern 1: Parser transforms console.X to Variable "console.X"
        if (call.Callee is SharpTS.Parsing.Expr.Variable v && v.Name.Lexeme.StartsWith("console."))
            return v.Name.Lexeme["console.".Length..];

        // Pattern 2: Get expression console.X
        if (call.Callee is SharpTS.Parsing.Expr.Get g &&
            g.Object is SharpTS.Parsing.Expr.Variable consoleVar &&
            consoleVar.Name.Lexeme == "console")
            return g.Name.Lexeme;

        return null;
    }

    /// <summary>
    /// Emits a console method call (log, error, warn, info, debug, clear, time, timeEnd, timeLog).
    /// Returns true if handled.
    /// </summary>
    public bool TryEmitConsoleMethod(
        SharpTS.Parsing.Expr.Call call,
        Action<SharpTS.Parsing.Expr> emitArgumentBoxed,
        EmittedRuntime runtime)
    {
        var methodName = GetConsoleMethodName(call);
        if (methodName == null)
            return false;

        switch (methodName)
        {
            case "log":
            case "info":
            case "debug":
                // info and debug are aliases for log - emit inline since TryEmitConsoleLog only checks for "log"
                EmitConsoleLogInline(call, emitArgumentBoxed, runtime.ConsoleLog, runtime.ConsoleLogMultiple);
                return true;

            case "error":
                EmitConsoleOutputMethod(call, emitArgumentBoxed, runtime.ConsoleError, runtime.ConsoleErrorMultiple);
                return true;

            case "warn":
                EmitConsoleOutputMethod(call, emitArgumentBoxed, runtime.ConsoleWarn, runtime.ConsoleWarnMultiple);
                return true;

            case "clear":
                _il.Emit(OpCodes.Call, runtime.ConsoleClear);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                return true;

            case "time":
                if (call.Arguments.Count >= 1)
                {
                    emitArgumentBoxed(call.Arguments[0]);
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, runtime.ConsoleTime);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                return true;

            case "timeEnd":
                if (call.Arguments.Count >= 1)
                {
                    emitArgumentBoxed(call.Arguments[0]);
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, runtime.ConsoleTimeEnd);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                return true;

            case "timeLog":
                if (call.Arguments.Count >= 1)
                {
                    emitArgumentBoxed(call.Arguments[0]);
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, runtime.ConsoleTimeLog);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                return true;

            // Phase 2 methods
            case "assert":
                EmitConsoleAssert(call, emitArgumentBoxed, runtime);
                return true;

            case "count":
                if (call.Arguments.Count >= 1)
                {
                    emitArgumentBoxed(call.Arguments[0]);
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, runtime.ConsoleCount);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                return true;

            case "countReset":
                if (call.Arguments.Count >= 1)
                {
                    emitArgumentBoxed(call.Arguments[0]);
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, runtime.ConsoleCountReset);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                return true;

            case "table":
                EmitConsoleTable(call, emitArgumentBoxed, runtime);
                return true;

            case "dir":
                EmitConsoleDir(call, emitArgumentBoxed, runtime);
                return true;

            case "group":
            case "groupCollapsed":
                EmitConsoleGroup(call, emitArgumentBoxed, runtime);
                return true;

            case "groupEnd":
                _il.Emit(OpCodes.Call, runtime.ConsoleGroupEnd);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                return true;

            case "trace":
                EmitConsoleTrace(call, emitArgumentBoxed, runtime);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Emits console.assert(condition, ...data) call.
    /// </summary>
    private void EmitConsoleAssert(
        SharpTS.Parsing.Expr.Call call,
        Action<SharpTS.Parsing.Expr> emitArgumentBoxed,
        EmittedRuntime runtime)
    {
        if (call.Arguments.Count == 0)
        {
            // No condition - assertion always fails with no message
            _il.Emit(OpCodes.Ldc_I4_0); // false condition
            _il.Emit(OpCodes.Ldnull);   // null message args
            _il.Emit(OpCodes.Call, runtime.ConsoleAssert);
        }
        else if (call.Arguments.Count == 1)
        {
            // Just condition
            emitArgumentBoxed(call.Arguments[0]);
            _il.Emit(OpCodes.Ldnull); // null message args
            _il.Emit(OpCodes.Call, runtime.ConsoleAssert);
        }
        else
        {
            // Condition + message args
            emitArgumentBoxed(call.Arguments[0]);
            // Build array of remaining args
            _il.Emit(OpCodes.Ldc_I4, call.Arguments.Count - 1);
            _il.Emit(OpCodes.Newarr, _types.Object);
            for (int i = 1; i < call.Arguments.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i - 1);
                emitArgumentBoxed(call.Arguments[i]);
                _il.Emit(OpCodes.Stelem_Ref);
            }
            _il.Emit(OpCodes.Call, runtime.ConsoleAssertMultiple);
        }
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits console.table(data, columns?) call.
    /// </summary>
    private void EmitConsoleTable(
        SharpTS.Parsing.Expr.Call call,
        Action<SharpTS.Parsing.Expr> emitArgumentBoxed,
        EmittedRuntime runtime)
    {
        if (call.Arguments.Count >= 1)
        {
            emitArgumentBoxed(call.Arguments[0]);
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }

        if (call.Arguments.Count >= 2)
        {
            emitArgumentBoxed(call.Arguments[1]);
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }
        _il.Emit(OpCodes.Call, runtime.ConsoleTable);
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits console.dir(obj, options?) call.
    /// </summary>
    private void EmitConsoleDir(
        SharpTS.Parsing.Expr.Call call,
        Action<SharpTS.Parsing.Expr> emitArgumentBoxed,
        EmittedRuntime runtime)
    {
        if (call.Arguments.Count >= 1)
        {
            emitArgumentBoxed(call.Arguments[0]);
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }
        _il.Emit(OpCodes.Call, runtime.ConsoleDir);
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits console.group(label?) or console.groupCollapsed(label?) call.
    /// </summary>
    private void EmitConsoleGroup(
        SharpTS.Parsing.Expr.Call call,
        Action<SharpTS.Parsing.Expr> emitArgumentBoxed,
        EmittedRuntime runtime)
    {
        if (call.Arguments.Count == 0)
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Call, runtime.ConsoleGroup);
        }
        else if (call.Arguments.Count == 1)
        {
            emitArgumentBoxed(call.Arguments[0]);
            _il.Emit(OpCodes.Call, runtime.ConsoleGroup);
        }
        else
        {
            // Multiple arguments - build array
            _il.Emit(OpCodes.Ldc_I4, call.Arguments.Count);
            _il.Emit(OpCodes.Newarr, _types.Object);
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);
                emitArgumentBoxed(call.Arguments[i]);
                _il.Emit(OpCodes.Stelem_Ref);
            }
            _il.Emit(OpCodes.Call, runtime.ConsoleGroupMultiple);
        }
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits console.trace(message?, ...data) call.
    /// </summary>
    private void EmitConsoleTrace(
        SharpTS.Parsing.Expr.Call call,
        Action<SharpTS.Parsing.Expr> emitArgumentBoxed,
        EmittedRuntime runtime)
    {
        if (call.Arguments.Count == 0)
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Call, runtime.ConsoleTrace);
        }
        else if (call.Arguments.Count == 1)
        {
            emitArgumentBoxed(call.Arguments[0]);
            _il.Emit(OpCodes.Call, runtime.ConsoleTrace);
        }
        else
        {
            // Multiple arguments - build array
            _il.Emit(OpCodes.Ldc_I4, call.Arguments.Count);
            _il.Emit(OpCodes.Newarr, _types.Object);
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);
                emitArgumentBoxed(call.Arguments[i]);
                _il.Emit(OpCodes.Stelem_Ref);
            }
            _il.Emit(OpCodes.Call, runtime.ConsoleTraceMultiple);
        }
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    /// <summary>
    /// Helper to emit console output methods (error, warn) with single/multiple argument support.
    /// </summary>
    private void EmitConsoleOutputMethod(
        SharpTS.Parsing.Expr.Call call,
        Action<SharpTS.Parsing.Expr> emitArgumentBoxed,
        MethodInfo singleArgMethod,
        MethodInfo multipleArgMethod)
    {
        if (call.Arguments.Count == 0)
        {
            // No arguments - just print newline to stderr
            _il.Emit(OpCodes.Call, _types.GetProperty(_types.Console, "Error").GetMethod!);
            _il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.TextWriter, "WriteLine"));
        }
        else if (call.Arguments.Count == 1)
        {
            emitArgumentBoxed(call.Arguments[0]);
            _il.Emit(OpCodes.Call, singleArgMethod);
        }
        else
        {
            // Multiple arguments
            _il.Emit(OpCodes.Ldc_I4, call.Arguments.Count);
            _il.Emit(OpCodes.Newarr, _types.Object);
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);
                emitArgumentBoxed(call.Arguments[i]);
                _il.Emit(OpCodes.Stelem_Ref);
            }
            _il.Emit(OpCodes.Call, multipleArgMethod);
        }

        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    /// <summary>
    /// Helper to emit console.log/info/debug calls directly.
    /// </summary>
    private void EmitConsoleLogInline(
        SharpTS.Parsing.Expr.Call call,
        Action<SharpTS.Parsing.Expr> emitArgumentBoxed,
        MethodInfo singleArgMethod,
        MethodInfo multipleArgMethod)
    {
        if (call.Arguments.Count == 0)
        {
            // No arguments - just print newline
            _il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Console, "WriteLine"));
        }
        else if (call.Arguments.Count == 1)
        {
            emitArgumentBoxed(call.Arguments[0]);
            _il.Emit(OpCodes.Call, singleArgMethod);
        }
        else
        {
            // Multiple arguments
            _il.Emit(OpCodes.Ldc_I4, call.Arguments.Count);
            _il.Emit(OpCodes.Newarr, _types.Object);
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);
                emitArgumentBoxed(call.Arguments[i]);
                _il.Emit(OpCodes.Stelem_Ref);
            }
            _il.Emit(OpCodes.Call, multipleArgMethod);
        }

        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    #endregion
}
