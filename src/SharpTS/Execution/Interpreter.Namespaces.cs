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

        // Get or create namespace object — check ONLY the current scope, not up the chain.
        // GetNamespace walks up and would find a same-named outer namespace (e.g. top-level A
        // when declaring O.A), incorrectly merging the nested namespace into the outer one and
        // leaving the current scope without an "A" binding (#746).
        SharpTSNamespace? existingNs = _environment.GetLocalNamespace(name);
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

        // For merged namespaces, propagate existing nested namespaces into the new scope
        // This allows GetNamespace() to find previously-defined nested namespaces
        if (existingNs != null)
        {
            foreach (var (memberName, memberValue) in existingNs.Members)
            {
                if (memberValue is SharpTSNamespace nestedNs)
                {
                    namespaceEnv.DefineNamespace(memberName, nestedNs);
                }
            }
        }

        using (PushScope(namespaceEnv))
        {
            foreach (var member in ns.Members)
            {
                var result = ExecuteNamespaceMember(member, nsObj);
                if (result.IsAbrupt) return result;
            }
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
                Stmt.Const ct => ct.Name.Lexeme,  // namespace-scoped const (#467)
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
                    Stmt.Const ct => ct.Name,  // namespace-scoped const (#467)
                    Stmt.Enum e => e.Name,
                    Stmt.Namespace n => n.Name,
                    _ => null
                };

                if (token != null)
                {
                    object? value = _environment.Get(token).ToObject();
                    nsObj.Set(memberName, value);

                    // An exported mutable variable is a live view of the namespace binding,
                    // not a snapshot taken here: a member function that reassigns it must be
                    // visible through external `N.x` access too (#623). Capture the namespace
                    // scope so external reads resolve the current value. const/function/class/
                    // enum members never reassign, so they keep the snapshot stored above.
                    if (member is Stmt.Var)
                    {
                        var bindingEnv = _environment;
                        var bindingToken = token;
                        nsObj.SetLiveBinding(memberName, () => bindingEnv.Get(bindingToken).ToObject());
                    }
                }
                else if (member is Stmt.ImportAlias ia)
                {
                    // For import aliases, get from current environment (it was bound by ExecuteImportAlias)
                    // Use try/catch since RuntimeEnvironment throws if undefined
                    try
                    {
                        object? value = _environment.Get(ia.AliasName).ToObject();
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
            throw new InterpreterException($"Namespace '{path[0].Lexeme}' is not defined.");
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
                throw new InterpreterException($"'{path[i].Lexeme}' does not exist in namespace '{currentNs.Name}'.");
            }
            else
            {
                throw new InterpreterException($"'{path[i].Lexeme}' is not a namespace.");
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
