using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Namespace emission - EmitNamespace and related handlers.
/// Uses static fields defined by ILCompiler for namespace objects.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Emits IL for a namespace declaration.
    /// The namespace object has already been created and stored in a static field.
    /// This method emits the code to store members in the namespace.
    /// </summary>
    private void EmitNamespace(Stmt.Namespace ns, string parentPath = "")
    {
        string path = string.IsNullOrEmpty(parentPath)
            ? ns.Name.Lexeme
            : $"{parentPath}.{ns.Name.Lexeme}";

        // Get the namespace field (already defined and initialized)
        if (_ctx.NamespaceFields == null || !_ctx.NamespaceFields.TryGetValue(path, out var nsField))
        {
            // Namespace field not defined - skip (shouldn't happen)
            return;
        }

        // Emit namespace members
        foreach (var member in ns.Members)
        {
            EmitNamespaceMember(member, nsField, path);
        }
    }

    /// <summary>
    /// Emits IL for a namespace member and stores it in the namespace object.
    /// </summary>
    private void EmitNamespaceMember(Stmt member, FieldBuilder nsField, string nsPath)
    {
        // Unwrap export
        if (member is Stmt.Export export && export.Declaration != null)
        {
            member = export.Declaration;
        }

        // Get member name
        string? memberName = member switch
        {
            Stmt.Function f => f.Name.Lexeme,
            Stmt.Class c => c.Name.Lexeme,
            Stmt.Var v => v.Name.Lexeme,
            Stmt.Const ct => ct.Name.Lexeme,
            Stmt.Enum e => e.Name.Lexeme,
            Stmt.Namespace n => n.Name.Lexeme,
            Stmt.ImportAlias ia => ia.AliasName.Lexeme,
            _ => null
        };

        // Execute the member based on type
        switch (member)
        {
            case Stmt.Function funcStmt:
                // Functions are defined at compile-time, not via EmitStatement
                // Wrap as TSFunction and store in namespace
                if (_ctx.Functions.TryGetValue(_ctx.ResolveFunctionName(funcStmt.Name.Lexeme), out var methodBuilder))
                {
                    // nsField.Set(funcName, new TSFunction(null, methodInfo))
                    IL.Emit(OpCodes.Ldsfld, nsField);
                    IL.Emit(OpCodes.Ldstr, funcStmt.Name.Lexeme);
                    // Create TSFunction(null, methodInfo)
                    IL.Emit(OpCodes.Ldnull); // target (static method)
                    IL.Emit(OpCodes.Ldtoken, methodBuilder);
                    IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle));
                    IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);
                    IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.TSNamespaceSet);
                }
                break;

            case Stmt.Var varStmt:
                // Emit variable declaration
                EmitStatement(varStmt);
                StoreLocalInNamespaceField(nsField, memberName!);
                break;

            case Stmt.Const constStmt:
                // Emit const declaration
                EmitStatement(constStmt);
                StoreLocalInNamespaceField(nsField, memberName!);
                break;

            case Stmt.Class classStmt:
                // Classes are defined separately - store the Type in namespace
                if (_ctx.Classes.TryGetValue(_ctx.ResolveClassName(classStmt.Name.Lexeme), out var classType))
                {
                    IL.Emit(OpCodes.Ldsfld, nsField);
                    IL.Emit(OpCodes.Ldstr, classStmt.Name.Lexeme);
                    IL.Emit(OpCodes.Ldtoken, classType);
                    IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.TSNamespaceSet);
                }
                break;

            case Stmt.Enum enumStmt:
                // Create a Dictionary<string, object?> with enum members and store in namespace
                // This allows namespace.EnumName.MemberName access at runtime
                EmitEnumInNamespace(nsField, enumStmt);
                break;

            case Stmt.Namespace nestedNs:
                // Recursively emit nested namespace members
                // The nested namespace field is already initialized and stored in parent by InitializeNamespaceFields
                EmitNamespace(nestedNs, nsPath);
                break;

            case Stmt.Interface:
            case Stmt.TypeAlias:
                // Type-only, no runtime effect
                break;

            case Stmt.ImportAlias importAlias:
                // Emit the import alias and store in namespace if exported
                EmitImportAlias(importAlias);
                StoreLocalInNamespaceField(nsField, memberName!);
                break;
        }
    }

    /// <summary>
    /// Emits IL for an import alias declaration: import X = Namespace.Member
    /// Creates a local variable that references the namespace member.
    /// </summary>
    private void EmitImportAlias(Stmt.ImportAlias importAlias)
    {
        var path = importAlias.QualifiedPath;
        string aliasName = importAlias.AliasName.Lexeme;

        // Build namespace path (all but last element)
        string nsPath = string.Join(".", path.Take(path.Count - 1).Select(t => t.Lexeme));
        string memberName = path[^1].Lexeme;

        // Get namespace field
        if (_ctx.NamespaceFields == null || !_ctx.NamespaceFields.TryGetValue(nsPath, out var nsField))
        {
            // Namespace not found - could be type-only alias or compile-time alias
            // Just define an empty local that won't be used
            return;
        }

        // Create local for the alias
        var aliasLocal = _ctx.Locals.DeclareLocal(aliasName, _ctx.Types.Object);

        // Emit: aliasLocal = nsField.Get(memberName)
        IL.Emit(OpCodes.Ldsfld, nsField);
        IL.Emit(OpCodes.Ldstr, memberName);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.TSNamespaceGet);
        IL.Emit(OpCodes.Stloc, aliasLocal);
    }

    /// <summary>
    /// Emits IL to create an enum object (Dictionary) and store it in the namespace.
    /// </summary>
    private void EmitEnumInNamespace(FieldBuilder nsField, Stmt.Enum enumStmt)
    {
        // Get the qualified enum name and its members
        string qualifiedEnumName = _ctx.ResolveEnumName(enumStmt.Name.Lexeme);
        if (_ctx.EnumMembers == null ||
            !_ctx.EnumMembers.TryGetValue(qualifiedEnumName, out var members) ||
            members == null)
        {
            return; // No enum members found
        }

        // Create: new Dictionary<string, object?>()
        IL.Emit(OpCodes.Newobj, _ctx.Types.GetDefaultConstructor(_ctx.Types.DictionaryStringObject));

        // For each member, add to dictionary: dict[memberName] = value
        foreach (var (memberName, value) in members!)
        {
            IL.Emit(OpCodes.Dup); // Keep dictionary on stack
            IL.Emit(OpCodes.Ldstr, memberName);

            if (value is double d)
            {
                IL.Emit(OpCodes.Ldc_R8, d);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
            }
            else if (value is string s)
            {
                IL.Emit(OpCodes.Ldstr, s);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull);
            }

            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.DictionaryStringObject, "set_Item"));
        }

        // Store the dictionary in the namespace: nsField.Set(enumName, dict)
        // Stack: dict
        var dictLocal = IL.DeclareLocal(_ctx.Types.DictionaryStringObject);
        IL.Emit(OpCodes.Stloc, dictLocal);

        IL.Emit(OpCodes.Ldsfld, nsField);
        IL.Emit(OpCodes.Ldstr, enumStmt.Name.Lexeme);
        IL.Emit(OpCodes.Ldloc, dictLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.TSNamespaceSet);
    }

    /// <summary>
    /// Stores a value from a local variable into the namespace static field.
    /// </summary>
    private void StoreLocalInNamespaceField(FieldBuilder nsField, string memberName)
    {
        var memberLocal = _ctx.Locals.GetLocal(memberName);
        if (memberLocal != null)
        {
            // nsField.Set(memberName, value)
            IL.Emit(OpCodes.Ldsfld, nsField);
            IL.Emit(OpCodes.Ldstr, memberName);
            IL.Emit(OpCodes.Ldloc, memberLocal);
            if (memberLocal.LocalType.IsValueType)
            {
                IL.Emit(OpCodes.Box, memberLocal.LocalType);
            }
            IL.Emit(OpCodes.Call, _ctx.Runtime!.TSNamespaceSet);
        }
    }
}
