using SharpTS.Parsing;

namespace SharpTS.Execution;

public partial class Interpreter
{
    internal void SetWithBinding(object receiver, string name, object? value, bool strict) =>
        EvaluateSetOnObject(new Expr.Set(null!, new Token(TokenType.IDENTIFIER, name, null, 0), null!),
            receiver, value, forceStrict: strict);
}
