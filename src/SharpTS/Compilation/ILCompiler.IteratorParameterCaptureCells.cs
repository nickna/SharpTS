using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private readonly Dictionary<IIteratorStateMachineBuilder,
        Dictionary<Token, (string Storage, FieldBuilder Field)>> _iteratorParameterCaptureCells =
        new(ReferenceEqualityComparer.Instance);

    private void EmitIteratorParameterCaptureCellStores(ILGenerator il,
        IIteratorStateMachineBuilder builder, CompilationContext source)
    {
        foreach (var (storage, declaration) in source.LoopBodyCellDeclarations)
        {
            if (!source.CellBindingLocals.TryGetValue(storage, out var cell)) continue;
            if (builder.Constructor.DeclaringType is not TypeBuilder owner || owner.IsCreated())
                throw new InvalidOperationException("Iterator parameter cells require their actual unfinished iterator owner.");
            if (!_iteratorParameterCaptureCells.TryGetValue(builder, out var fields))
                _iteratorParameterCaptureCells[builder] = fields = new(ReferenceEqualityComparer.Instance);
            if (!fields.TryGetValue(declaration, out var target))
            {
                var field = owner.DefineField($"<>parameterCaptureCell{fields.Count}",
                    cell.LocalType, FieldAttributes.Assembly);
                fields.Add(declaration, target = (storage, field));
            }
            if (target.Storage != storage || target.Field.FieldType != cell.LocalType)
                throw new InvalidOperationException("Iterator parameter cells must preserve their actual declaration storage.");
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc, cell);
            il.Emit(OpCodes.Stfld, target.Field);
        }
    }

    private void ApplyIteratorParameterCaptureCells(CompilationContext context,
        IIteratorStateMachineBuilder builder)
    {
        if (!_iteratorParameterCaptureCells.TryGetValue(builder, out var fields)) return;
        if (!ReferenceEquals(context.CurrentMethod?.DeclaringType, builder.Constructor.DeclaringType))
            throw new InvalidOperationException("Iterator parameter cells belong to their actual MoveNext owner.");
        foreach (var (declaration, target) in fields)
        {
            context.LexicalCellStorageNames[declaration] = target.Storage;
            context.LoopBodyCellDeclarations[target.Storage] = declaration;
            context.LoopBodyCellFields[target.Storage] = target.Field;
        }
    }
}
