using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    internal void EmitWithEnvironmentCall(Expr.Call call, MethodInfo method)
    {
        var arguments = new List<LocalBuilder>();
        for (int index = 0; index < call.Arguments.Count; index++)
        {
            EmitExpression(call.Arguments[index]);
            EnsureBoxed();
            var local = IL.DeclareLocal(index == 1 ? Types.String : Types.Object);
            IL.Emit(OpCodes.Stloc, local);
            arguments.Add(local);
        }
        var result = IL.DeclareLocal(Types.Object);
        // Getter/proxy/setter effects can throw on either side of a suspension.
        // Enter the state-machine exception guard only once its stack is empty.
        EmitPropertyWriteOperation(() =>
        {
            foreach (LocalBuilder argument in arguments) IL.Emit(OpCodes.Ldloc, argument);
            IL.Emit(OpCodes.Call, method);
            IL.Emit(OpCodes.Stloc, result);
        });
        IL.Emit(OpCodes.Ldloc, result);
        SetStackUnknown();
    }
}
