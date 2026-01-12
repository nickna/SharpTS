using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// Namespace compilation methods for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Defines static fields for a namespace and its nested namespaces.
    /// Called during the definition phase to create module-level fields.
    /// </summary>
    private void DefineNamespaceFields(Stmt.Namespace ns, string parentPath = "")
    {
        string path = string.IsNullOrEmpty(parentPath)
            ? ns.Name.Lexeme
            : $"{parentPath}.{ns.Name.Lexeme}";

        // Create static field for this namespace if it doesn't exist
        if (!_namespaceFields.ContainsKey(path))
        {
            var field = _programType.DefineField(
                $"$ns_{path.Replace(".", "_")}",
                typeof(SharpTSNamespace),
                FieldAttributes.Public | FieldAttributes.Static);
            _namespaceFields[path] = field;
        }

        // Define namespace members (functions, classes, enums, nested namespaces)
        foreach (var member in ns.Members)
        {
            var actualMember = member;
            // Unwrap export
            if (member is Stmt.Export { Declaration: not null } exp)
            {
                actualMember = exp.Declaration;
            }

            switch (actualMember)
            {
                case Stmt.Namespace nested:
                    DefineNamespaceFields(nested, path);
                    break;

                case Stmt.Function funcStmt when funcStmt.Body != null:
                    // Define functions inside namespace
                    DefineFunction(funcStmt);
                    break;

                case Stmt.Class classStmt:
                    // Define classes inside namespace
                    DefineClass(classStmt);
                    break;

                case Stmt.Enum enumStmt:
                    // Define enums inside namespace
                    DefineEnum(enumStmt);
                    break;
            }
        }
    }

    /// <summary>
    /// Emits method bodies for functions and classes inside a namespace.
    /// Called during Phase 7 (method body emission).
    /// </summary>
    private void EmitNamespaceMemberBodies(Stmt.Namespace ns)
    {
        foreach (var member in ns.Members)
        {
            var actualMember = member;
            // Unwrap export
            if (member is Stmt.Export { Declaration: not null } exp)
            {
                actualMember = exp.Declaration;
            }

            switch (actualMember)
            {
                case Stmt.Namespace nested:
                    EmitNamespaceMemberBodies(nested);
                    break;

                case Stmt.Function funcStmt when funcStmt.Body != null:
                    EmitFunctionBody(funcStmt);
                    break;

                case Stmt.Class classStmt:
                    EmitClassMethods(classStmt);
                    break;
            }
        }
    }

    /// <summary>
    /// Initializes all namespace fields at the start of Main().
    /// Must be called before any namespace member access.
    /// </summary>
    private void InitializeNamespaceFields(ILGenerator il)
    {
        // Initialize namespace fields ordered by nesting depth (parents first)
        // This ensures parent namespaces exist before children are added
        foreach (var (nsPath, field) in _namespaceFields.OrderBy(kv => kv.Key.Count(c => c == '.')))
        {
            // Get simple name (last part of path)
            string simpleName = nsPath.Contains('.')
                ? nsPath[(nsPath.LastIndexOf('.') + 1)..]
                : nsPath;

            // new SharpTSNamespace(simpleName)
            il.Emit(OpCodes.Ldstr, simpleName);
            il.Emit(OpCodes.Newobj, typeof(SharpTSNamespace).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Stsfld, field);

            // If nested, add to parent: parent.Set(childName, child)
            int dotIndex = nsPath.LastIndexOf('.');
            if (dotIndex > 0)
            {
                string parentPath = nsPath[..dotIndex];
                if (_namespaceFields.TryGetValue(parentPath, out var parentField))
                {
                    il.Emit(OpCodes.Ldsfld, parentField);
                    il.Emit(OpCodes.Ldstr, simpleName);
                    il.Emit(OpCodes.Ldsfld, field);
                    il.Emit(OpCodes.Call, typeof(SharpTSNamespace).GetMethod("Set")!);
                }
            }
        }
    }
}
