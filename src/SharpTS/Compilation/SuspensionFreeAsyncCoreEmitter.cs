using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits a synchronous primitive core for an async function after analysis proved every retained
/// await is a stable call to another suspension-free primitive core.
/// </summary>
internal sealed class SuspensionFreeAsyncCoreEmitter(CompilationContext context) : ILEmitter(context)
{
    protected override void EmitAwait(Expr.Await expression)
    {
        if (Ctx.SuspensionFreePrimitiveAsyncCoreAwaits?.Contains(expression) != true
            || expression.Expression is not Expr.Call
            {
                Callee: Expr.Variable variable,
                Arguments: var arguments
            })
        {
            throw new CompileException("Await is not suspension-free in this async core.");
        }

        string resolvedName = Ctx.ResolveFunctionName(variable.Name.Lexeme);
        Dictionary<string, MethodBuilder>? stableCores =
            Ctx.SuspensionFreePrimitiveAsyncCores;
        if (stableCores == null
            || !stableCores.TryGetValue(resolvedName, out MethodBuilder? coreMethod)
            || arguments.Count != coreMethod.GetParameters().Length)
        {
            throw new CompileException(
                "A pre-proven suspension-free async core call could not be emitted.");
        }

        ParameterInfo[] parameters = coreMethod.GetParameters();
        var argumentLocals = new LocalBuilder[arguments.Count];
        for (int index = 0; index < arguments.Count; index++)
        {
            EmitExpression(arguments[index]);
            EmitConversionForParameter(arguments[index], parameters[index].ParameterType);
            LocalBuilder local = IL.DeclareLocal(parameters[index].ParameterType);
            IL.Emit(OpCodes.Stloc, local);
            argumentLocals[index] = local;
        }
        foreach (LocalBuilder local in argumentLocals)
            IL.Emit(OpCodes.Ldloc, local);

        IL.Emit(OpCodes.Call, coreMethod);
        if (coreMethod.ReturnType == typeof(double))
            SetStackType(StackType.Double);
        else if (coreMethod.ReturnType == typeof(bool))
            SetStackType(StackType.Boolean);
        else if (coreMethod.ReturnType == typeof(string))
            SetStackType(StackType.String);
        else
            SetStackUnknown();
    }
}
