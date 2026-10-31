using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    private void RecordExternalInput(Expr? value, Type target, NativeClrAdmission.InputConversion conversion = NativeClrAdmission.InputConversion.Argument) =>
        _ctx.NativeClrAdmission?.RecordInput(CurrentSourceExpression, value, target, conversion, _stackType);

}
