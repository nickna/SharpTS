using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Index access type checking - bracket notation for arrays, tuples, objects.
/// </summary>
/// <remarks>
/// Contains handlers for index operations:
/// CheckGetIndex, CheckSetIndex.
/// </remarks>
public partial class TypeChecker
{
    private TypeInfo CheckGetIndex(Expr.GetIndex getIndex)
    {
        TypeInfo objType = CheckExpr(getIndex.Object);
        TypeInfo indexType = CheckExpr(getIndex.Index);

        // Allow indexing on 'any' type (returns 'any')
        if (objType is TypeInfo.Any)
        {
            return new TypeInfo.Any();
        }

        // Handle string index on objects/interfaces
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            // String literal index - look up specific property
            if (getIndex.Index is Expr.Literal { Value: string propName })
            {
                if (objType is TypeInfo.Record rec && rec.Fields.TryGetValue(propName, out var fieldType))
                    return fieldType;
                if (objType is TypeInfo.Interface itf && itf.Members.TryGetValue(propName, out var memberType))
                    return memberType;
            }

            // Dynamic string index - use index signature if available
            if (objType is TypeInfo.Record rec2 && rec2.StringIndexType != null)
                return rec2.StringIndexType;
            if (objType is TypeInfo.Interface itf2 && itf2.StringIndexType != null)
                return itf2.StringIndexType;

            // Allow bracket access on any object/interface (returns any for unknown keys)
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return new TypeInfo.Any();
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // Tuple indexing with position-based types
            if (objType is TypeInfo.Tuple tupleType)
            {
                // Literal index -> exact element type
                if (getIndex.Index is Expr.Literal { Value: double idx })
                {
                    int i = (int)idx;
                    if (i >= 0 && i < tupleType.ElementTypes.Count)
                        return tupleType.ElementTypes[i];
                    if (tupleType.RestElementType != null && i >= tupleType.ElementTypes.Count)
                        return tupleType.RestElementType;
                    if (i < 0 || (tupleType.MaxLength != null && i >= tupleType.MaxLength))
                        throw new TypeCheckException($" Tuple index {i} is out of bounds.");
                }
                // Dynamic index -> union of all possible types
                var allTypes = tupleType.ElementTypes.ToList();
                if (tupleType.RestElementType != null)
                    allTypes.Add(tupleType.RestElementType);
                var unique = allTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
                return unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique);
            }

            if (objType is TypeInfo.Array arrayType)
            {
                return arrayType.ElementType;
            }

            // Enum reverse mapping: Direction[0] returns "Up" (only for numeric enums)
            if (objType is TypeInfo.Enum enumType)
            {
                // Const enums cannot use reverse mapping
                if (enumType.IsConst)
                {
                    throw new TypeCheckException($" A const enum member can only be accessed using its name, not by index. Cannot use reverse mapping on const enum '{enumType.Name}'.");
                }
                if (enumType.Kind == EnumKind.String)
                {
                    throw new TypeCheckException($" Reverse mapping is not supported for string enum '{enumType.Name}'.");
                }
                return new TypeInfo.Primitive(TokenType.TYPE_STRING);
            }

            // Number index signature on interface/record
            if (objType is TypeInfo.Interface itf3 && itf3.NumberIndexType != null)
                return itf3.NumberIndexType;
            if (objType is TypeInfo.Record rec3 && rec3.NumberIndexType != null)
                return rec3.NumberIndexType;
        }

        // Handle symbol index
        if (indexType is TypeInfo.Symbol)
        {
            if (objType is TypeInfo.Interface itf4 && itf4.SymbolIndexType != null)
                return itf4.SymbolIndexType;
            if (objType is TypeInfo.Record rec4 && rec4.SymbolIndexType != null)
                return rec4.SymbolIndexType;

            // Allow symbol bracket access on any object (returns any)
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return new TypeInfo.Any();
        }

        throw new TypeCheckException($" Index type '{indexType}' is not valid for indexing '{objType}'.");
    }

    private TypeInfo CheckSetIndex(Expr.SetIndex setIndex)
    {
        TypeInfo objType = CheckExpr(setIndex.Object);
        TypeInfo indexType = CheckExpr(setIndex.Index);
        TypeInfo valueType = CheckExpr(setIndex.Value);

        // Allow setting on 'any' type
        if (objType is TypeInfo.Any)
        {
            return valueType;
        }

        // Handle string index on objects/interfaces
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            // Check if value is compatible with string index signature
            if (objType is TypeInfo.Interface itf && itf.StringIndexType != null)
            {
                if (!IsCompatible(itf.StringIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to index signature type '{itf.StringIndexType}'.");
                return valueType;
            }
            if (objType is TypeInfo.Record rec && rec.StringIndexType != null)
            {
                if (!IsCompatible(rec.StringIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to index signature type '{rec.StringIndexType}'.");
                return valueType;
            }

            // Allow bracket assignment on any object/interface
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // Tuple index assignment
            if (objType is TypeInfo.Tuple tupleType)
            {
                // Literal index -> check against specific element type
                if (setIndex.Index is Expr.Literal { Value: double idx })
                {
                    int i = (int)idx;
                    if (i >= 0 && i < tupleType.ElementTypes.Count)
                    {
                        if (!IsCompatible(tupleType.ElementTypes[i], valueType))
                            throw new TypeCheckException($" Cannot assign '{valueType}' to tuple element of type '{tupleType.ElementTypes[i]}'.");
                        return valueType;
                    }
                    if (tupleType.RestElementType != null && i >= tupleType.ElementTypes.Count)
                    {
                        if (!IsCompatible(tupleType.RestElementType, valueType))
                            throw new TypeCheckException($" Cannot assign '{valueType}' to tuple rest element of type '{tupleType.RestElementType}'.");
                        return valueType;
                    }
                    throw new TypeCheckException($" Tuple index {i} is out of bounds.");
                }
                // Dynamic index -> value must be compatible with all possible element types
                var allTypes = tupleType.ElementTypes.ToList();
                if (tupleType.RestElementType != null)
                    allTypes.Add(tupleType.RestElementType);
                if (!allTypes.All(t => IsCompatible(t, valueType)))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to tuple with mixed element types.");
                return valueType;
            }

            if (objType is TypeInfo.Array arrayType)
            {
                if (!IsCompatible(arrayType.ElementType, valueType))
                {
                    throw new TypeCheckException($" Cannot assign '{valueType}' to array of '{arrayType.ElementType}'.");
                }
                return valueType;
            }

            // Number index signature on interface/record
            if (objType is TypeInfo.Interface itf2 && itf2.NumberIndexType != null)
            {
                if (!IsCompatible(itf2.NumberIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to number index signature type '{itf2.NumberIndexType}'.");
                return valueType;
            }
            if (objType is TypeInfo.Record rec2 && rec2.NumberIndexType != null)
            {
                if (!IsCompatible(rec2.NumberIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to number index signature type '{rec2.NumberIndexType}'.");
                return valueType;
            }
        }

        // Handle symbol index
        if (indexType is TypeInfo.Symbol)
        {
            if (objType is TypeInfo.Interface itf3 && itf3.SymbolIndexType != null)
            {
                if (!IsCompatible(itf3.SymbolIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to symbol index signature type '{itf3.SymbolIndexType}'.");
                return valueType;
            }
            if (objType is TypeInfo.Record rec3 && rec3.SymbolIndexType != null)
            {
                if (!IsCompatible(rec3.SymbolIndexType, valueType))
                    throw new TypeCheckException($" Cannot assign '{valueType}' to symbol index signature type '{rec3.SymbolIndexType}'.");
                return valueType;
            }

            // Allow symbol bracket assignment on any object
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        throw new TypeCheckException($" Index type '{indexType}' is not valid for assigning to '{objType}'.");
    }
}
