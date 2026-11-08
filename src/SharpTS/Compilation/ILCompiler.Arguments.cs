using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Runtime;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void EmitMappedArgumentsBindings(ILGenerator il, CompilationContext context,
        LocalBuilder? arguments, List<Stmt.Parameter> parameters, List<Stmt>? body)
    {
        if (arguments is null || context.IsStrictMode || context.Runtime is not { } runtime) return;
        foreach (var (index, parameter) in ParameterBinder.MappedArgumentParameters(parameters, body))
        {
            if (context.ClosureAnalyzer?.IsMappedArgumentParameter(parameter) != true
                || !context.ParameterCaptureCells.TryGetValue(parameter.Lexeme, out var cell)) continue;
            MappedArgumentsEmitter.EmitBind(il, runtime, context.Types, index,
                () => il.Emit(OpCodes.Ldloc, arguments), () => il.Emit(OpCodes.Ldloc, cell.Cell));
        }
    }

    private void DefineAsyncMappedParameterFields(StateMachineBuilderBase builder,
        List<Stmt.Parameter> parameters, List<Stmt>? body)
    {
        foreach (var (index, parameter) in ParameterBinder.MappedArgumentParameters(parameters, body))
        {
            if (!_closures.Analyzer.IsMappedArgumentParameter(parameter)) continue;
            Type referenceType = _debugLexicalCellStorages.GetValueOrDefault(parameter)?.ReferenceType
                ?? _types.StrongBoxOfObject;
            builder.DefineMappedArgumentField(index, parameter, referenceType);
        }
    }

    private void DefineAsyncArrowStub(AsyncArrowStateMachineBuilder builder)
    {
        if (builder.Arrow.HasOwnThis)
            DefineAsyncMappedParameterFields(builder, builder.Arrow.Parameters, builder.Arrow.BlockBody);
        builder.DefineStubMethod(_programType, _runtime, (il, stateMachine, parameterOffset) =>
        {
            if (!builder.Arrow.HasOwnThis || !builder.LocalFields.TryGetValue("arguments", out var field))
                return;
            var context = CreateModuleMemberContext(il, builder.StubMethod);
            var types = builder.StubMethod.GetParameters().Skip(parameterOffset)
                .Select(parameter => parameter.ParameterType).ToArray();
            // A capture/outer-state target occupies only a CLR parameter slot.
            // Invoke publishes the original user array without that target.
            var arguments = EmitArgumentsLocalPrologueCore(il, context, builder.Arrow.Parameters,
                types, parameterOffset);
            il.Emit(OpCodes.Ldloca, stateMachine);
            il.Emit(OpCodes.Ldloc, arguments);
            il.Emit(OpCodes.Stfld, field);
        });
        MarkNonConstructible(builder.StubMethod);
    }
}
