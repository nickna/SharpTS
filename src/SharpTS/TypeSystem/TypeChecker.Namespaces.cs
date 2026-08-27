using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Namespace type checking - CheckNamespace and related handlers.
/// </summary>
public partial class TypeChecker
{
    private TypeInfo.Namespace? _currentMergedNamespace;

    /// <summary>
    /// Type-checks a namespace declaration, handling declaration merging.
    /// Uses two-pass checking: first collect types, then full check for values.
    /// </summary>
    private void CheckNamespace(Stmt.Namespace ns)
    {
        string name = ns.Name.Lexeme;
        RegisterValueDeclaration(ns.Name, mergeWithLocal: true);

        // Get or create namespace - use mutable dictionaries for construction
        TypeInfo.Namespace? existingNs = _environment.GetNamespace(name);
        Dictionary<string, TypeInfo> types;
        Dictionary<string, TypeInfo> values;
        Dictionary<string, BindingSymbol> typeBindings;
        Dictionary<string, BindingSymbol> valueBindings;

        if (existingNs != null)
        {
            // Declaration merging - start with existing members
            types = new Dictionary<string, TypeInfo>(existingNs.Types);
            values = new Dictionary<string, TypeInfo>(existingNs.Values);
            typeBindings = existingNs.TypeBindings is null
                ? []
                : new Dictionary<string, BindingSymbol>(
                    existingNs.TypeBindings);
            valueBindings = existingNs.ValueBindings is null
                ? []
                : new Dictionary<string, BindingSymbol>(
                    existingNs.ValueBindings);
        }
        else
        {
            // New namespace
            types = new Dictionary<string, TypeInfo>();
            values = new Dictionary<string, TypeInfo>();
            typeBindings = [];
            valueBindings = [];
        }

        // Create new scope for namespace body
        var namespaceEnv = new TypeEnvironment(_environment);
        foreach (var (memberName, binding) in typeBindings)
            namespaceEnv.DefineTypeBinding(memberName, binding);
        foreach (var (memberName, binding) in valueBindings)
            namespaceEnv.DefineValueBinding(memberName, binding);

        // For merged namespaces, propagate existing nested namespaces into the new scope
        // This allows GetNamespace() to find previously-defined nested namespaces
        if (existingNs != null)
        {
            // Only exported class/enum/namespace members have both value and type
            // facets. Private variables from an earlier declaration body are not
            // visible in a reopened namespace and must not be seeded here.
            foreach (var (memberName, memberType) in existingNs.Values)
            {
                if (existingNs.Types.ContainsKey(memberName))
                    namespaceEnv.Define(memberName, memberType);
            }
            foreach (var (nestedName, nestedType) in existingNs.Types)
            {
                // Seed every existing type, not only nested namespaces. Interface
                // declaration merging inside a reopened namespace (notably global
                // JSX.IntrinsicElements from React plus a user augmentation) needs the
                // prior interface in the local environment so DefineOrMergeInterface can
                // combine its members instead of replacing it.
                namespaceEnv.DefineType(nestedName, nestedType);
                if (nestedType is TypeInfo.Namespace nestedNs)
                {
                    namespaceEnv.DefineNamespace(nestedName, nestedNs);
                }
            }
        }

        TypeInfo.Namespace? previousMergedNamespace = _currentMergedNamespace;
        _currentMergedNamespace = existingNs;
        _namespaceDepth++;
        try
        {
            using var namespaceScope = new EnvironmentScope(this, namespaceEnv);
            // Pre-register type declarations in the NAMESPACE scope (mirrors the top-level
            // pass): forward and self references (`interface S2 { foo: S2 }`) need the name
            // bound before members resolve, and it must bind to THIS namespace's declaration,
            // not a same-named one from an enclosing or earlier sibling scope.
            PreRegisterTypeDeclarations(ns.Members);

            // Hoist function declarations into the namespace scope before the first pass, mirroring
            // the top-level pass. The first pass fully checks any NESTED namespace (its bodies
            // resolve here), so a nested member's bare reference to an enclosing-namespace function
            // (`outerHelp()` from `O.I.f`) must already be bound — and functions are otherwise only
            // registered in the second pass, after nested namespaces are checked (#665).
            HoistFunctionDeclarations(ns.Members);

            // Hoist var declarations (as `any`) before anything resolves, mirroring the top-level
            // pass: a member's own annotation/initializer may reference itself or a later var
            // (`var a: { foo: typeof a }`, `var a2 = { foo: a2 }`).
            HoistVarDeclarations(ns.Members);

            // Likewise pre-register let/const members as `any` so a member function declared before a
            // later block-scoped binding can forward-reference it (#533).
            HoistLexicalDeclarations(ns.Members);

            // First pass: collect all type declarations (classes, interfaces, enums, nested namespaces)
            foreach (var member in ns.Members)
            {
                if (_recoveryMode)
                {
                    try { CollectNamespaceMemberType(member, types, values); }
                    catch (TypeMismatchException ex) { RecordTypeError(ex); }
                    catch (TypeCheckException ex) { RecordTypeError(ex); }
                }
                else
                {
                    CollectNamespaceMemberType(member, types, values);
                }
                CaptureNamespaceBindings(
                    namespaceEnv,
                    types,
                    values,
                    typeBindings,
                    valueBindings);
                // Self-bind THIS namespace's own name into its own body scope from the members
                // collected so far, so a later nested-namespace member body checked in this same
                // pass can reference the enclosing namespace by its own dotted name (`O.A.g()`
                // from inside O.B.f). Source order: `A` precedes `B` in the repro, so `A` is
                // already in `types` when B's body is checked. DefineNamespace merges on repeat,
                // so re-calling each iteration is safe. This binding lives only inside the
                // namespace scope and is discarded at scope exit; the outer binding still happens
                // after the body (below). Only first-pass `types` members (nested namespaces,
                // classes, interfaces, enums, type aliases) resolve via the own dotted name —
                // functions/vars enter `values` only in the second pass, so `O.outerFunc()` by the
                // enclosing namespace's own name is not covered (the bare form works via #665). (#745)
                _environment.DefineNamespace(name, new TypeInfo.Namespace(
                    name,
                    types.ToFrozenDictionary(),
                    values.ToFrozenDictionary(),
                    typeBindings.Count == 0
                        ? null
                        : typeBindings.ToFrozenDictionary(),
                    valueBindings.Count == 0
                        ? null
                        : valueBindings.ToFrozenDictionary()));
            }

            // Second pass: fully type-check all members. In recovery mode, check each member
            // independently so one type error doesn't abort the rest of the namespace.
            foreach (var member in ns.Members)
            {
                if (_recoveryMode)
                {
                    if (_diagnostics.HitErrorLimit) break;
                    int? saved = _currentStatementLine;
                    _currentStatementLine = TryGetStmtLine(member) ?? saved;
                    try { CheckNamespaceMember(member, values); }
                    catch (TypeMismatchException ex) { RecordTypeError(ex); }
                    catch (TypeCheckException ex) { RecordTypeError(ex); }
                    finally { _currentStatementLine = saved; }
                }
                else
                {
                    CheckNamespaceMember(member, values);
                }
                CaptureNamespaceBindings(
                    namespaceEnv,
                    types,
                    values,
                    typeBindings,
                    valueBindings);
            }
        }
        finally
        {
            _namespaceDepth--;
            _currentMergedNamespace = previousMergedNamespace;
        }

        // Create namespace with frozen collections
        var nsType = new TypeInfo.Namespace(
            name,
            types.ToFrozenDictionary(),
            values.ToFrozenDictionary(),
            typeBindings.Count == 0
                ? null
                : typeBindings.ToFrozenDictionary(),
            valueBindings.Count == 0
                ? null
                : valueBindings.ToFrozenDictionary());
        _environment.DefineNamespace(name, nsType);
    }

    private static void CaptureNamespaceBindings(
        TypeEnvironment environment,
        IReadOnlyDictionary<string, TypeInfo> types,
        IReadOnlyDictionary<string, TypeInfo> values,
        Dictionary<string, BindingSymbol> typeBindings,
        Dictionary<string, BindingSymbol> valueBindings)
    {
        foreach (string memberName in types.Keys)
        {
            if (environment.GetLocalTypeSymbol(memberName) is { } typeBinding)
                typeBindings[memberName] = typeBinding;
            if (environment.GetLocalValueBinding(memberName) is { } valueBinding)
                valueBindings[memberName] = valueBinding;
        }

        foreach (string memberName in values.Keys)
        {
            if (environment.GetLocalValueBinding(memberName) is { } valueBinding)
                valueBindings[memberName] = valueBinding;
            if (environment.GetLocalTypeSymbol(memberName) is { } typeBinding)
                typeBindings[memberName] = typeBinding;
        }
    }

    /// <summary>
    /// Collects type information from a namespace member (first pass).
    /// Registers classes, interfaces, enums, and nested namespaces.
    /// </summary>
    private void CollectNamespaceMemberType(
        Stmt member,
        Dictionary<string, TypeInfo> types,
        Dictionary<string, TypeInfo> values)
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
            case Stmt.Namespace nested:
                // Recursively handle nested namespace
                CheckNamespace(nested);
                var nestedNsType = _environment.GetNamespace(nested.Name.Lexeme);
                if (nestedNsType != null)
                {
                    types[nested.Name.Lexeme] = nestedNsType;
                    if (isExported)
                        values[nested.Name.Lexeme] = nestedNsType;
                }
                break;

            case Stmt.Class classStmt:
                // Register class type (full check in second pass)
                CheckClassSignature(classStmt, types);
                if (isExported && _environment.Get(classStmt.Name.Lexeme) is { } classValue)
                    values[classStmt.Name.Lexeme] = classValue;
                break;

            case Stmt.Interface interfaceStmt:
                CheckStmt(interfaceStmt);
                var ifaceType = _environment.GetTypeBinding(interfaceStmt.Name.Lexeme);
                if (ifaceType != null)
                {
                    types[interfaceStmt.Name.Lexeme] = ifaceType;
                }
                break;

            case Stmt.Enum enumStmt:
                CheckStmt(enumStmt);
                var enumType = _environment.GetTypeBinding(enumStmt.Name.Lexeme);
                if (enumType != null)
                {
                    types[enumStmt.Name.Lexeme] = enumType;
                    if (isExported)
                        values[enumStmt.Name.Lexeme] = enumType;
                }
                break;

            case Stmt.TypeAlias typeAlias:
                CheckStmt(typeAlias);
                // Resolve through the shared name resolver: node-first alias expansion with the
                // standard cache/recursion guards, instead of re-parsing the definition string.
                var aliasType = ResolveTypeName(typeAlias.Name.Lexeme);
                types[typeAlias.Name.Lexeme] = aliasType;
                break;

            case Stmt.ImportAlias importAlias:
                // Check the import alias in first pass
                CheckImportAlias(importAlias);
                // Add to types if it resolves to a type (class, interface, etc.)
                var importAliasInfo = _environment.GetImportAlias(importAlias.AliasName.Lexeme);
                if (importAliasInfo != null)
                {
                    types[importAlias.AliasName.Lexeme] = importAliasInfo.Value.Type;
                }
                break;
        }
    }

    /// <summary>
    /// Helper to check class signature and register in namespace without full body check.
    /// </summary>
    private void CheckClassSignature(Stmt.Class classStmt, Dictionary<string, TypeInfo> types)
    {
        // Full class check (will define the class type)
        CheckStmt(classStmt);
        var classType = _environment.GetTypeBinding(classStmt.Name.Lexeme);
        if (classType != null)
        {
            types[classStmt.Name.Lexeme] = classType;
        }
    }

    /// <summary>
    /// Type-checks a namespace member (second pass).
    /// Handles functions and variables that may reference types from first pass.
    /// </summary>
    private void CheckNamespaceMember(Stmt member, Dictionary<string, TypeInfo> values)
    {
        // Unwrap export statements
        bool isExported = false;
        if (member is Stmt.Export export && export.Declaration != null)
        {
            isExported = true;
            member = export.Declaration;
        }

        // Track the member's line so diagnostics raised while checking it (which often don't carry
        // their own line) are reported against the member, not the enclosing namespace.
        _currentStatementLine = TryGetStmtLine(member);

        switch (member)
        {
            case Stmt.Function funcStmt:
                CheckStmt(funcStmt);
                var funcType = _environment.Get(funcStmt.Name.Lexeme);
                if (funcType != null && isExported)
                {
                    values[funcStmt.Name.Lexeme] = funcType;
                }
                else if (funcType != null)
                {
                    // Non-exported members are still available within the namespace
                    values[funcStmt.Name.Lexeme] = funcType;
                }
                break;

            case Stmt.Var varStmt:
                CheckStmt(varStmt);
                var varType = _environment.Get(varStmt.Name.Lexeme);
                if (varType != null && isExported)
                {
                    values[varStmt.Name.Lexeme] = varType;
                }
                else if (varType != null)
                {
                    values[varStmt.Name.Lexeme] = varType;
                }
                break;

            // A namespace-scoped `const` (since #467) registers its binding in the namespace's
            // values exactly like `Stmt.Var`, but carries the narrowed literal type from const
            // checking (e.g. `export const x = 5` ⇒ `N.x` has type `5`, matching tsc). Without
            // this arm the const fell to `default` and was type-checked but never registered,
            // so `N.x` was not a visible member.
            case Stmt.Const constStmt:
                CheckStmt(constStmt);
                var constType = _environment.Get(constStmt.Name.Lexeme);
                if (constType != null)
                {
                    values[constStmt.Name.Lexeme] = constType;
                }
                break;

            case Stmt.Class:
            case Stmt.Namespace:
            case Stmt.Interface:
            case Stmt.Enum:
            case Stmt.TypeAlias:
            case Stmt.ImportAlias:
                // Already handled in first pass
                break;

            default:
                // Any other statement — `const`, expression statements (e.g. `s = t;`),
                // control flow — is type-checked normally so errors inside a namespace body
                // are reported just like at the top level.
                CheckStmt(member);
                break;
        }
    }

    /// <summary>
    /// Type-checks an import alias declaration: import X = A.B.C.member
    /// Resolves the namespace path and registers the alias.
    /// </summary>
    private void CheckImportAlias(Stmt.ImportAlias importAlias)
    {
        var path = importAlias.QualifiedPath;
        string aliasName = importAlias.AliasName.Lexeme;

        // Resolve the namespace path
        // First token should be a namespace
        TypeInfo.Namespace? currentNs = _environment.GetNamespace(path[0].Lexeme);
        if (currentNs == null)
        {
            throw new TypeCheckException(
                $"Type Error at line {path[0].Line}: Namespace '{path[0].Lexeme}' is not defined.", tsCode: "TS2503");
        }
        if (_environment.GetValueBinding(path[0].Lexeme) is { } rootValue)
            Bindings.Bind(path[0], CurrentSourceDocument, rootValue);
        if (_environment.GetTypeSymbol(path[0].Lexeme) is { } rootType)
            Bindings.Bind(path[0], CurrentSourceDocument, rootType);

        // Walk the path until the last element
        for (int i = 1; i < path.Count - 1; i++)
        {
            BindNamespaceMemberFacets(currentNs, path[i]);
            var memberType = currentNs.GetMember(path[i].Lexeme);
            if (memberType is TypeInfo.Namespace nestedNs)
            {
                currentNs = nestedNs;
            }
            else if (memberType == null)
            {
                throw new TypeCheckException(
                    $"Type Error at line {path[i].Line}: '{path[i].Lexeme}' does not exist in namespace '{currentNs.Name}'.", tsCode: "TS2694");
            }
            else
            {
                throw new TypeCheckException(
                    $"Type Error at line {path[i].Line}: '{path[i].Lexeme}' is not a namespace.", tsCode: "TS2503");
            }
        }

        // Get the final member
        Token finalMember = path[^1];
        BindNamespaceMemberFacets(currentNs, finalMember);
        TypeInfo? resolvedType = currentNs.Values.GetValueOrDefault(finalMember.Lexeme)
            ?? currentNs.Types.GetValueOrDefault(finalMember.Lexeme);

        if (resolvedType == null)
        {
            throw new TypeCheckException(
                $"Type Error at line {finalMember.Line}: Member '{finalMember.Lexeme}' does not exist in namespace '{currentNs.Name}'.", tsCode: "TS2694");
        }

        // Determine if this is a value alias (has runtime representation)
        bool isValue = resolvedType switch
        {
            TypeInfo.Function => true,
            TypeInfo.Class => true,
            TypeInfo.GenericClass => true,
            TypeInfo.Enum => true,
            TypeInfo.Namespace => true,  // Nested namespace is a value
            TypeInfo.Interface => false,  // Type-only
            TypeInfo.GenericInterface => false,  // Type-only
            _ => currentNs.Values.ContainsKey(finalMember.Lexeme)  // Check if in values dict
        };

        // Register the alias
        if (isValue)
            RegisterValueDeclaration(importAlias.AliasName);
        _environment.DefineImportAlias(aliasName, resolvedType, isValue);
    }

    private void BindNamespaceMemberFacets(
        TypeInfo.Namespace namespaceType,
        Token member)
    {
        if (namespaceType.GetValueBinding(member.Lexeme) is { } valueBinding)
            Bindings.Bind(member, CurrentSourceDocument, valueBinding);
        if (namespaceType.GetTypeBinding(member.Lexeme) is { } typeBinding)
            Bindings.Bind(member, CurrentSourceDocument, typeBinding);
    }
}
