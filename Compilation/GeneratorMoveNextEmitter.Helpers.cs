using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    private void EnsureBoxed()
    {
        switch (_stackType)
        {
            case StackType.Double:
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case StackType.Boolean:
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
        }
        _stackType = StackType.Unknown;
    }

    private void EmitTruthyCheck()
    {
        // Call runtime IsTruthy method
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
        _stackType = StackType.Boolean;
    }
}
