using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Namespace type checking - CheckNamespace and related handlers.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Type-checks a namespace declaration, handling declaration merging.
    /// Uses two-pass checking: first collect types, then full check for values.
    /// </summary>
    private void CheckNamespace(Stmt.Namespace ns)
    {
        string name = ns.Name.Lexeme;

        // Get or create namespace type
        TypeInfo.Namespace? existingNs = _environment.GetNamespace(name);
        TypeInfo.Namespace nsType;

        if (existingNs != null)
        {
            // Declaration merging - reuse existing namespace
            nsType = existingNs;
        }
        else
        {
            // New namespace
            nsType = new TypeInfo.Namespace(
                name,
                new Dictionary<string, TypeInfo>(),
                new Dictionary<string, TypeInfo>()
            );
            _environment.DefineNamespace(name, nsType);
        }

        // Create new scope for namespace body
        var namespaceEnv = new TypeEnvironment(_environment);
        var savedEnv = _environment;
        _environment = namespaceEnv;

        try
        {
            // First pass: collect all type declarations (classes, interfaces, enums, nested namespaces)
            foreach (var member in ns.Members)
            {
                CollectNamespaceMemberType(member, nsType);
            }

            // Second pass: fully type-check all members
            foreach (var member in ns.Members)
            {
                CheckNamespaceMember(member, nsType);
            }
        }
        finally
        {
            _environment = savedEnv;
        }
    }

    /// <summary>
    /// Collects type information from a namespace member (first pass).
    /// Registers classes, interfaces, enums, and nested namespaces.
    /// </summary>
    private void CollectNamespaceMemberType(Stmt member, TypeInfo.Namespace nsType)
    {
        // Unwrap export statements
        if (member is Stmt.Export export && export.Declaration != null)
        {
            member = export.Declaration;
        }

        switch (member)
        {
            case Stmt.Namespace nested:
                // Recursively handle nested namespace
                CheckNamespace(nested);
                var nestedNsType = _environment.GetNamespace(nested.Name.Lexeme);
                if (nestedNsType != null)
                {
                    nsType.Types[nested.Name.Lexeme] = nestedNsType;
                }
                break;

            case Stmt.Class classStmt:
                // Register class type (full check in second pass)
                CheckClassSignature(classStmt, nsType);
                break;

            case Stmt.Interface interfaceStmt:
                CheckStmt(interfaceStmt);
                var ifaceType = _environment.Get(interfaceStmt.Name.Lexeme);
                if (ifaceType != null)
                {
                    nsType.Types[interfaceStmt.Name.Lexeme] = ifaceType;
                }
                break;

            case Stmt.Enum enumStmt:
                CheckStmt(enumStmt);
                var enumType = _environment.Get(enumStmt.Name.Lexeme);
                if (enumType != null)
                {
                    nsType.Types[enumStmt.Name.Lexeme] = enumType;
                }
                break;

            case Stmt.TypeAlias typeAlias:
                CheckStmt(typeAlias);
                var aliasType = ToTypeInfo(_environment.GetTypeAlias(typeAlias.Name.Lexeme)!);
                nsType.Types[typeAlias.Name.Lexeme] = aliasType;
                break;
        }
    }

    /// <summary>
    /// Helper to check class signature and register in namespace without full body check.
    /// </summary>
    private void CheckClassSignature(Stmt.Class classStmt, TypeInfo.Namespace nsType)
    {
        // Full class check (will define the class type)
        CheckStmt(classStmt);
        var classType = _environment.Get(classStmt.Name.Lexeme);
        if (classType != null)
        {
            nsType.Types[classStmt.Name.Lexeme] = classType;
        }
    }

    /// <summary>
    /// Type-checks a namespace member (second pass).
    /// Handles functions and variables that may reference types from first pass.
    /// </summary>
    private void CheckNamespaceMember(Stmt member, TypeInfo.Namespace nsType)
    {
        // Unwrap export statements
        bool isExported = false;
        if (member is Stmt.Export export && export.Declaration != null)
        {
            isExported = true;
            member = export.Declaration;
        }

        switch (member)
        {
            case Stmt.Function funcStmt:
                CheckStmt(funcStmt);
                var funcType = _environment.Get(funcStmt.Name.Lexeme);
                if (funcType != null && isExported)
                {
                    nsType.Values[funcStmt.Name.Lexeme] = funcType;
                }
                else if (funcType != null)
                {
                    // Non-exported members are still available within the namespace
                    nsType.Values[funcStmt.Name.Lexeme] = funcType;
                }
                break;

            case Stmt.Var varStmt:
                CheckStmt(varStmt);
                var varType = _environment.Get(varStmt.Name.Lexeme);
                if (varType != null && isExported)
                {
                    nsType.Values[varStmt.Name.Lexeme] = varType;
                }
                else if (varType != null)
                {
                    nsType.Values[varStmt.Name.Lexeme] = varType;
                }
                break;

            case Stmt.Class:
            case Stmt.Namespace:
            case Stmt.Interface:
            case Stmt.Enum:
            case Stmt.TypeAlias:
                // Already handled in first pass
                break;
        }
    }
}
