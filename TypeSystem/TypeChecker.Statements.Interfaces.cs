using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Interface declaration type checking - handles interface statements including members and index signatures.
/// </summary>
public partial class TypeChecker
{
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
        TypeEnvironment savedEnvForInterface = _environment;
        _environment = interfaceTypeEnv;

        Dictionary<string, TypeInfo> members = [];
        HashSet<string> optionalMembers = [];
        foreach (var member in interfaceStmt.Members)
        {
            members[member.Name.Lexeme] = ToTypeInfo(member.Type);
            if (member.IsOptional)
            {
                optionalMembers.Add(member.Name.Lexeme);
            }
        }

        // Process index signatures
        TypeInfo? stringIndexType = null;
        TypeInfo? numberIndexType = null;
        TypeInfo? symbolIndexType = null;
        if (interfaceStmt.IndexSignatures != null)
        {
            foreach (var indexSig in interfaceStmt.IndexSignatures)
            {
                TypeInfo valueType = ToTypeInfo(indexSig.ValueType);
                switch (indexSig.KeyType)
                {
                    case TokenType.TYPE_STRING:
                        if (stringIndexType != null)
                            throw new Exception($"Type Error: Duplicate string index signature in interface '{interfaceStmt.Name.Lexeme}'.");
                        stringIndexType = valueType;
                        break;
                    case TokenType.TYPE_NUMBER:
                        if (numberIndexType != null)
                            throw new Exception($"Type Error: Duplicate number index signature in interface '{interfaceStmt.Name.Lexeme}'.");
                        numberIndexType = valueType;
                        break;
                    case TokenType.TYPE_SYMBOL:
                        if (symbolIndexType != null)
                            throw new Exception($"Type Error: Duplicate symbol index signature in interface '{interfaceStmt.Name.Lexeme}'.");
                        symbolIndexType = valueType;
                        break;
                }
            }

            // TypeScript rule: number index type must be assignable to string index type
            if (stringIndexType != null && numberIndexType != null)
            {
                if (!IsCompatible(stringIndexType, numberIndexType))
                {
                    throw new Exception($"Type Error: Number index type '{numberIndexType}' is not assignable to string index type '{stringIndexType}' in interface '{interfaceStmt.Name.Lexeme}'.");
                }
            }

            // Validate explicit properties are compatible with string index signature
            if (stringIndexType != null)
            {
                foreach (var (name, type) in members)
                {
                    if (!IsCompatible(stringIndexType, type))
                    {
                        throw new Exception($"Type Error: Property '{name}' of type '{type}' is not assignable to string index type '{stringIndexType}' in interface '{interfaceStmt.Name.Lexeme}'.");
                    }
                }
            }
        }

        // Restore environment
        _environment = savedEnvForInterface;

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
