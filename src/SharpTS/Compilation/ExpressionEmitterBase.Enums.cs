using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    protected bool TryEmitConstEnumMember(Expr receiver, string name)
    {
        // Const enums have no local slot. Resolve the checked binding even when
        // a different enum with the same name has a module-level runtime object.
        if (receiver is not Expr.Variable variable
            || Ctx.TypeMap?.GetDeclaredValueType(variable.Name) is not TypeInfo.Enum { IsConst: true } enumType
            || !ReferenceEquals(enumType.DeclarationBinding, Ctx.TypeMap.GetValueBinding(variable.Name))
            || !enumType.Members.TryGetValue(name, out var value))
            return false;

        EmitLiteral(new Expr.Literal(value));
        return true;
    }

    protected bool TryEmitEnumVariable(string name)
    {
        if (Ctx.EnumValueFields == null || !Ctx.EnumValueFields.TryGetValue(Ctx.ResolveEnumName(name), out var field))
            return false;

        // Ordinary enums have a hoisted value binding, initialized at their declaration.
        var initialized = IL.DefineLabel();
        IL.Emit(OpCodes.Ldsfld, field);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Brtrue, initialized);
        IL.Emit(OpCodes.Pop);
        EmitUndefinedConstant();
        IL.MarkLabel(initialized);
        SetStackUnknown();
        return true;
    }

    private Action? _loadEnumInitializerObject;
    private HashSet<string>? _enumInitializerMembers;

    protected void EmitStaticEnumDeclaration(Stmt.Enum declaration)
    {
        if (Ctx.EnumValueFields == null || !Ctx.EnumValueFields.TryGetValue(Ctx.ResolveEnumName(declaration.Name.Lexeme), out var field))
            return;

        // Reopened enum declarations extend the same object.
        var initialized = IL.DefineLabel();
        IL.Emit(OpCodes.Ldsfld, field);
        IL.Emit(OpCodes.Brtrue, initialized);
        IL.Emit(OpCodes.Newobj, Types.GetDefaultConstructor(Types.DictionaryStringObject));
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ObjectConstruction.Create);
        IL.Emit(OpCodes.Stsfld, field);
        IL.MarkLabel(initialized);

        EmitEnumMembers(declaration, () => IL.Emit(OpCodes.Ldsfld, field));
        if (Ctx.CurrentModulePath is { } modulePath &&
            Ctx.ModuleExportFields?.TryGetValue(modulePath, out var exports) == true &&
            exports.TryGetValue(declaration.Name.Lexeme, out var exportField))
        {
            IL.Emit(OpCodes.Ldsfld, field);
            IL.Emit(OpCodes.Stsfld, exportField);
        }
    }

    protected void EmitEnumMembers(Stmt.Enum declaration, Action loadObject)
    {
        var previousLoadObject = _loadEnumInitializerObject;
        var previousMembers = _enumInitializerMembers;
        _loadEnumInitializerObject = loadObject;
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
            loadObject();
            IL.Emit(OpCodes.Ldstr, member.Name.Lexeme);
            IL.Emit(OpCodes.Ldloc, value);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.ObjectWrite.Property);

            // String members have forward entries only. Numeric/computed members
            // also install the reverse entry, overwriting earlier duplicate values.
            if (member.Value is not Expr.Literal { Value: string })
            {
                loadObject();
                IL.Emit(OpCodes.Ldloc, value);
                IL.Emit(OpCodes.Call, Ctx.Runtime.StringCoercion.ToJsString);
                IL.Emit(OpCodes.Ldstr, member.Name.Lexeme);
                IL.Emit(OpCodes.Call, Ctx.Runtime.ObjectWrite.Property);
            }
            _enumInitializerMembers.Add(member.Name.Lexeme);
            firstMember = false;
        }
        _loadEnumInitializerObject = previousLoadObject;
        _enumInitializerMembers = previousMembers;
        SetStackUnknown();
    }

    protected bool TryEmitEnumInitializerMember(string name)
    {
        if (_loadEnumInitializerObject == null || _enumInitializerMembers?.Contains(name) != true)
            return false;
        _loadEnumInitializerObject();
        IL.Emit(OpCodes.Ldstr, name);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ObjectRead.Property);
        SetStackUnknown();
        return true;
    }
}
