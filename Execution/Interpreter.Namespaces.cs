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
    private void ExecuteNamespace(Stmt.Namespace ns)
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
                ExecuteNamespaceMember(member, nsObj);
            }
        }
        finally
        {
            _environment = savedEnv;
        }
    }

    /// <summary>
    /// Executes a namespace member and adds it to the namespace object.
    /// </summary>
    private void ExecuteNamespaceMember(Stmt member, SharpTSNamespace nsObj)
    {
        bool isExported = false;

        // Unwrap export
        if (member is Stmt.Export export && export.Declaration != null)
        {
            isExported = true;
            member = export.Declaration;
        }

        // Execute the member
        Execute(member);

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
                try
                {
                    var token = member switch
                    {
                        Stmt.Function f => f.Name,
                        Stmt.Class c => c.Name,
                        Stmt.Var v => v.Name,
                        Stmt.Enum e => e.Name,
                        Stmt.Namespace n => n.Name,
                        _ => throw new Exception()
                    };
                    object? value = _environment.Get(token);
                    nsObj.Set(memberName, value);
                }
                catch
                {
                    // Ignore lookup errors for type-only declarations
                }
            }
        }
    }
}
