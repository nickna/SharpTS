using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;

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
            Stmt.Enum e => e.Name.Lexeme,
            Stmt.Namespace n => n.Name.Lexeme,
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
                    IL.Emit(OpCodes.Call, typeof(SharpTSNamespace).GetMethod("Set")!);
                }
                break;

            case Stmt.Var varStmt:
                // Emit variable declaration
                EmitStatement(varStmt);
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
                    IL.Emit(OpCodes.Call, typeof(SharpTSNamespace).GetMethod("Set")!);
                }
                break;

            case Stmt.Enum enumStmt:
                // Enums are handled at compile time - store the enum values object if available
                // For now, skip - enums are accessed via special handling
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
        }
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
            IL.Emit(OpCodes.Call, typeof(SharpTSNamespace).GetMethod("Set")!);
        }
    }
}
