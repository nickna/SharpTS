using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorMoveNextEmitter
{
    private void EnsureBoxed()
    {
        switch (_stackType)
        {
            case StackType.Number:
                _il.Emit(OpCodes.Box, _types.Double);
                break;
            case StackType.Boolean:
                _il.Emit(OpCodes.Box, _types.Boolean);
                break;
        }
        _stackType = StackType.Unknown;
    }

    private void EmitTruthyCheck()
    {
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
        _stackType = StackType.Boolean;
    }

    #region Literal/Constant Helpers

    private void EmitDoubleConstant(double value)
    {
        _il.Emit(OpCodes.Ldc_R8, value);
        _stackType = StackType.Number;
    }

    private void EmitBoxedDoubleConstant(double value)
    {
        _il.Emit(OpCodes.Ldc_R8, value);
        _il.Emit(OpCodes.Box, _types.Double);
        _stackType = StackType.Unknown;
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
        _stackType = StackType.Unknown;
    }

    private void EmitStringConstant(string value)
    {
        _il.Emit(OpCodes.Ldstr, value);
        _stackType = StackType.String;
    }

    private void EmitNullConstant()
    {
        _il.Emit(OpCodes.Ldnull);
        _stackType = StackType.Object;
    }

    #endregion

    #region Boxing Helpers

    private void EmitBoxDouble()
    {
        _il.Emit(OpCodes.Box, _types.Double);
        _stackType = StackType.Unknown;
    }

    private void EmitBoxBool()
    {
        _il.Emit(OpCodes.Box, _types.Boolean);
        _stackType = StackType.Unknown;
    }

    #endregion

    #region Method Call Helpers

    private void EmitCallUnknown(MethodInfo method)
    {
        _il.Emit(OpCodes.Call, method);
        _stackType = StackType.Unknown;
    }

    private void EmitCallvirtUnknown(MethodInfo method)
    {
        _il.Emit(OpCodes.Callvirt, method);
        _stackType = StackType.Unknown;
    }

    #endregion

    #region Variable Load Helpers

    private void EmitLdlocUnknown(LocalBuilder local)
    {
        _il.Emit(OpCodes.Ldloc, local);
        _stackType = StackType.Unknown;
    }

    private void EmitLdargUnknown(int argIndex)
    {
        _il.Emit(OpCodes.Ldarg, argIndex);
        _stackType = StackType.Unknown;
    }

    private void EmitLdfldUnknown(FieldInfo field)
    {
        _il.Emit(OpCodes.Ldfld, field);
        _stackType = StackType.Unknown;
    }

    #endregion

    #region Specialized Helpers

    private void EmitNewobjUnknown(ConstructorInfo ctor)
    {
        _il.Emit(OpCodes.Newobj, ctor);
        _stackType = StackType.Unknown;
    }

    private void EmitConvertToDouble()
    {
        _il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", [_types.Object]));
        _stackType = StackType.Number;
    }

    #endregion
}
