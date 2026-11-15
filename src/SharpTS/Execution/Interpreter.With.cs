using SharpTS.Parsing;

namespace SharpTS.Execution;

public partial class Interpreter
{
    internal object? CaptureWithCall(object receiver, string name) =>
        BindMemberCallReceiver(GetProperty(receiver, name), receiver);

    internal void SetWithBinding(object receiver, string name, object? value, bool strict) =>
        EvaluateSetOnObject(new Expr.Set(null!, new Token(TokenType.IDENTIFIER, name, null, 0), null!),
            receiver, value, forceStrict: strict);
}
