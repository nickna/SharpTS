using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    protected bool TryResolveExternalClassValue(Expr.Variable reference, out Type clrType)
    {
        var checkedType = Ctx.TypeMap?.Get(reference);
        if (checkedType is TypeInfo.Class imported &&
            DotNetTypeSynthesizer.TryGetClrClassType(imported, out clrType))
        {
            return true;
        }

        // Decorated declarations are registered under their exact checked identity.
        // A simple-name lookup could select another module's import or guest class.
        int? declarationId = checkedType switch
        {
            TypeInfo.Class cls => cls.Core.DeclarationId,
            TypeInfo.GenericClass cls => cls.Core.DeclarationId,
            TypeInfo.MutableClass cls => cls.DeclarationId,
            _ => null
        };
        if (declarationId is { } id &&
            Ctx.CheckedClassNames.TryGetValue(id, out var qualifiedName) &&
            Ctx.TypeMapper.ExternalTypes.TryGetValue(qualifiedName, out clrType!))
        {
            return true;
        }

        clrType = null!;
        return false;
    }

    protected bool TryEmitExternalClassValue(Expr.Variable reference)
    {
        if (!TryResolveExternalClassValue(reference, out var clrType))
            return false;

        Ctx.NativeClrAdmission?.RecordTypeValue(reference, clrType);
        IL.Emit(OpCodes.Ldtoken, clrType);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        SetStackUnknown();
        return true;
    }

    protected bool IsKnownVariable(Expr.Variable reference)
        => IsKnownVariable(reference.Name.Lexeme) || TryResolveExternalClassValue(reference, out _);
}
