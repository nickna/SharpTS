using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    #region Boxing and Type Conversion - Delegated to StateMachineEmitHelpers

    // Note: EnsureBoxed is inherited from ExpressionEmitterBase
    public new void EnsureDouble() => _helpers.EnsureDouble();
    public new void EnsureBoolean() => _helpers.EnsureBoolean();
    public new void EnsureString() => _helpers.EnsureString();

    #endregion

    #region ILEmitter-only Helpers - Delegated to StateMachineEmitHelpers

    // Arithmetic (box-and-return variants unique to ILEmitter)
    private void EmitAddAndBox() => _helpers.EmitAddAndBox();
    private void EmitSubAndBox() => _helpers.EmitSubAndBox();
    private void EmitMulAndBox() => _helpers.EmitMulAndBox();
    private void EmitDivAndBox() => _helpers.EmitDivAndBox();

    // Variable loads (unique to ILEmitter)
    private void EmitLdloc(LocalBuilder local, Type localType) => _helpers.EmitLdloc(local, localType);
    private void EmitLdarg0Unknown() => _helpers.EmitLdarg0Unknown();
    private void EmitLdsfldUnknown(FieldInfo field) => _helpers.EmitLdsfldUnknown(field);

    // Specialized (unique to ILEmitter)
    private void EmitObjectEqualsBoxed_NoBox() => _helpers.EmitObjectEqualsBoxed_NoBox();

    #endregion
}
