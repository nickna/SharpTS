using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Interface declaration type checking - handles interface statements including members and index signatures.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Pre-registers an interface declaration before function hoisting.
    /// This creates a basic interface type without full validation so that
    /// function signatures can reference the interface name.
    /// Full validation happens later in CheckInterfaceDeclaration.
    /// </summary>
    private void PreRegisterInterface(Stmt.Interface interfaceStmt)
    {
        // Skip if already registered
        if (_environment.IsDefinedLocally(interfaceStmt.Name.Lexeme))
            return;

        // Handle generic type parameters with two-pass approach to support recursive constraints
        List<TypeInfo.TypeParameter>? interfaceTypeParams = null;
        TypeEnvironment interfaceTypeEnv = new(_environment);
        if (interfaceStmt.TypeParams != null && interfaceStmt.TypeParams.Count > 0)
        {
            interfaceTypeParams = [];

            // First pass: define all type parameters without constraints
            foreach (var tp in interfaceStmt.TypeParams)
            {
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, null, null, tp.IsConst, tp.Variance);
                interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }

            // Second pass: parse constraints (which may reference other type parameters)
            using (new EnvironmentScope(this, interfaceTypeEnv))
            {
                foreach (var tp in interfaceStmt.TypeParams)
                {
                    // During pre-registration, we use a simple constraint parsing
                    // that may fail on forward references - that's OK, we catch the error
                    TypeInfo? constraint = null;
                    TypeInfo? defaultType = null;
                    try
                    {
                        constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                        defaultType = tp.Default != null ? ToTypeInfo(tp.Default) : null;
                    }
                    catch
                    {
                        // Ignore constraint/default parsing errors during pre-registration
                    }
                    var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType, tp.IsConst, tp.Variance);
                    interfaceTypeParams.Add(typeParam);
                    // Redefine with the actual constraint
                    interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
                }
            }
        }

        // Parse member types (may have forward references that resolve to Any, which is OK)
        Dictionary<string, TypeInfo> members = [];
        Dictionary<string, List<TypeInfo.Function>> pendingOverloads = [];
        HashSet<string> optionalMembers = [];
        HashSet<string> readonlyMembers = [];

        using (new EnvironmentScope(this, interfaceTypeEnv))
        {
            foreach (var member in interfaceStmt.Members)
            {
                try
                {
                    var memberType = ToTypeInfo(member.Type);

                    // Check if this is a duplicate member name (overload)
                    if (members.TryGetValue(member.Name.Lexeme, out var existingType))
                    {
                        // This is an overloaded method - collect signatures
                        if (!pendingOverloads.TryGetValue(member.Name.Lexeme, out var overloadList))
                        {
                            overloadList = [];
                            pendingOverloads[member.Name.Lexeme] = overloadList;
                            if (existingType is TypeInfo.Function existingFunc)
                                overloadList.Add(existingFunc);
                        }
                        if (memberType is TypeInfo.Function newFunc)
                            overloadList.Add(newFunc);
                    }
                    else
                    {
                        members[member.Name.Lexeme] = memberType;
                    }
                }
                catch
                {
                    // If type parsing fails, use Any as placeholder
                    members[member.Name.Lexeme] = new TypeInfo.Any();
                }
                if (member.IsOptional)
                {
                    optionalMembers.Add(member.Name.Lexeme);
                }
                if (member.IsReadonly)
                {
                    readonlyMembers.Add(member.Name.Lexeme);
                }
            }

            // Convert collected overloads to OverloadedFunction types
            foreach (var (name, signatures) in pendingOverloads)
            {
                members[name] = new TypeInfo.OverloadedFunction(signatures, signatures[0]);
            }
        }

        // Resolve extended interfaces
        FrozenSet<TypeInfo.Interface>? extends = null;
        if (interfaceStmt.Extends != null && interfaceStmt.Extends.Count > 0)
        {
            var extendsList = new HashSet<TypeInfo.Interface>();
            foreach (var extendTypeName in interfaceStmt.Extends)
            {
                try
                {
                    var extendType = ToTypeInfo(extendTypeName);
                    if (extendType is TypeInfo.Interface extendInterface)
                    {
                        extendsList.Add(extendInterface);
                    }
                }
                catch
                {
                    // Ignore resolution errors during pre-registration
                }
            }
            if (extendsList.Count > 0)
            {
                extends = extendsList.ToFrozenSet();
            }
        }

        // Parse call signatures (skip during pre-registration - just add empty lists for now)
        List<TypeInfo.CallSignature>? callSignatures = null;
        List<TypeInfo.ConstructorSignature>? constructorSignatures = null;

        // Register the interface (skip index signatures during pre-registration - they'll be added during full check)
        if (interfaceTypeParams != null && interfaceTypeParams.Count > 0)
        {
            var genericItfType = new TypeInfo.GenericInterface(
                interfaceStmt.Name.Lexeme,
                interfaceTypeParams,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet(),
                CallSignatures: callSignatures,
                ConstructorSignatures: constructorSignatures,
                ReadonlyMembers: readonlyMembers.Count > 0 ? readonlyMembers.ToFrozenSet() : null
            );
            _environment.Define(interfaceStmt.Name.Lexeme, genericItfType);
        }
        else
        {
            TypeInfo.Interface itfType = new(
                interfaceStmt.Name.Lexeme,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet(),
                Extends: extends,
                CallSignatures: callSignatures,
                ConstructorSignatures: constructorSignatures,
                ReadonlyMembers: readonlyMembers.Count > 0 ? readonlyMembers.ToFrozenSet() : null
            );
            _environment.Define(interfaceStmt.Name.Lexeme, itfType);
        }
    }

    private void CheckInterfaceDeclaration(Stmt.Interface interfaceStmt)
    {
        // Handle generic type parameters with two-pass approach to support recursive constraints (e.g., T extends TreeNode<T>)
        List<TypeInfo.TypeParameter>? interfaceTypeParams = null;
        TypeEnvironment interfaceTypeEnv = new(_environment);
        if (interfaceStmt.TypeParams != null && interfaceStmt.TypeParams.Count > 0)
        {
            interfaceTypeParams = [];

            // First pass: define all type parameters without constraints so they can reference each other
            foreach (var tp in interfaceStmt.TypeParams)
            {
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, null, null, tp.IsConst, tp.Variance);
                interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }

            // Second pass: parse constraints (which may reference other type parameters, including themselves)
            using (new EnvironmentScope(this, interfaceTypeEnv))
            {
                foreach (var tp in interfaceStmt.TypeParams)
                {
                    TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                    TypeInfo? defaultType = tp.Default != null ? ToTypeInfo(tp.Default) : null;
                    var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType, tp.IsConst, tp.Variance);
                    interfaceTypeParams.Add(typeParam);
                    // Redefine with the actual constraint
                    interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
                }
            }
        }

        // Use interfaceTypeEnv for member type resolution so T resolves correctly
        Dictionary<string, TypeInfo> members = [];
        Dictionary<string, List<TypeInfo.Function>> pendingOverloads = []; // Track overloaded methods
        HashSet<string> optionalMembers = [];
        HashSet<string> readonlyMembers = [];
        TypeInfo? stringIndexType = null;
        TypeInfo? numberIndexType = null;
        TypeInfo? symbolIndexType = null;

        using (new EnvironmentScope(this, interfaceTypeEnv))
        {
        foreach (var member in interfaceStmt.Members)
        {
            var memberType = ToTypeInfo(member.Type);

            // Check if this is a duplicate member name (overload)
            if (members.TryGetValue(member.Name.Lexeme, out var existingType))
            {
                // This is an overloaded method - collect signatures
                if (!pendingOverloads.TryGetValue(member.Name.Lexeme, out var overloadList))
                {
                    overloadList = [];
                    pendingOverloads[member.Name.Lexeme] = overloadList;

                    // Add the first signature to the overload list
                    if (existingType is TypeInfo.Function existingFunc)
                        overloadList.Add(existingFunc);
                }

                // Add the new signature
                if (memberType is TypeInfo.Function newFunc)
                    overloadList.Add(newFunc);
            }
            else
            {
                members[member.Name.Lexeme] = memberType;
            }

            if (member.IsOptional)
            {
                optionalMembers.Add(member.Name.Lexeme);
            }

            if (member.IsReadonly)
            {
                readonlyMembers.Add(member.Name.Lexeme);
            }
        }

        // Convert collected overloads to OverloadedFunction types
        foreach (var (name, signatures) in pendingOverloads)
        {
            // Use the first signature as the "implementation" for the overloaded function
            // In interfaces, there's no true implementation, so we just need the signatures
            members[name] = new TypeInfo.OverloadedFunction(signatures, signatures[0]);
        }

        // Process index signatures
        if (interfaceStmt.IndexSignatures != null)
        {
            foreach (var indexSig in interfaceStmt.IndexSignatures)
            {
                TypeInfo valueType = ToTypeInfo(indexSig.ValueType);
                switch (indexSig.KeyType)
                {
                    case TokenType.TYPE_STRING:
                        if (stringIndexType != null)
                            throw new TypeCheckException($" Duplicate string index signature in interface '{interfaceStmt.Name.Lexeme}'.");
                        stringIndexType = valueType;
                        break;
                    case TokenType.TYPE_NUMBER:
                        if (numberIndexType != null)
                            throw new TypeCheckException($" Duplicate number index signature in interface '{interfaceStmt.Name.Lexeme}'.");
                        numberIndexType = valueType;
                        break;
                    case TokenType.TYPE_SYMBOL:
                        if (symbolIndexType != null)
                            throw new TypeCheckException($" Duplicate symbol index signature in interface '{interfaceStmt.Name.Lexeme}'.");
                        symbolIndexType = valueType;
                        break;
                }
            }

            // TypeScript rule: number index type must be assignable to string index type
            if (stringIndexType != null && numberIndexType != null)
            {
                if (!IsCompatible(stringIndexType, numberIndexType))
                {
                    throw new TypeCheckException($" Number index type '{numberIndexType}' is not assignable to string index type '{stringIndexType}' in interface '{interfaceStmt.Name.Lexeme}'.");
                }
            }

            // Validate explicit properties are compatible with string index signature
            if (stringIndexType != null)
            {
                foreach (var (name, type) in members)
                {
                    if (!IsCompatible(stringIndexType, type))
                    {
                        throw new TypeCheckException($" Property '{name}' of type '{type}' is not assignable to string index type '{stringIndexType}' in interface '{interfaceStmt.Name.Lexeme}'.");
                    }
                }
            }
        }
        }

        // Resolve extended interfaces
        FrozenSet<TypeInfo.Interface>? extends = null;
        if (interfaceStmt.Extends != null && interfaceStmt.Extends.Count > 0)
        {
            var extendsList = new HashSet<TypeInfo.Interface>();
            foreach (var extendTypeName in interfaceStmt.Extends)
            {
                var extendType = ToTypeInfo(extendTypeName);
                if (extendType is TypeInfo.Interface extendInterface)
                {
                    extendsList.Add(extendInterface);
                }
                else
                {
                    throw new TypeCheckException($" Interface '{interfaceStmt.Name.Lexeme}' can only extend other interfaces, but '{extendTypeName}' is not an interface.");
                }
            }
            extends = extendsList.ToFrozenSet();
        }

        // Process call signatures
        List<TypeInfo.CallSignature>? callSignatures = null;
        if (interfaceStmt.CallSignatures != null && interfaceStmt.CallSignatures.Count > 0)
        {
            callSignatures = [];
            using (new EnvironmentScope(this, interfaceTypeEnv))
            {
                foreach (var sig in interfaceStmt.CallSignatures)
                {
                    var sigTypeParams = ParseSignatureTypeParams(sig.TypeParams);
                    var paramTypes = sig.Parameters.Select(p => p.Type != null ? ToTypeInfo(p.Type) : new TypeInfo.Any()).ToList();
                    var returnType = ToTypeInfo(sig.ReturnType);
                    int requiredParams = sig.Parameters.TakeWhile(p => !p.IsOptional && p.DefaultValue == null).Count();
                    bool hasRestParam = sig.Parameters.Any(p => p.IsRest);
                    var paramNames = sig.Parameters.Select(p => p.Name.Lexeme).ToList();
                    callSignatures.Add(new TypeInfo.CallSignature(sigTypeParams, paramTypes, returnType, requiredParams, hasRestParam, paramNames));
                }
            }
        }

        // Process constructor signatures
        List<TypeInfo.ConstructorSignature>? constructorSignatures = null;
        if (interfaceStmt.ConstructorSignatures != null && interfaceStmt.ConstructorSignatures.Count > 0)
        {
            constructorSignatures = [];
            using (new EnvironmentScope(this, interfaceTypeEnv))
            {
                foreach (var sig in interfaceStmt.ConstructorSignatures)
                {
                    var sigTypeParams = ParseSignatureTypeParams(sig.TypeParams);
                    var paramTypes = sig.Parameters.Select(p => p.Type != null ? ToTypeInfo(p.Type) : new TypeInfo.Any()).ToList();
                    var returnType = ToTypeInfo(sig.ReturnType);
                    int requiredParams = sig.Parameters.TakeWhile(p => !p.IsOptional && p.DefaultValue == null).Count();
                    bool hasRestParam = sig.Parameters.Any(p => p.IsRest);
                    var paramNames = sig.Parameters.Select(p => p.Name.Lexeme).ToList();
                    constructorSignatures.Add(new TypeInfo.ConstructorSignature(sigTypeParams, paramTypes, returnType, requiredParams, hasRestParam, paramNames));
                }
            }
        }

        // Create GenericInterface or regular Interface
        if (interfaceTypeParams != null && interfaceTypeParams.Count > 0)
        {
            var genericItfType = new TypeInfo.GenericInterface(
                interfaceStmt.Name.Lexeme,
                interfaceTypeParams,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet(),
                stringIndexType,
                numberIndexType,
                symbolIndexType,
                extends,
                callSignatures,
                constructorSignatures,
                readonlyMembers.Count > 0 ? readonlyMembers.ToFrozenSet() : null
            );
            _environment.Define(interfaceStmt.Name.Lexeme, genericItfType);
        }
        else
        {
            TypeInfo.Interface itfType = new(
                interfaceStmt.Name.Lexeme,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet(),
                stringIndexType,
                numberIndexType,
                symbolIndexType,
                extends,
                callSignatures,
                constructorSignatures,
                readonlyMembers.Count > 0 ? readonlyMembers.ToFrozenSet() : null
            );
            _environment.Define(interfaceStmt.Name.Lexeme, itfType);
        }
    }

    /// <summary>
    /// Parses type parameters from a signature into TypeInfo.TypeParameter list.
    /// </summary>
    private List<TypeInfo.TypeParameter>? ParseSignatureTypeParams(List<TypeParam>? typeParams)
    {
        if (typeParams == null || typeParams.Count == 0)
            return null;

        List<TypeInfo.TypeParameter> result = [];
        foreach (var tp in typeParams)
        {
            TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
            TypeInfo? defaultType = tp.Default != null ? ToTypeInfo(tp.Default) : null;
            result.Add(new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType, tp.IsConst, tp.Variance));
        }
        return result;
    }
}
