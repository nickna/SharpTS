using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class StateMachineExitRoutingEmitter
{
    protected override FieldBuilder DefineLexicalCellField(string name, Token declaration)
    {
        var storage = LexicalCellStorage.ForDeclaration(Ctx, declaration);
        if (storage.DebugHolderName is { } holderName)
        {
            if (Ctx.ProjectedLexicalCellFields.TryGetValue(declaration, out var field)) return field;
            // This holder points at the same cell forwarded to every closure. It is visible only
            // over the binding's lexical range; its payload is the source-named authoritative field.
            field = ((TypeBuilder)Ctx.CurrentMethod!.DeclaringType!).DefineField(
                holderName, storage.ReferenceType, FieldAttributes.Public);
            Ctx.ProjectedLexicalCellFields.Add(declaration, field);
            return field;
        }
        return DefineStateMachineField(name, storage.ReferenceType);
    }
}
