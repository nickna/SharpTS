using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    private FieldBuilder? _enumInitializerField;
    private HashSet<string>? _enumInitializerMembers;

    private void EmitEnumDeclaration(Stmt.Enum declaration)
    {
        if (_ctx.EnumValueFields == null || !_ctx.EnumValueFields.TryGetValue(_ctx.ResolveEnumName(declaration.Name.Lexeme), out var field))
            return;

        // Reopened enum declarations extend the same object.
        var initialized = IL.DefineLabel();
        IL.Emit(OpCodes.Ldsfld, field);
        IL.Emit(OpCodes.Brtrue, initialized);
        IL.Emit(OpCodes.Newobj, Types.GetDefaultConstructor(Types.DictionaryStringObject));
        IL.Emit(OpCodes.Call, _ctx.Runtime!.ObjectConstruction.Create);
        IL.Emit(OpCodes.Stsfld, field);
        IL.MarkLabel(initialized);

        var previousField = _enumInitializerField;
        var previousMembers = _enumInitializerMembers;
        _enumInitializerField = field;
        _enumInitializerMembers = [];
        var value = IL.DeclareLocal(Types.Object);
        bool firstMember = true;
        foreach (var member in declaration.Members)
        {
            if (member.Value != null)
            {
                EmitExpression(member.Value);
                EnsureBoxed();
            }
            else
            {
                if (firstMember)
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                else
                {
                    IL.Emit(OpCodes.Ldloc, value);
                    IL.Emit(OpCodes.Unbox_Any, Types.Double);
                    IL.Emit(OpCodes.Ldc_R8, 1.0);
                    IL.Emit(OpCodes.Add);
                }
                IL.Emit(OpCodes.Box, Types.Double);
            }
            IL.Emit(OpCodes.Stloc, value);
            IL.Emit(OpCodes.Ldsfld, field);
            IL.Emit(OpCodes.Ldstr, member.Name.Lexeme);
            IL.Emit(OpCodes.Ldloc, value);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.ObjectWrite.Property);

            // String members have forward entries only. Numeric/computed members
            // also install the reverse entry, overwriting earlier duplicate values.
            if (member.Value is not Expr.Literal { Value: string })
            {
                IL.Emit(OpCodes.Ldsfld, field);
                IL.Emit(OpCodes.Ldloc, value);
                IL.Emit(OpCodes.Call, _ctx.Runtime.StringCoercion.ToJsString);
                IL.Emit(OpCodes.Ldstr, member.Name.Lexeme);
                IL.Emit(OpCodes.Call, _ctx.Runtime.ObjectWrite.Property);
            }
            _enumInitializerMembers.Add(member.Name.Lexeme);
            firstMember = false;
        }
        _enumInitializerField = previousField;
        _enumInitializerMembers = previousMembers;
        SetStackUnknown();
    }

    private bool TryEmitEnumInitializerMember(string name)
    {
        if (_enumInitializerField == null || _enumInitializerMembers?.Contains(name) != true)
            return false;
        IL.Emit(OpCodes.Ldsfld, _enumInitializerField);
        IL.Emit(OpCodes.Ldstr, name);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.ObjectRead.Property);
        SetStackUnknown();
        return true;
    }
}
