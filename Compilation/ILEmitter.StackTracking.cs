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
    /// This method stays in ILEmitter because it requires access to _ctx.TypeMap.
    /// </summary>
    private StackType GetExpressionStackType(Expr expr)
    {
        var type = _ctx.TypeMap?.Get(expr);
        return type switch
        {
            TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => StackType.Double,
            TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => StackType.Boolean,
            TypeSystem.TypeInfo.String => StackType.String,
            TypeSystem.TypeInfo.Null => StackType.Null,
            _ => StackType.Unknown
        };
    }

    #endregion

    #region Boxing and Type Conversion - Delegated to StateMachineEmitHelpers

    private void EnsureBoxed() => _helpers.EnsureBoxed();
    public void EnsureDouble() => _helpers.EnsureDouble();
    public void EnsureBoolean() => _helpers.EnsureBoolean();
    public void EnsureString() => _helpers.EnsureString();

    #endregion

    #region Literal/Constant Helpers - Delegated to StateMachineEmitHelpers

    private void EmitBoxedDoubleConstant(double value) => _helpers.EmitBoxedDoubleConstant(value);
    private void EmitStringConstant(string value) => _helpers.EmitStringConstant(value);
    private void EmitDoubleConstant(double value) => _helpers.EmitDoubleConstant(value);
    private void EmitBoolConstant(bool value) => _helpers.EmitBoolConstant(value);
    private void EmitBoxedBoolConstant(bool value) => _helpers.EmitBoxedBoolConstant(value);
    private void EmitNullConstant() => _helpers.EmitNullConstant();

    #endregion

    #region Boxing Helpers - Delegated to StateMachineEmitHelpers

    private void EmitBoxDouble() => _helpers.EmitBoxDouble();
    private void EmitBoxBool() => _helpers.EmitBoxBool();
    private void SetStackUnknown() => _helpers.SetStackUnknown();
    private void SetStackType(StackType type) => _helpers.SetStackType(type);

    #endregion

    #region Arithmetic Helpers - Delegated to StateMachineEmitHelpers

    private void EmitAdd_Double() => _helpers.EmitAdd_Double();
    private void EmitSub_Double() => _helpers.EmitSub_Double();
    private void EmitMul_Double() => _helpers.EmitMul_Double();
    private void EmitDiv_Double() => _helpers.EmitDiv_Double();
    private void EmitRem_Double() => _helpers.EmitRem_Double();
    private void EmitNeg_Double() => _helpers.EmitNeg_Double();
    private void EmitAddAndBox() => _helpers.EmitAddAndBox();
    private void EmitSubAndBox() => _helpers.EmitSubAndBox();
    private void EmitMulAndBox() => _helpers.EmitMulAndBox();
    private void EmitDivAndBox() => _helpers.EmitDivAndBox();

    #endregion

    #region Comparison Helpers - Delegated to StateMachineEmitHelpers

    private void EmitClt_Boolean() => _helpers.EmitClt_Boolean();
    private void EmitCgt_Boolean() => _helpers.EmitCgt_Boolean();
    private void EmitCeq_Boolean() => _helpers.EmitCeq_Boolean();
    private void EmitLessOrEqual_Boolean() => _helpers.EmitLessOrEqual_Boolean();
    private void EmitGreaterOrEqual_Boolean() => _helpers.EmitGreaterOrEqual_Boolean();

    #endregion

    #region Variable Load Helpers - Delegated to StateMachineEmitHelpers

    private void EmitLdloc(LocalBuilder local, Type localType) => _helpers.EmitLdloc(local, localType);
    private void EmitLdlocUnknown(LocalBuilder local) => _helpers.EmitLdlocUnknown(local);
    private void EmitLdargUnknown(int argIndex) => _helpers.EmitLdargUnknown(argIndex);
    private void EmitLdarg0Unknown() => _helpers.EmitLdarg0Unknown();
    private void EmitLdfldUnknown(FieldInfo field) => _helpers.EmitLdfldUnknown(field);
    private void EmitLdsfldUnknown(FieldInfo field) => _helpers.EmitLdsfldUnknown(field);

    #endregion

    #region Method Call Helpers - Delegated to StateMachineEmitHelpers

    private void EmitCallUnknown(MethodInfo method) => _helpers.EmitCallUnknown(method);
    private void EmitCallvirtUnknown(MethodInfo method) => _helpers.EmitCallvirtUnknown(method);
    private void EmitCallString(MethodInfo method) => _helpers.EmitCallString(method);
    private void EmitCallBoolean(MethodInfo method) => _helpers.EmitCallBoolean(method);
    private void EmitCallDouble(MethodInfo method) => _helpers.EmitCallDouble(method);
    private void EmitCallAndBoxDouble(MethodInfo method) => _helpers.EmitCallAndBoxDouble(method);
    private void EmitCallAndBoxBool(MethodInfo method) => _helpers.EmitCallAndBoxBool(method);

    #endregion

    #region Specialized Helpers - Delegated to StateMachineEmitHelpers

    private void EmitNewobjUnknown(ConstructorInfo ctor) => _helpers.EmitNewobjUnknown(ctor);
    private void EmitConvertToDouble() => _helpers.EmitConvertToDouble();
    private void EmitConvR8AndBox() => _helpers.EmitConvR8AndBox();
    private void EmitObjectEqualsBoxed() => _helpers.EmitObjectEqualsBoxed();
    private void EmitObjectNotEqualsBoxed() => _helpers.EmitObjectNotEqualsBoxed();

    #endregion
}
