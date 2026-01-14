using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

/// <summary>
/// Namespace execution - ExecuteNamespace and related handlers.
/// </summary>
public partial class Interpreter
{
    /// <summary>
    /// Executes a namespace declaration, creating or merging the runtime namespace object.
    /// </summary>
    private ExecutionResult ExecuteNamespace(Stmt.Namespace ns)
    {
        string name = ns.Name.Lexeme;

        // Get or create namespace object
        SharpTSNamespace? existingNs = _environment.GetNamespace(name);
        SharpTSNamespace nsObj;

        if (existingNs != null)
        {
            // Declaration merging
            nsObj = existingNs;
        }
        else
        {
            nsObj = new SharpTSNamespace(name);
            _environment.DefineNamespace(name, nsObj);
        }

        // Create namespace scope
        var namespaceEnv = new RuntimeEnvironment(_environment);
        var savedEnv = _environment;
        _environment = namespaceEnv;

        try
        {
            foreach (var member in ns.Members)
            {
                var result = ExecuteNamespaceMember(member, nsObj);
                if (result.IsAbrupt) return result;
            }
        }
        finally
        {
            _environment = savedEnv;
        }
        
        return ExecutionResult.Success();
    }

    /// <summary>
    /// Executes a namespace member and adds it to the namespace object.
    /// </summary>
    private ExecutionResult ExecuteNamespaceMember(Stmt member, SharpTSNamespace nsObj)
    {
        bool isExported = false;

        // Unwrap export
        if (member is Stmt.Export export && export.Declaration != null)
        {
            isExported = true;
            member = export.Declaration;
        }

        // ImportAlias has its own IsExported flag
        if (member is Stmt.ImportAlias importAliasStmt && importAliasStmt.IsExported)
        {
            isExported = true;
        }

        // Execute the member
        var result = Execute(member);
        if (result.IsAbrupt) return result;

        // Add exported members to namespace object (or nested namespaces)
        // In TypeScript, only exported members are accessible via the namespace object
        // But nested namespaces are always accessible from parent namespace
        if (isExported || member is Stmt.Namespace)
        {
            string? memberName = member switch
            {
                Stmt.Function f => f.Name.Lexeme,
                Stmt.Class c => c.Name.Lexeme,
                Stmt.Var v => v.Name.Lexeme,
                Stmt.Enum e => e.Name.Lexeme,
                Stmt.Namespace n => n.Name.Lexeme,
                Stmt.Interface => null,  // Type-only, no runtime value
                Stmt.TypeAlias => null,  // Type-only, no runtime value
                Stmt.ImportAlias ia => ia.AliasName.Lexeme,
                _ => null
            };

            if (memberName != null)
            {
                // Get the value from the namespace scope
                var token = member switch
                {
                    Stmt.Function f => f.Name,
                    Stmt.Class c => c.Name,
                    Stmt.Var v => v.Name,
                    Stmt.Enum e => e.Name,
                    Stmt.Namespace n => n.Name,
                    _ => null
                };

                if (token != null)
                {
                    object? value = _environment.Get(token);
                    nsObj.Set(memberName, value);
                }
                else if (member is Stmt.ImportAlias ia)
                {
                    // For import aliases, get from current environment (it was bound by ExecuteImportAlias)
                    // Use try/catch since RuntimeEnvironment throws if undefined
                    try
                    {
                        object? value = _environment.Get(ia.AliasName);
                        nsObj.Set(memberName, value);
                    }
                    catch
                    {
                        // Type-only alias - no runtime value to export
                    }
                }
            }
        }

        return ExecutionResult.Success();
    }

    /// <summary>
    /// Executes an import alias declaration: import X = Namespace.Member
    /// Resolves the namespace path at runtime and binds the alias name.
    /// </summary>
    private ExecutionResult ExecuteImportAlias(Stmt.ImportAlias importAlias)
    {
        var path = importAlias.QualifiedPath;
        string aliasName = importAlias.AliasName.Lexeme;

        // Resolve the namespace path at runtime
        // Get the root namespace
        SharpTSNamespace? currentNs = _environment.GetNamespace(path[0].Lexeme);
        if (currentNs == null)
        {
            throw new Exception($"Runtime Error: Namespace '{path[0].Lexeme}' is not defined.");
        }

        // Walk to the final namespace
        for (int i = 1; i < path.Count - 1; i++)
        {
            object? member = currentNs.Get(path[i].Lexeme);
            if (member is SharpTSNamespace nested)
            {
                currentNs = nested;
            }
            else if (member == null)
            {
                throw new Exception($"Runtime Error: '{path[i].Lexeme}' does not exist in namespace '{currentNs.Name}'.");
            }
            else
            {
                throw new Exception($"Runtime Error: '{path[i].Lexeme}' is not a namespace.");
            }
        }

        // Get the final member value
        string finalMemberName = path[^1].Lexeme;
        object? value = currentNs.Get(finalMemberName);

        // If value exists, bind it to the alias name
        // (Type-only aliases like interfaces don't have runtime values)
        if (value != null)
        {
            _environment.Define(aliasName, value);
        }
        // Note: Type-only aliases are handled entirely at compile-time

        return ExecutionResult.Success();
    }
}
