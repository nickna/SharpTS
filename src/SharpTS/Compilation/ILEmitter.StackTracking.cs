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

    // Specialized (unique to ILEmitter)
    private void EmitObjectEqualsBoxed_NoBox() => _helpers.EmitObjectEqualsBoxed_NoBox();

    #endregion
}
