using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    private void EnsureBoxed()
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

    private void EmitTruthyCheck()
    {
        // Call runtime IsTruthy method
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
        _stackType = StackType.Boolean;
    }

    #region Literal/Constant Helpers

    private void EmitDoubleConstant(double value)
    {
        _il.Emit(OpCodes.Ldc_R8, value);
        _stackType = StackType.Double;
    }

    private void EmitBoxedDoubleConstant(double value)
    {
        _il.Emit(OpCodes.Ldc_R8, value);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    private void EmitBoolConstant(bool value)
    {
        _il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        _stackType = StackType.Boolean;
    }

    private void EmitBoxedBoolConstant(bool value)
    {
        _il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    private void EmitStringConstant(string value)
    {
        _il.Emit(OpCodes.Ldstr, value);
        _stackType = StackType.String;
    }

    private void EmitNullConstant()
    {
        _il.Emit(OpCodes.Ldnull);
        _stackType = StackType.Null;
    }

    #endregion

    #region Boxing Helpers

    private void EmitBoxDouble()
    {
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    private void EmitBoxBool()
    {
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    private void SetStackUnknown()
    {
        _stackType = StackType.Unknown;
    }

    private void SetStackType(StackType type)
    {
        _stackType = type;
    }

    #endregion

    #region Arithmetic Helpers

    private void EmitAdd_Double()
    {
        _il.Emit(OpCodes.Add);
        _stackType = StackType.Double;
    }

    private void EmitSub_Double()
    {
        _il.Emit(OpCodes.Sub);
        _stackType = StackType.Double;
    }

    private void EmitMul_Double()
    {
        _il.Emit(OpCodes.Mul);
        _stackType = StackType.Double;
    }

    private void EmitDiv_Double()
    {
        _il.Emit(OpCodes.Div);
        _stackType = StackType.Double;
    }

    private void EmitRem_Double()
    {
        _il.Emit(OpCodes.Rem);
        _stackType = StackType.Double;
    }

    private void EmitNeg_Double()
    {
        _il.Emit(OpCodes.Neg);
        _stackType = StackType.Double;
    }

    #endregion

    #region Comparison Helpers

    private void EmitClt_Boolean()
    {
        _il.Emit(OpCodes.Clt);
        _stackType = StackType.Boolean;
    }

    private void EmitCgt_Boolean()
    {
        _il.Emit(OpCodes.Cgt);
        _stackType = StackType.Boolean;
    }

    private void EmitCeq_Boolean()
    {
        _il.Emit(OpCodes.Ceq);
        _stackType = StackType.Boolean;
    }

    private void EmitLessOrEqual_Boolean()
    {
        _il.Emit(OpCodes.Cgt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _stackType = StackType.Boolean;
    }

    private void EmitGreaterOrEqual_Boolean()
    {
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _stackType = StackType.Boolean;
    }

    #endregion

    #region Method Call Helpers

    private void EmitCallUnknown(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        SetStackUnknown();
    }

    private void EmitCallvirtUnknown(MethodInfo method)
    {
        _il.Emit(OpCodes.Callvirt, method);
        SetStackUnknown();
    }

    private void EmitCallString(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _stackType = StackType.String;
    }

    private void EmitCallBoolean(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _stackType = StackType.Boolean;
    }

    private void EmitCallDouble(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _stackType = StackType.Double;
    }

    private void EmitCallAndBoxDouble(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    private void EmitCallAndBoxBool(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    #endregion

    #region Variable Load Helpers

    private void EmitLdlocUnknown(LocalBuilder local)
    {
        _il.Emit(OpCodes.Ldloc, local);
        SetStackUnknown();
    }

    private void EmitLdargUnknown(int argIndex)
    {
        _il.Emit(OpCodes.Ldarg, argIndex);
        SetStackUnknown();
    }

    private void EmitLdfldUnknown(FieldInfo field)
    {
        _il.Emit(OpCodes.Ldfld, field);
        SetStackUnknown();
    }

    #endregion

    #region Specialized Helpers

    private void EmitNewobjUnknown(ConstructorInfo ctor)
    {
        _il.Emit(OpCodes.Newobj, ctor);
        SetStackUnknown();
    }

    private void EmitConvertToDouble()
    {
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _stackType = StackType.Double;
    }

    private void EmitConvR8AndBox()
    {
        _il.Emit(OpCodes.Conv_R8);
        _il.Emit(OpCodes.Box, _types.Double);
        SetStackUnknown();
    }

    private void EmitObjectEqualsBoxed()
    {
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", [_types.Object, _types.Object]));
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    private void EmitObjectNotEqualsBoxed()
    {
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", [_types.Object, _types.Object]));
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, _types.Boolean);
        SetStackUnknown();
    }

    #endregion
}
