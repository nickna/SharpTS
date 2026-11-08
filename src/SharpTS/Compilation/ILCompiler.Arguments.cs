using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Runtime;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private readonly HashSet<MethodBuilder> _argumentContextEntries = new(ReferenceEqualityComparer.Instance);

    private void MarkArgumentContextEntry(MethodBuilder method)
    {
        if (_argumentContextEntries.Add(method))
            method.SetCustomAttribute(_runtime.FunctionAttributes.CapturesArgumentsCtor, CustomAttributeEncoder.EmptyBlob);
    }

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
            context.IsStrictMode |= _strictArrows.Contains(builder.Arrow)
                || BodyDeclaresUseStrict(builder.Arrow.BlockBody);
            var types = builder.StubMethod.GetParameters().Skip(parameterOffset)
                .Select(parameter => parameter.ParameterType).ToArray();
            // A capture/outer-state target occupies only a CLR parameter slot.
            // Invoke publishes the original user array without that target.
            var arguments = EmitArgumentsLocalPrologueCore(il, context, builder.Arrow.Parameters,
                types, parameterOffset, body: builder.Arrow.BlockBody);
            il.Emit(OpCodes.Ldloca, stateMachine);
            il.Emit(OpCodes.Ldloc, arguments);
            il.Emit(OpCodes.Stfld, field);
        });
        MarkNonConstructible(builder.StubMethod);
    }

    private void EmitArgumentsCallee(ILGenerator il, CompilationContext context, EmittedRuntime runtime)
    {
        if (context.CurrentMethod is not MethodBuilder body)
            throw new InvalidOperationException("Mapped arguments require the invoking method.");

        var entry = _newTargetBodies.FirstOrDefault(pair => ReferenceEquals(pair.Value, body)).Key ?? body;
        MarkArgumentContextEntry(entry);
        var method = il.DeclareLocal(_types.MethodInfo);
        _types.EmitLoadMethodInfo(il, entry);
        il.Emit(OpCodes.Stloc, method);
        il.Emit(OpCodes.Ldloc, method);
        il.Emit(OpCodes.Call, runtime.Arguments.FindCallee);
        var haveCallee = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, haveCallee);
        il.Emit(OpCodes.Pop);

        // Direct declaration calls have no reflective wrapper on entry. Use
        // the declaration's normal function value when materializing callee.
        if (entry.IsStatic)
        {
            il.Emit(OpCodes.Ldloc, method);
            il.Emit(OpCodes.Ldstr, context.GetFunctionName(entry, entry.Name));
            il.Emit(OpCodes.Ldc_I4, context.GetFunctionLength(entry));
            NativeSourceFunctionEmission.EmitCachedFactory(il, _types, runtime, entry,
                generics: context.FunctionGenericParameters);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, method);
            NativeSourceFunctionEmission.EmitConstructor(il, _types, runtime, entry,
                generics: context.FunctionGenericParameters);
        }
        il.MarkLabel(haveCallee);
    }
}
