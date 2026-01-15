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

        // Handle generic type parameters
        List<TypeInfo.TypeParameter>? interfaceTypeParams = null;
        TypeEnvironment interfaceTypeEnv = new(_environment);
        if (interfaceStmt.TypeParams != null && interfaceStmt.TypeParams.Count > 0)
        {
            interfaceTypeParams = [];
            foreach (var tp in interfaceStmt.TypeParams)
            {
                // During pre-registration, we use a simple constraint parsing
                // that may fail on forward references - that's OK, we catch the error
                TypeInfo? constraint = null;
                try
                {
                    constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                }
                catch
                {
                    // Ignore constraint parsing errors during pre-registration
                }
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint);
                interfaceTypeParams.Add(typeParam);
                interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }
        }

        // Parse member types (may have forward references that resolve to Any, which is OK)
        Dictionary<string, TypeInfo> members = [];
        HashSet<string> optionalMembers = [];

        using (new EnvironmentScope(this, interfaceTypeEnv))
        {
            foreach (var member in interfaceStmt.Members)
            {
                try
                {
                    members[member.Name.Lexeme] = ToTypeInfo(member.Type);
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
            }
        }

        // Register the interface (skip index signatures during pre-registration - they'll be added during full check)
        if (interfaceTypeParams != null && interfaceTypeParams.Count > 0)
        {
            var genericItfType = new TypeInfo.GenericInterface(
                interfaceStmt.Name.Lexeme,
                interfaceTypeParams,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet()
            );
            _environment.Define(interfaceStmt.Name.Lexeme, genericItfType);
        }
        else
        {
            TypeInfo.Interface itfType = new(
                interfaceStmt.Name.Lexeme,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet()
            );
            _environment.Define(interfaceStmt.Name.Lexeme, itfType);
        }
    }

    private void CheckInterfaceDeclaration(Stmt.Interface interfaceStmt)
    {
        // Handle generic type parameters
        List<TypeInfo.TypeParameter>? interfaceTypeParams = null;
        TypeEnvironment interfaceTypeEnv = new(_environment);
        if (interfaceStmt.TypeParams != null && interfaceStmt.TypeParams.Count > 0)
        {
            interfaceTypeParams = [];
            foreach (var tp in interfaceStmt.TypeParams)
            {
                TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint);
                interfaceTypeParams.Add(typeParam);
                interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }
        }

        // Use interfaceTypeEnv for member type resolution so T resolves correctly
        Dictionary<string, TypeInfo> members = [];
        HashSet<string> optionalMembers = [];
        TypeInfo? stringIndexType = null;
        TypeInfo? numberIndexType = null;
        TypeInfo? symbolIndexType = null;

        using (new EnvironmentScope(this, interfaceTypeEnv))
        {
        foreach (var member in interfaceStmt.Members)
        {
            members[member.Name.Lexeme] = ToTypeInfo(member.Type);
            if (member.IsOptional)
            {
                optionalMembers.Add(member.Name.Lexeme);
            }
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

        // Create GenericInterface or regular Interface
        if (interfaceTypeParams != null && interfaceTypeParams.Count > 0)
        {
            var genericItfType = new TypeInfo.GenericInterface(
                interfaceStmt.Name.Lexeme,
                interfaceTypeParams,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet()
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
                symbolIndexType
            );
            _environment.Define(interfaceStmt.Name.Lexeme, itfType);
        }
    }
}
