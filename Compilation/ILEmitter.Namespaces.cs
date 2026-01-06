using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// Namespace emission - EmitNamespace and related handlers.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Emits IL for a namespace declaration.
    /// Creates a SharpTSNamespace object and stores members in it.
    /// </summary>
    private void EmitNamespace(Stmt.Namespace ns)
    {
        string name = ns.Name.Lexeme;

        // Check if namespace already exists (declaration merging)
        LocalBuilder? nsLocal = _ctx.Locals.GetLocal(name);

        if (nsLocal == null)
        {
            // Create new SharpTSNamespace
            nsLocal = _ctx.Locals.DeclareLocal(name, typeof(SharpTSNamespace));

            // new SharpTSNamespace(name)
            IL.Emit(OpCodes.Ldstr, name);
            IL.Emit(OpCodes.Newobj, typeof(SharpTSNamespace).GetConstructor([typeof(string)])!);
            IL.Emit(OpCodes.Stloc, nsLocal);
        }

        // Emit namespace members
        foreach (var member in ns.Members)
        {
            EmitNamespaceMember(member, nsLocal);
        }
    }

    /// <summary>
    /// Emits IL for a namespace member and stores it in the namespace object.
    /// </summary>
    private void EmitNamespaceMember(Stmt member, LocalBuilder nsLocal)
    {
        bool isExported = false;

        // Unwrap export
        if (member is Stmt.Export export && export.Declaration != null)
        {
            isExported = true;
            member = export.Declaration;
        }

        // Get member name before execution
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
                // Emit function and store in namespace
                EmitStatement(funcStmt);
                if (isExported || true) // All members accessible in namespace
                {
                    StoreInNamespace(nsLocal, memberName!, funcStmt.Name.Lexeme);
                }
                break;

            case Stmt.Var varStmt:
                // Emit variable declaration
                EmitStatement(varStmt);
                if (isExported || true)
                {
                    StoreInNamespace(nsLocal, memberName!, varStmt.Name.Lexeme);
                }
                break;

            case Stmt.Class classStmt:
                // Classes in namespaces need to be handled specially
                // For now, just skip - class constructors are stored elsewhere
                // The class will be accessible via the generated type
                break;

            case Stmt.Enum enumStmt:
                // Enums are handled at compile time - skip here
                break;

            case Stmt.Namespace nestedNs:
                // Recursively emit nested namespace
                EmitNamespace(nestedNs);
                // Store nested namespace in parent
                var nestedLocal = _ctx.Locals.GetLocal(nestedNs.Name.Lexeme);
                if (nestedLocal != null)
                {
                    IL.Emit(OpCodes.Ldloc, nsLocal);
                    IL.Emit(OpCodes.Ldstr, nestedNs.Name.Lexeme);
                    IL.Emit(OpCodes.Ldloc, nestedLocal);
                    IL.Emit(OpCodes.Call, typeof(SharpTSNamespace).GetMethod("Set")!);
                }
                break;

            case Stmt.Interface:
            case Stmt.TypeAlias:
                // Type-only, no runtime effect
                break;
        }
    }

    /// <summary>
    /// Stores a value from a local variable into the namespace object.
    /// </summary>
    private void StoreInNamespace(LocalBuilder nsLocal, string memberName, string localName)
    {
        var memberLocal = _ctx.Locals.GetLocal(localName);
        if (memberLocal != null)
        {
            // nsLocal.Set(memberName, value)
            IL.Emit(OpCodes.Ldloc, nsLocal);
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
