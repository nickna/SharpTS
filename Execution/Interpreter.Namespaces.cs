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
            }
        }
        
        return ExecutionResult.Success();
    }
}
