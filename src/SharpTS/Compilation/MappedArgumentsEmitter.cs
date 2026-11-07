using System.Globalization;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal static class MappedArgumentsEmitter
{
    internal static void EmitBind(ILGenerator il, EmittedRuntime runtime, int index,
        Action loadArguments, Action loadCell)
    {
        var storage = runtime.DescriptorStorage;
        var parameterCell = storage.DescriptorParameterCell
            ?? throw new InvalidOperationException("Mapped arguments require parameter cell metadata.");
        var skip = il.DefineLabel();
        loadArguments();
        il.Emit(OpCodes.Castclass, runtime.Arguments.Type);
        il.Emit(OpCodes.Ldfld, runtime.Arguments.LengthField);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ble, skip);
        loadArguments();
        il.Emit(OpCodes.Ldstr, index.ToString(CultureInfo.InvariantCulture));
        il.Emit(OpCodes.Newobj, storage.DescriptorConstructor);
        il.Emit(OpCodes.Dup);
        loadCell();
        il.Emit(OpCodes.Callvirt, parameterCell.GetSetMethod()!);
        il.Emit(OpCodes.Call, storage.DefineValidatedProperty);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skip);
    }

    internal static void EmitStateMachinePrologue(CompilationContext context, StateMachineBuilderBase builder)
    {
        if (builder.MappedArgumentFields.Count == 0) return;
        var il = context.IL;
        var arguments = builder.GetVariableField("arguments")
            ?? throw new InvalidOperationException("Mapped parameters require an arguments object.");
        foreach (var (parameter, (index, field)) in builder.MappedArgumentFields)
        {
            string name = parameter.Lexeme;
            var raw = builder.GetVariableField(name)
                ?? throw new InvalidOperationException($"Mapped parameter '{name}' requires a source slot.");
            var storage = LexicalCellStorage.ForDeclaration(context, parameter);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, raw);
            if (raw.FieldType.IsValueType)
                il.Emit(OpCodes.Box, raw.FieldType);
            il.Emit(OpCodes.Newobj, storage.Constructor);
            il.Emit(OpCodes.Stfld, field);
            context.LexicalCellStorageNames[parameter] = name;
            context.LoopBodyCellDeclarations[name] = parameter;
            context.LoopBodyCellFields[name] = field;
            if (context.ProjectedLexicalCellLocals.TryGetValue(parameter, out var alias))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
                il.Emit(OpCodes.Stloc, alias);
            }
            if (!context.IsStrictMode)
                EmitBind(il, context.Runtime!, index, () =>
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, arguments);
                }, () =>
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, field);
                });
        }
    }
}
