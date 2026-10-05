using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    internal object? EvaluateClassInitializer(SharpTSClass owner, object receiver, Expr expression)
    {
        var environment = new RuntimeEnvironment(owner.InitializerEnvironment ?? _environment)
        {
            PrivateClass = owner
        };
        environment.Define("this", receiver);
        if (owner.Superclass is { } parent) environment.Define("super", parent);
        using (PushScope(environment)) return Evaluate(expression);
    }
}
