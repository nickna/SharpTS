using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    #region Stack Type Tracking

    /// <summary>
    /// Returns the stack type that an expression will produce based on TypeMap.
    /// </summary>
    private StackType GetExpressionStackType(Expr expr)
    {
        var type = _ctx.TypeMap?.Get(expr);
        return type switch
        {
            TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => StackType.Double,
            TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => StackType.Boolean,
            TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_STRING } => StackType.String,
            TypeSystem.TypeInfo.Null => StackType.Null,
            _ => StackType.Unknown
        };
    }

    /// <summary>
    /// Ensures the value on stack is boxed as object.
    /// Only emits boxing IL if current stack type is a value type.
    /// </summary>
    private void EnsureBoxed()
    {
        switch (_stackType)
        {
            case StackType.Double:
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                _stackType = StackType.Unknown;
                break;
            case StackType.Boolean:
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                _stackType = StackType.Unknown;
                break;
            // String, Null, Unknown are already reference types - no boxing needed
        }
    }

    /// <summary>
    /// Ensures the value on stack is an unboxed double.
    /// Only emits unboxing IL if stack is not already a double.
    /// </summary>
    public void EnsureDouble()
    {
        if (_stackType != StackType.Double)
        {
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Convert, "ToDouble", _ctx.Types.Object));
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
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Convert, "ToBoolean", _ctx.Types.Object));
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
            // Call object.ToString() - handles null and value types
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Convert, "ToString", _ctx.Types.Object));
            _stackType = StackType.String;
        }
    }

    /// <summary>
    /// Emits a boxed double constant and sets _stackType.
    /// Use this helper to avoid forgetting to set _stackType after boxing.
    /// </summary>
    private void EmitBoxedDoubleConstant(double value)
    {
        IL.Emit(OpCodes.Ldc_R8, value);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits a string constant and sets _stackType.
    /// Use this helper to avoid forgetting to set _stackType after loading a string.
    /// </summary>
    private void EmitStringConstant(string value)
    {
        IL.Emit(OpCodes.Ldstr, value);
        _stackType = StackType.String;
    }

    #region Literal/Constant Helpers

    /// <summary>
    /// Emits an unboxed double constant.
    /// </summary>
    private void EmitDoubleConstant(double value)
    {
        IL.Emit(OpCodes.Ldc_R8, value);
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits an unboxed boolean constant.
    /// </summary>
    private void EmitBoolConstant(bool value)
    {
        IL.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        _stackType = StackType.Boolean;
    }

    /// <summary>
    /// Emits a boxed boolean constant.
    /// </summary>
    private void EmitBoxedBoolConstant(bool value)
    {
        IL.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits a null constant.
    /// </summary>
    private void EmitNullConstant()
    {
        IL.Emit(OpCodes.Ldnull);
        _stackType = StackType.Null;
    }

    #endregion

    #region Boxing Helpers

    /// <summary>
    /// Boxes the current double on stack.
    /// </summary>
    private void EmitBoxDouble()
    {
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Boxes the current boolean on stack.
    /// </summary>
    private void EmitBoxBool()
    {
        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Sets stack type to Unknown (for cases where boxing already happened).
    /// </summary>
    private void SetStackUnknown()
    {
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Sets stack type explicitly.
    /// </summary>
    private void SetStackType(StackType type)
    {
        _stackType = type;
    }

    #endregion

    #region Arithmetic Helpers

    /// <summary>
    /// Emits Add and sets stack type to Double.
    /// </summary>
    private void EmitAdd_Double()
    {
        IL.Emit(OpCodes.Add);
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits Sub and sets stack type to Double.
    /// </summary>
    private void EmitSub_Double()
    {
        IL.Emit(OpCodes.Sub);
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits Mul and sets stack type to Double.
    /// </summary>
    private void EmitMul_Double()
    {
        IL.Emit(OpCodes.Mul);
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits Div and sets stack type to Double.
    /// </summary>
    private void EmitDiv_Double()
    {
        IL.Emit(OpCodes.Div);
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits Rem and sets stack type to Double.
    /// </summary>
    private void EmitRem_Double()
    {
        IL.Emit(OpCodes.Rem);
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits Neg and sets stack type to Double.
    /// </summary>
    private void EmitNeg_Double()
    {
        IL.Emit(OpCodes.Neg);
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits Add and boxes result.
    /// </summary>
    private void EmitAddAndBox()
    {
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits Sub and boxes result.
    /// </summary>
    private void EmitSubAndBox()
    {
        IL.Emit(OpCodes.Sub);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits Mul and boxes result.
    /// </summary>
    private void EmitMulAndBox()
    {
        IL.Emit(OpCodes.Mul);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits Div and boxes result.
    /// </summary>
    private void EmitDivAndBox()
    {
        IL.Emit(OpCodes.Div);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        _stackType = StackType.Unknown;
    }

    #endregion

    #region Comparison Helpers

    /// <summary>
    /// Emits Clt and sets stack type to Boolean.
    /// </summary>
    private void EmitClt_Boolean()
    {
        IL.Emit(OpCodes.Clt);
        _stackType = StackType.Boolean;
    }

    /// <summary>
    /// Emits Cgt and sets stack type to Boolean.
    /// </summary>
    private void EmitCgt_Boolean()
    {
        IL.Emit(OpCodes.Cgt);
        _stackType = StackType.Boolean;
    }

    /// <summary>
    /// Emits Ceq and sets stack type to Boolean.
    /// </summary>
    private void EmitCeq_Boolean()
    {
        IL.Emit(OpCodes.Ceq);
        _stackType = StackType.Boolean;
    }

    /// <summary>
    /// Emits less-or-equal comparison (cgt; ldc.i4.0; ceq) and sets stack type to Boolean.
    /// </summary>
    private void EmitLessOrEqual_Boolean()
    {
        IL.Emit(OpCodes.Cgt);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Ceq);
        _stackType = StackType.Boolean;
    }

    /// <summary>
    /// Emits greater-or-equal comparison (clt; ldc.i4.0; ceq) and sets stack type to Boolean.
    /// </summary>
    private void EmitGreaterOrEqual_Boolean()
    {
        IL.Emit(OpCodes.Clt);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Ceq);
        _stackType = StackType.Boolean;
    }

    #endregion

    #region Variable Load Helpers

    /// <summary>
    /// Loads a local variable and sets stack type based on local type.
    /// </summary>
    private void EmitLdloc(LocalBuilder local, Type localType)
    {
        IL.Emit(OpCodes.Ldloc, local);
        _stackType = _ctx.Types.IsDouble(localType) ? StackType.Double
                   : _ctx.Types.IsBoolean(localType) ? StackType.Boolean
                   : StackType.Unknown;
    }

    /// <summary>
    /// Loads a local variable with Unknown stack type.
    /// </summary>
    private void EmitLdlocUnknown(LocalBuilder local)
    {
        IL.Emit(OpCodes.Ldloc, local);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Loads an argument with Unknown stack type.
    /// </summary>
    private void EmitLdargUnknown(int argIndex)
    {
        IL.Emit(OpCodes.Ldarg, argIndex);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Loads argument 0 (this) with Unknown stack type.
    /// </summary>
    private void EmitLdarg0Unknown()
    {
        IL.Emit(OpCodes.Ldarg_0);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Loads a field with Unknown stack type.
    /// </summary>
    private void EmitLdfldUnknown(FieldInfo field)
    {
        IL.Emit(OpCodes.Ldfld, field);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Loads a static field with Unknown stack type.
    /// </summary>
    private void EmitLdsfldUnknown(FieldInfo field)
    {
        IL.Emit(OpCodes.Ldsfld, field);
        _stackType = StackType.Unknown;
    }

    #endregion

    #region Method Call Helpers

    /// <summary>
    /// Emits a method call with Unknown result type.
    /// </summary>
    private void EmitCallUnknown(MethodInfo method)
    {
        IL.Emit(OpCodes.Call, method);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits a virtual method call with Unknown result type.
    /// </summary>
    private void EmitCallvirtUnknown(MethodInfo method)
    {
        IL.Emit(OpCodes.Callvirt, method);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits a method call that returns a string.
    /// </summary>
    private void EmitCallString(MethodInfo method)
    {
        IL.Emit(OpCodes.Call, method);
        _stackType = StackType.String;
    }

    /// <summary>
    /// Emits a method call that returns an unboxed boolean.
    /// </summary>
    private void EmitCallBoolean(MethodInfo method)
    {
        IL.Emit(OpCodes.Call, method);
        _stackType = StackType.Boolean;
    }

    /// <summary>
    /// Emits a method call that returns an unboxed double.
    /// </summary>
    private void EmitCallDouble(MethodInfo method)
    {
        IL.Emit(OpCodes.Call, method);
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits a method call and boxes the double result.
    /// </summary>
    private void EmitCallAndBoxDouble(MethodInfo method)
    {
        IL.Emit(OpCodes.Call, method);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits a method call and boxes the bool result.
    /// </summary>
    private void EmitCallAndBoxBool(MethodInfo method)
    {
        IL.Emit(OpCodes.Call, method);
        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
        _stackType = StackType.Unknown;
    }

    #endregion

    #region Specialized Helpers

    /// <summary>
    /// Emits newobj with Unknown result.
    /// </summary>
    private void EmitNewobjUnknown(ConstructorInfo ctor)
    {
        IL.Emit(OpCodes.Newobj, ctor);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits conversion to double.
    /// </summary>
    private void EmitConvertToDouble()
    {
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Convert, "ToDouble", _ctx.Types.Object));
        _stackType = StackType.Double;
    }

    /// <summary>
    /// Emits Conv_R8 and boxes result.
    /// </summary>
    private void EmitConvR8AndBox()
    {
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits object.Equals and boxes the result.
    /// </summary>
    private void EmitObjectEqualsBoxed()
    {
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Object, "Equals", _ctx.Types.Object, _ctx.Types.Object));
        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits object.Equals, negates, and boxes the result.
    /// </summary>
    private void EmitObjectNotEqualsBoxed()
    {
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Object, "Equals", _ctx.Types.Object, _ctx.Types.Object));
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Ceq);
        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
        _stackType = StackType.Unknown;
    }

    #endregion

    #endregion
}
