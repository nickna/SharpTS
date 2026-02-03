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

        // Handle Union types - distribute index access across all union members
        if (objType is TypeInfo.Union union)
        {
            List<TypeInfo> memberTypes = [];
            foreach (var member in union.FlattenedTypes)
            {
                try
                {
                    var memberType = CheckGetIndexOnType(member, indexType, getIndex);
                    if (memberType != null)
                    {
                        memberTypes.Add(memberType);
                    }
                    else
                    {
                        throw new TypeCheckException($" Index type '{indexType}' is not valid for indexing member '{member}' of union type.");
                    }
                }
                catch (TypeCheckException)
                {
                    throw new TypeCheckException($" Index type '{indexType}' is not valid for indexing all members of union type '{union}'.");
                }
            }
            // Return union of all member types
            var unique = memberTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            return unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique);
        }

        // Handle TypeParameter with constraint - delegate to constraint for indexing
        if (objType is TypeInfo.TypeParameter objTp)
        {
            // If index is a TypeParameter with keyof constraint matching this type param, allow it
            if (indexType is TypeInfo.TypeParameter indexTp && indexTp.Constraint is TypeInfo.KeyOf keyOf)
            {
                // keyof T where T is the same type parameter we're indexing - return the indexed access type
                if (keyOf.SourceType is TypeInfo.TypeParameter keyOfTp && keyOfTp.Name == objTp.Name)
                {
                    // Return IndexedAccess type that will be resolved when concrete types are provided
                    // For now, return Any since we can't know the exact property type until instantiation
                    return new TypeInfo.Any();
                }
            }

            // Delegate to constraint type if available
            if (objTp.Constraint != null)
            {
                var constrainedResult = CheckGetIndexOnType(objTp.Constraint, indexType, getIndex);
                if (constrainedResult != null) return constrainedResult;
            }

            // Check if the index type has a keyof constraint on this type parameter
            // This handles cases like T[K] where K extends keyof T, and T is unconstrained
            if (indexType is TypeInfo.TypeParameter indexTp2 && indexTp2.Constraint is TypeInfo.KeyOf keyOf2)
            {
                if (keyOf2.SourceType is TypeInfo.TypeParameter keyOfTp2 && keyOfTp2.Name == objTp.Name)
                {
                    // K extends keyof T and we're indexing T with K - allow it
                    return new TypeInfo.Any();
                }
            }

            // If index is a string/number type, return Any for generic flexibility
            if (IsString(indexType) || IsNumber(indexType))
            {
                return new TypeInfo.Any();
            }

            // Unconstrained type parameter can't be indexed with arbitrary types
            throw new TypeCheckException($" Cannot index type parameter '{objTp.Name}' with type '{indexType}'.");
        }

        // Handle TypeParameter index type with keyof constraint
        if (indexType is TypeInfo.TypeParameter indexTpOnly && indexTpOnly.Constraint is TypeInfo.KeyOf keyOfConstraint)
        {
            // Check if the keyof constraint's source type is compatible with objType
            var keyOfSourceType = keyOfConstraint.SourceType;
            if (keyOfSourceType is TypeInfo.TypeParameter)
            {
                // The keyof is on a type parameter - in generic context, allow it
                return new TypeInfo.Any();
            }
            // If we can verify the keyof source matches objType, allow it
            if (IsCompatible(keyOfSourceType, objType))
            {
                return new TypeInfo.Any();
            }
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

            // TypedArray index access returns number
            if (objType is TypeInfo.TypedArray)
            {
                return new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER);
            }

            // Buffer index access returns number (Buffer is a Uint8Array subclass)
            if (objType is TypeInfo.Buffer)
            {
                return new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER);
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
                return new TypeInfo.String();
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

        // Invalidate any narrowings affected by this index assignment
        var basePath = Narrowing.NarrowingPathExtractor.TryExtract(setIndex.Object);
        if (basePath != null)
        {
            Narrowing.NarrowingPath? assignedPath = null;

            // For numeric literal index (tuple/array access), create ElementAccess path
            if (setIndex.Index is Expr.Literal { Value: double d } && d == Math.Floor(d) && d >= 0)
            {
                assignedPath = new Narrowing.NarrowingPath.ElementAccess(basePath, (int)d);
            }
            // For string literal index, treat as property access
            else if (setIndex.Index is Expr.Literal { Value: string propName })
            {
                assignedPath = new Narrowing.NarrowingPath.PropertyAccess(basePath, propName);
            }
            else
            {
                // For computed index, conservatively invalidate the entire object's narrowings
                assignedPath = basePath;
            }

            InvalidateNarrowingsFor(assignedPath);
        }

        // Allow setting on 'any' type
        if (objType is TypeInfo.Any)
        {
            return valueType;
        }

        // Handle Union types - verify assignment is valid for all union members
        if (objType is TypeInfo.Union union)
        {
            foreach (var member in union.FlattenedTypes)
            {
                // Verify the index is valid for each member
                var memberIndexResult = CheckSetIndexOnType(member, indexType, valueType, setIndex);
                if (memberIndexResult == null)
                {
                    throw new TypeCheckException($" Index type '{indexType}' is not valid for assigning to member '{member}' of union type.");
                }
            }
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

            // TypedArray index assignment
            if (objType is TypeInfo.TypedArray typedArrayType)
            {
                // TypedArrays accept number values
                if (!IsNumber(valueType) && valueType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" Cannot assign '{valueType}' to {typedArrayType.ElementType}Array.");
                }
                return valueType;
            }

            // Buffer index assignment (Buffer is a Uint8Array subclass)
            if (objType is TypeInfo.Buffer)
            {
                // Buffer accepts number values
                if (!IsNumber(valueType) && valueType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" Cannot assign '{valueType}' to Buffer.");
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

    /// <summary>
    /// Checks index access on a given type (used for delegating from TypeParameter constraints).
    /// Returns null if the index type is not valid for the object type.
    /// </summary>
    private TypeInfo? CheckGetIndexOnType(TypeInfo objType, TypeInfo indexType, Expr.GetIndex getIndex)
    {
        // Recursive case for nested type parameters
        if (objType is TypeInfo.TypeParameter tp && tp.Constraint != null)
        {
            return CheckGetIndexOnType(tp.Constraint, indexType, getIndex);
        }

        // Handle string index
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            if (getIndex.Index is Expr.Literal { Value: string propName })
            {
                if (objType is TypeInfo.Record rec && rec.Fields.TryGetValue(propName, out var fieldType))
                    return fieldType;
                if (objType is TypeInfo.Interface itf && itf.Members.TryGetValue(propName, out var memberType))
                    return memberType;
            }
            if (objType is TypeInfo.Record rec2 && rec2.StringIndexType != null)
                return rec2.StringIndexType;
            if (objType is TypeInfo.Interface itf2 && itf2.StringIndexType != null)
                return itf2.StringIndexType;
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return new TypeInfo.Any();
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            if (objType is TypeInfo.Array arrayType)
                return arrayType.ElementType;
            // TypedArray index access returns number
            if (objType is TypeInfo.TypedArray)
                return new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER);
            // Buffer index access returns number (Buffer is a Uint8Array subclass)
            if (objType is TypeInfo.Buffer)
                return new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER);
            if (objType is TypeInfo.Interface itf3 && itf3.NumberIndexType != null)
                return itf3.NumberIndexType;
            if (objType is TypeInfo.Record rec3 && rec3.NumberIndexType != null)
                return rec3.NumberIndexType;
        }

        return null;
    }

    /// <summary>
    /// Checks index assignment on a given type (used for delegating from Union types).
    /// Returns the value type if assignment is valid, null otherwise.
    /// </summary>
    private TypeInfo? CheckSetIndexOnType(TypeInfo objType, TypeInfo indexType, TypeInfo valueType, Expr.SetIndex setIndex)
    {
        // Recursive case for nested type parameters
        if (objType is TypeInfo.TypeParameter tp && tp.Constraint != null)
        {
            return CheckSetIndexOnType(tp.Constraint, indexType, valueType, setIndex);
        }

        // Handle string index
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            if (objType is TypeInfo.Interface itf && itf.StringIndexType != null)
            {
                if (IsCompatible(itf.StringIndexType, valueType))
                    return valueType;
                return null;
            }
            if (objType is TypeInfo.Record rec && rec.StringIndexType != null)
            {
                if (IsCompatible(rec.StringIndexType, valueType))
                    return valueType;
                return null;
            }
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            if (objType is TypeInfo.Array arrayType)
            {
                if (IsCompatible(arrayType.ElementType, valueType))
                    return valueType;
                return null;
            }
            // TypedArray index assignment
            if (objType is TypeInfo.TypedArray)
            {
                if (IsNumber(valueType) || valueType is TypeInfo.Any)
                    return valueType;
                return null;
            }
            // Buffer index assignment
            if (objType is TypeInfo.Buffer)
            {
                if (IsNumber(valueType) || valueType is TypeInfo.Any)
                    return valueType;
                return null;
            }
            if (objType is TypeInfo.Interface itf2 && itf2.NumberIndexType != null)
            {
                if (IsCompatible(itf2.NumberIndexType, valueType))
                    return valueType;
                return null;
            }
            if (objType is TypeInfo.Record rec2 && rec2.NumberIndexType != null)
            {
                if (IsCompatible(rec2.NumberIndexType, valueType))
                    return valueType;
                return null;
            }
        }

        return null;
    }
}
