using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem.Services.Compatibility;

/// <summary>
/// Implementation of type compatibility checking service.
/// </summary>
/// <remarks>
/// This service consolidates all type compatibility logic from the TypeChecker
/// partial class files into a testable, reusable service.
/// </remarks>
public sealed class CompatibilityService : ICompatibilityService, ITypePredicates
{
    private readonly TypeCheckingContext _context;
    private readonly Func<TypeInfo, TypeInfo> _expandRecursiveTypeAlias;
    private readonly Func<TypeInfo.KeyOf, TypeInfo> _evaluateKeyOf;
    private readonly Func<TypeInfo.MappedType, TypeInfo> _expandMappedType;
    private readonly Func<TypeInfo.IndexedAccess, Dictionary<string, TypeInfo>, TypeInfo> _resolveIndexedAccess;
    private readonly Func<TypeInfo.ConditionalType, TypeInfo> _evaluateConditionalType;
    private readonly Func<TypeInfo.IntrinsicStringType, TypeInfo> _evaluateIntrinsicStringType;

    /// <summary>
    /// Creates a new compatibility service.
    /// </summary>
    /// <param name="context">The shared type checking context.</param>
    /// <param name="expandRecursiveTypeAlias">Delegate to expand recursive type aliases.</param>
    /// <param name="evaluateKeyOf">Delegate to evaluate keyof types.</param>
    /// <param name="expandMappedType">Delegate to expand mapped types.</param>
    /// <param name="resolveIndexedAccess">Delegate to resolve indexed access types.</param>
    /// <param name="evaluateConditionalType">Delegate to evaluate conditional types.</param>
    /// <param name="evaluateIntrinsicStringType">Delegate to evaluate intrinsic string types.</param>
    public CompatibilityService(
        TypeCheckingContext context,
        Func<TypeInfo, TypeInfo> expandRecursiveTypeAlias,
        Func<TypeInfo.KeyOf, TypeInfo> evaluateKeyOf,
        Func<TypeInfo.MappedType, TypeInfo> expandMappedType,
        Func<TypeInfo.IndexedAccess, Dictionary<string, TypeInfo>, TypeInfo> resolveIndexedAccess,
        Func<TypeInfo.ConditionalType, TypeInfo> evaluateConditionalType,
        Func<TypeInfo.IntrinsicStringType, TypeInfo> evaluateIntrinsicStringType)
    {
        _context = context;
        _expandRecursiveTypeAlias = expandRecursiveTypeAlias;
        _evaluateKeyOf = evaluateKeyOf;
        _expandMappedType = expandMappedType;
        _resolveIndexedAccess = resolveIndexedAccess;
        _evaluateConditionalType = evaluateConditionalType;
        _evaluateIntrinsicStringType = evaluateIntrinsicStringType;
    }

    /// <inheritdoc/>
    public bool IsCompatible(TypeInfo expected, TypeInfo actual)
    {
        _context.EnsureCompatibilityCache();
        var key = (expected, actual);

        if (_context.CompatibilityCache!.TryGetValue(key, out var cached))
            return cached;

        var result = IsCompatibleCore(expected, actual);
        _context.CompatibilityCache[key] = result;
        return result;
    }

    /// <summary>
    /// Core type compatibility logic without caching.
    /// </summary>
    private bool IsCompatibleCore(TypeInfo expected, TypeInfo actual)
    {
        if (expected is TypeInfo.Any || actual is TypeInfo.Any) return true;

        // Expand recursive type aliases lazily
        if (expected is TypeInfo.RecursiveTypeAlias expectedRTA)
        {
            return IsCompatible(_expandRecursiveTypeAlias(expectedRTA), actual);
        }
        if (actual is TypeInfo.RecursiveTypeAlias actualRTA)
        {
            return IsCompatible(expected, _expandRecursiveTypeAlias(actualRTA));
        }

        // Type predicate compatibility
        if (expected is TypeInfo.TypePredicate pred)
        {
            if (pred.IsAssertion)
            {
                return actual is TypeInfo.Void or TypeInfo.Never;
            }
            else
            {
                return actual is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN }
                    or TypeInfo.BooleanLiteral;
            }
        }
        if (expected is TypeInfo.AssertsNonNull)
        {
            return actual is TypeInfo.Void or TypeInfo.Never;
        }

        // Type parameter compatibility
        if (expected is TypeInfo.TypeParameter expectedTp && actual is TypeInfo.TypeParameter actualTp)
        {
            return expectedTp.Name == actualTp.Name;
        }

        if (expected is TypeInfo.TypeParameter tp)
        {
            if (tp.Constraint != null)
                return IsCompatible(tp.Constraint, actual);
            return true;
        }

        if (actual is TypeInfo.TypeParameter actualTpOnly)
        {
            if (expected is TypeInfo.Any) return true;
            if (expected is TypeInfo.Union expUnionForTp)
            {
                return expUnionForTp.FlattenedTypes.Any(t =>
                    t is TypeInfo.TypeParameter unionTp && unionTp.Name == actualTpOnly.Name);
            }
            return false;
        }

        // never/unknown handling
        if (actual is TypeInfo.Never) return true;
        if (expected is TypeInfo.Never) return actual is TypeInfo.Never;
        if (expected is TypeInfo.Unknown) return true;
        if (actual is TypeInfo.Unknown)
            return expected is TypeInfo.Unknown || expected is TypeInfo.Any;

        // object type
        if (expected is TypeInfo.Object)
        {
            if (actual is TypeInfo.Never) return true;
            if (actual is TypeInfo.Any) return true;
            if (actual is TypeInfo.Object) return true;
            if (IsPrimitiveType(actual)) return false;
            if (actual is TypeInfo.Null or TypeInfo.Undefined) return false;
            return true;
        }

        if (actual is TypeInfo.Object)
        {
            return expected is TypeInfo.Object or TypeInfo.Any or TypeInfo.Unknown;
        }

        // Null/Undefined compatibility
        if (actual is TypeInfo.Null)
        {
            if (expected is TypeInfo.Union u && u.ContainsNull) return true;
            if (expected is TypeInfo.Null) return true;
            return false;
        }

        if (actual is TypeInfo.Undefined)
        {
            if (expected is TypeInfo.Union u && u.ContainsUndefined) return true;
            if (expected is TypeInfo.Undefined) return true;
            return false;
        }

        // Literal type compatibility
        if (expected is TypeInfo.StringLiteral sl1 && actual is TypeInfo.StringLiteral sl2)
            return sl1.Value == sl2.Value;
        if (expected is TypeInfo.NumberLiteral nl1 && actual is TypeInfo.NumberLiteral nl2)
            return nl1.Value == nl2.Value;
        if (expected is TypeInfo.BooleanLiteral bl1 && actual is TypeInfo.BooleanLiteral bl2)
            return bl1.Value == bl2.Value;

        // Literal to primitive widening
        if (expected is TypeInfo.String && actual is TypeInfo.StringLiteral)
            return true;
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } && actual is TypeInfo.NumberLiteral)
            return true;
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } && actual is TypeInfo.BooleanLiteral)
            return true;

        // Template literal type compatibility
        if (expected is TypeInfo.String && actual is TypeInfo.TemplateLiteralType)
            return true;

        if (expected is TypeInfo.TemplateLiteralType expectedTL && actual is TypeInfo.StringLiteral actualSL)
            return MatchesTemplateLiteralPattern(expectedTL, actualSL.Value);

        if (expected is TypeInfo.TemplateLiteralType expTL && actual is TypeInfo.TemplateLiteralType actTL)
            return TemplatePatternStructurallyCompatible(expTL, actTL);

        // Intrinsic string type
        if (actual is TypeInfo.IntrinsicStringType ist)
        {
            var evaluated = _evaluateIntrinsicStringType(ist);
            return IsCompatible(expected, evaluated);
        }

        // Union compatibility
        if (expected is TypeInfo.Union expectedUnion && actual is TypeInfo.Union actualUnion)
        {
            var expectedTypes = expectedUnion.FlattenedTypes;
            var actualTypes = actualUnion.FlattenedTypes;
            return actualTypes.All(actualType =>
                expectedTypes.Any(expectedType => IsCompatible(expectedType, actualType)));
        }

        if (expected is TypeInfo.Union expUnion)
        {
            var expTypes = expUnion.FlattenedTypes;
            return expTypes.Any(t => IsCompatible(t, actual));
        }

        if (actual is TypeInfo.Union actUnion)
        {
            var actTypes = actUnion.FlattenedTypes;
            return actTypes.All(t => IsCompatible(expected, t));
        }

        // Intersection compatibility
        if (expected is TypeInfo.Intersection expIntersection)
        {
            var expTypes = expIntersection.FlattenedTypes;
            return expTypes.All(t => IsCompatible(t, actual));
        }

        if (actual is TypeInfo.Intersection actIntersection)
        {
            var actTypes = actIntersection.FlattenedTypes;
            return actTypes.Any(t => IsCompatible(expected, t));
        }

        // KeyOf type compatibility
        if (expected is TypeInfo.KeyOf expectedKeyOf)
        {
            if (expectedKeyOf.SourceType is TypeInfo.TypeParameter)
            {
                return actual is TypeInfo.String or TypeInfo.StringLiteral or TypeInfo.Any or
                       TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or
                       TypeInfo.NumberLiteral or TypeInfo.Symbol or TypeInfo.TypeParameter;
            }
            TypeInfo expandedExpected = _evaluateKeyOf(expectedKeyOf);
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.KeyOf actualKeyOf)
        {
            if (actualKeyOf.SourceType is TypeInfo.TypeParameter)
            {
                return expected is TypeInfo.String or TypeInfo.StringLiteral or TypeInfo.Any or
                       TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or
                       TypeInfo.NumberLiteral or TypeInfo.Symbol or TypeInfo.KeyOf;
            }
            TypeInfo expandedActual = _evaluateKeyOf(actualKeyOf);
            return IsCompatible(expected, expandedActual);
        }

        // Mapped type compatibility
        if (expected is TypeInfo.MappedType expectedMapped)
        {
            TypeInfo expandedExpected = _expandMappedType(expectedMapped);
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.MappedType actualMapped)
        {
            TypeInfo expandedActual = _expandMappedType(actualMapped);
            return IsCompatible(expected, expandedActual);
        }

        // Indexed access type compatibility
        if (expected is TypeInfo.IndexedAccess expectedIA)
        {
            TypeInfo expandedExpected = _resolveIndexedAccess(expectedIA, new Dictionary<string, TypeInfo>());
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.IndexedAccess actualIA)
        {
            TypeInfo expandedActual = _resolveIndexedAccess(actualIA, new Dictionary<string, TypeInfo>());
            return IsCompatible(expected, expandedActual);
        }

        // Conditional type compatibility
        if (expected is TypeInfo.ConditionalType expectedCond)
        {
            TypeInfo expandedExpected = _evaluateConditionalType(expectedCond);
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.ConditionalType actualCond)
        {
            TypeInfo expandedActual = _evaluateConditionalType(actualCond);
            return IsCompatible(expected, expandedActual);
        }

        // InferredTypeParameter
        if (expected is TypeInfo.InferredTypeParameter || actual is TypeInfo.InferredTypeParameter)
        {
            return false;
        }

        // Enum compatibility
        if (expected is TypeInfo.Enum expectedEnum)
        {
            if (actual is TypeInfo.Enum actualEnum && expectedEnum.Name == actualEnum.Name)
                return true;

            if (expectedEnum.Kind == EnumKind.Numeric &&
                actual is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
                return true;

            if (expectedEnum.Kind == EnumKind.String && actual is TypeInfo.String)
                return true;

            if (expectedEnum.Kind == EnumKind.Heterogeneous &&
                (actual is TypeInfo.String || actual is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }))
                return true;

            return false;
        }

        if (actual is TypeInfo.Enum actualEnumType)
        {
            if (actualEnumType.Kind == EnumKind.Numeric &&
                expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
                return true;

            if (actualEnumType.Kind == EnumKind.String && expected is TypeInfo.String)
                return true;

            if (actualEnumType.Kind == EnumKind.Heterogeneous &&
                (expected is TypeInfo.String || expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }))
                return true;
        }

        // Primitive compatibility
        if (expected is TypeInfo.Primitive p1 && actual is TypeInfo.Primitive p2)
        {
            return p1.Type == p2.Type;
        }

        // String type compatibility
        if (expected is TypeInfo.String && actual is TypeInfo.String)
        {
            return true;
        }

        // UniqueSymbol compatibility
        if (expected is TypeInfo.UniqueSymbol expectedUnique)
        {
            if (actual is TypeInfo.UniqueSymbol actualUnique)
                return expectedUnique.DeclarationId == actualUnique.DeclarationId;
            return false;
        }

        // Symbol compatibility
        if (expected is TypeInfo.Symbol)
        {
            return actual is TypeInfo.Symbol or TypeInfo.UniqueSymbol;
        }

        // BigInt compatibility
        if (expected is TypeInfo.BigInt && actual is TypeInfo.BigInt)
        {
            return true;
        }

        // Promise compatibility
        if (expected is TypeInfo.Promise expPromise && actual is TypeInfo.Promise actPromise)
        {
            return IsCompatible(expPromise.ValueType, actPromise.ValueType);
        }

        // Map compatibility
        if (expected is TypeInfo.Map expMap && actual is TypeInfo.Map actMap)
        {
            return IsCompatible(expMap.KeyType, actMap.KeyType) &&
                   IsCompatible(expMap.ValueType, actMap.ValueType);
        }

        // Set compatibility
        if (expected is TypeInfo.Set expSet && actual is TypeInfo.Set actSet)
        {
            return IsCompatible(expSet.ElementType, actSet.ElementType);
        }

        // WeakMap compatibility
        if (expected is TypeInfo.WeakMap expWeakMap && actual is TypeInfo.WeakMap actWeakMap)
        {
            return IsCompatible(expWeakMap.KeyType, actWeakMap.KeyType) &&
                   IsCompatible(expWeakMap.ValueType, actWeakMap.ValueType);
        }

        // WeakSet compatibility
        if (expected is TypeInfo.WeakSet expWeakSet && actual is TypeInfo.WeakSet actWeakSet)
        {
            return IsCompatible(expWeakSet.ElementType, actWeakSet.ElementType);
        }

        // Instance compatibility
        if (expected is TypeInfo.Instance i1 && actual is TypeInfo.Instance i2)
        {
            return CheckInstanceCompatibility(i1, i2);
        }

        // Interface compatibility
        if (expected is TypeInfo.Interface itf)
        {
            return CheckInterfaceCompatibility(itf, actual);
        }

        // InstantiatedGeneric interface
        if (expected is TypeInfo.InstantiatedGeneric expectedInterfaceIG &&
            expectedInterfaceIG.GenericDefinition is TypeInfo.GenericInterface gi)
        {
            return CheckGenericInterfaceCompatibility(expectedInterfaceIG, gi, actual);
        }

        // Array compatibility
        if (expected is TypeInfo.Array a1 && actual is TypeInfo.Array a2)
        {
            return IsCompatible(a1.ElementType, a2.ElementType);
        }

        // Record-to-Record compatibility
        if (expected is TypeInfo.Record expRecord && actual is TypeInfo.Record actRecord)
        {
            return CheckRecordCompatibility(expRecord, actRecord);
        }

        // Record constraint compatibility
        if (expected is TypeInfo.Record expRec)
        {
            return CheckStructuralCompatibility(expRec.Fields, actual, expRec.OptionalFields);
        }

        // Tuple compatibility
        if (expected is TypeInfo.Tuple expTuple && actual is TypeInfo.Tuple actTuple)
        {
            return IsTupleCompatible(expTuple, actTuple);
        }

        // Tuple to array
        if (expected is TypeInfo.Array expArr && actual is TypeInfo.Tuple actTuple2)
        {
            return IsTupleToArrayCompatible(expArr, actTuple2);
        }

        // Array to tuple
        if (expected is TypeInfo.Tuple expTuple2 && actual is TypeInfo.Array actArr)
        {
            return IsArrayToTupleCompatible(expTuple2, actArr);
        }

        // Void compatibility
        if (expected is TypeInfo.Void && actual is TypeInfo.Void) return true;

        // OverloadedFunction compatibility
        if (expected is TypeInfo.OverloadedFunction overloadedFunc && actual is TypeInfo.Function actualFunc)
        {
            foreach (var signature in overloadedFunc.Signatures)
            {
                if (!IsFunctionCompatibleWithSignature(actualFunc, signature))
                    return false;
            }
            return true;
        }

        // Function compatibility
        if (expected is TypeInfo.Function f1 && actual is TypeInfo.Function f2)
        {
            return CheckFunctionCompatibility(f1, f2);
        }

        return false;
    }

    #region Instance Compatibility

    private bool CheckInstanceCompatibility(TypeInfo.Instance i1, TypeInfo.Instance i2)
    {
        // Handle InstantiatedGeneric expected type
        if (i1.ClassType is TypeInfo.InstantiatedGeneric expectedIG)
        {
            if (i2.ClassType is TypeInfo.InstantiatedGeneric actualIG)
            {
                if (expectedIG.GenericDefinition is TypeInfo.GenericClass gc1 &&
                    actualIG.GenericDefinition is TypeInfo.GenericClass gc2 &&
                    gc1.Name == gc2.Name)
                {
                    if (expectedIG.TypeArguments.Count != actualIG.TypeArguments.Count)
                        return false;
                    if (!AreTypeArgumentsCompatible(gc1.TypeParams, expectedIG.TypeArguments, actualIG.TypeArguments))
                        return false;
                    return true;
                }
                return IsInSuperclassChain(actualIG, expectedIG);
            }

            if (i2.ClassType is TypeInfo.Class actualClassForIG)
            {
                return IsInSuperclassChain(actualClassForIG, expectedIG);
            }
        }

        var resolvedExpected = i1.ResolvedClassType;
        var resolvedActual = i2.ResolvedClassType;

        if (resolvedExpected is TypeInfo.Class expectedClass && resolvedActual is TypeInfo.Class actualClass)
        {
            TypeInfo? current = actualClass;
            while (current != null)
            {
                if (current is TypeInfo.Class cls && cls.Name == expectedClass.Name) return true;
                current = GetSuperclass(current);
            }
        }
        else if (resolvedExpected is TypeInfo.MutableClass mc1 && resolvedActual is TypeInfo.MutableClass mc2)
        {
            return mc1.Name == mc2.Name;
        }

        return false;
    }

    #endregion

    #region Interface Compatibility

    private bool CheckInterfaceCompatibility(TypeInfo.Interface itf, TypeInfo actual)
    {
        if (itf.IsCallable && actual is TypeInfo.Function func)
        {
            return FunctionMatchesCallSignatures(func, itf.CallSignatures!);
        }

        if (itf.IsConstructable && actual is TypeInfo.Class cls)
        {
            return ClassMatchesConstructorSignatures(cls, itf.ConstructorSignatures!);
        }

        if (actual is TypeInfo.Interface actualItf)
        {
            var allExpectedMembers = itf.GetAllMembers().ToDictionary(m => m.Key, m => m.Value);
            var allExpectedOptional = itf.GetAllOptionalMembers().ToHashSet();
            var allActualMembers = actualItf.GetAllMembers().ToDictionary(m => m.Key, m => m.Value);

            foreach (var member in allExpectedMembers)
            {
                if (!allActualMembers.TryGetValue(member.Key, out var actualMemberType))
                {
                    if (!allExpectedOptional.Contains(member.Key))
                        return false;
                }
                else if (!IsCompatible(member.Value, actualMemberType))
                {
                    return false;
                }
            }
            return true;
        }

        var allMembers = itf.GetAllMembers().ToDictionary(m => m.Key, m => m.Value);
        var allOptional = itf.GetAllOptionalMembers().ToHashSet();
        return CheckStructuralCompatibility(allMembers, actual, allOptional);
    }

    private bool CheckGenericInterfaceCompatibility(
        TypeInfo.InstantiatedGeneric expectedInterfaceIG,
        TypeInfo.GenericInterface gi,
        TypeInfo actual)
    {
        if (actual is TypeInfo.InstantiatedGeneric actualInterfaceIG &&
            actualInterfaceIG.GenericDefinition is TypeInfo.GenericInterface actualGI &&
            gi.Name == actualGI.Name)
        {
            if (expectedInterfaceIG.TypeArguments.Count != actualInterfaceIG.TypeArguments.Count)
                return false;
            if (!AreTypeArgumentsCompatible(gi.TypeParams, expectedInterfaceIG.TypeArguments, actualInterfaceIG.TypeArguments))
                return false;
            return true;
        }

        Dictionary<string, TypeInfo> subs = [];
        for (int i = 0; i < gi.TypeParams.Count; i++)
            subs[gi.TypeParams[i].Name] = expectedInterfaceIG.TypeArguments[i];

        Dictionary<string, TypeInfo> substitutedMembers = [];
        foreach (var kvp in gi.Members)
            substitutedMembers[kvp.Key] = Substitute(kvp.Value, subs);

        return CheckStructuralCompatibility(substitutedMembers, actual, gi.OptionalMembers);
    }

    #endregion

    #region Record Compatibility

    private bool CheckRecordCompatibility(TypeInfo.Record expRecord, TypeInfo.Record actRecord)
    {
        foreach (var (name, expectedFieldType) in expRecord.Fields)
        {
            if (!actRecord.Fields.TryGetValue(name, out var actualFieldType))
            {
                if (!expRecord.IsFieldOptional(name))
                    return false;
            }
            else if (!IsCompatible(expectedFieldType, actualFieldType))
            {
                return false;
            }
        }
        return true;
    }

    #endregion

    #region Function Compatibility

    private bool CheckFunctionCompatibility(TypeInfo.Function f1, TypeInfo.Function f2)
    {
        if (f2.ParamTypes.Count > f1.ParamTypes.Count) return false;
        for (int i = 0; i < f2.ParamTypes.Count; i++)
        {
            if (!IsCompatible(f1.ParamTypes[i], f2.ParamTypes[i])) return false;
        }
        if (f1.ReturnType is TypeInfo.Void) return true;
        return IsCompatible(f1.ReturnType, f2.ReturnType);
    }

    private bool IsFunctionCompatibleWithSignature(TypeInfo.Function actualFunc, TypeInfo.Function signature)
    {
        if (actualFunc.ParamTypes.Count < signature.MinArity)
            return false;

        for (int i = 0; i < signature.ParamTypes.Count && i < actualFunc.ParamTypes.Count; i++)
        {
            if (!IsCompatible(actualFunc.ParamTypes[i], signature.ParamTypes[i]))
                return false;
        }

        if (signature.ReturnType is not TypeInfo.Void)
        {
            if (!IsCompatible(signature.ReturnType, actualFunc.ReturnType))
                return false;
        }

        return true;
    }

    private bool FunctionMatchesCallSignatures(TypeInfo.Function func, List<TypeInfo.CallSignature> callSignatures)
    {
        return callSignatures.Any(sig => FunctionMatchesCallSignature(func, sig));
    }

    private bool FunctionMatchesCallSignature(TypeInfo.Function func, TypeInfo.CallSignature sig)
    {
        if (sig.IsGeneric)
        {
            return false;
        }

        if (func.ParamTypes.Count < sig.MinArity)
            return false;

        if (!sig.HasRestParam && func.ParamTypes.Count > sig.ParamTypes.Count)
            return false;

        int paramCount = Math.Min(func.ParamTypes.Count, sig.ParamTypes.Count);
        for (int i = 0; i < paramCount; i++)
        {
            if (!IsCompatible(func.ParamTypes[i], sig.ParamTypes[i]))
                return false;
        }

        return IsCompatible(sig.ReturnType, func.ReturnType);
    }

    private bool ClassMatchesConstructorSignatures(TypeInfo.Class cls, List<TypeInfo.ConstructorSignature> constructorSignatures)
    {
        return constructorSignatures.Any(sig => ClassMatchesConstructorSignature(cls, sig));
    }

    private bool ClassMatchesConstructorSignature(TypeInfo.Class cls, TypeInfo.ConstructorSignature sig)
    {
        if (sig.IsGeneric)
        {
            return false;
        }

        if (!cls.Methods.TryGetValue("constructor", out var ctorTypeInfo))
        {
            return sig.MinArity == 0;
        }

        if (ctorTypeInfo is TypeInfo.OverloadedFunction overloadedCtor)
        {
            return overloadedCtor.Signatures.Any(ctorSig => ConstructorSignatureMatches(ctorSig, sig));
        }
        else if (ctorTypeInfo is TypeInfo.Function ctorFunc)
        {
            return ConstructorSignatureMatches(ctorFunc, sig);
        }

        return false;
    }

    private bool ConstructorSignatureMatches(TypeInfo.Function ctorFunc, TypeInfo.ConstructorSignature sig)
    {
        if (ctorFunc.ParamTypes.Count < sig.MinArity)
            return false;

        if (!sig.HasRestParam && ctorFunc.ParamTypes.Count > sig.ParamTypes.Count)
            return false;

        int paramCount = Math.Min(ctorFunc.ParamTypes.Count, sig.ParamTypes.Count);
        for (int i = 0; i < paramCount; i++)
        {
            if (!IsCompatible(ctorFunc.ParamTypes[i], sig.ParamTypes[i]))
                return false;
        }

        return true;
    }

    #endregion

    #region Tuple Compatibility

    private bool IsTupleCompatible(TypeInfo.Tuple expected, TypeInfo.Tuple actual)
    {
        if (expected.HasSpread || actual.HasSpread)
            return IsVariadicTupleCompatible(expected, actual);

        var expectedTypes = expected.Elements.Select(e => e.Type).ToList();
        var actualTypes = actual.Elements.Select(e => e.Type).ToList();

        if (actualTypes.Count < expected.RequiredCount) return false;

        if (expected.MaxLength != null && actualTypes.Count > expected.MaxLength) return false;

        int minLen = Math.Min(expectedTypes.Count, actualTypes.Count);
        for (int i = 0; i < minLen; i++)
        {
            if (!IsCompatible(expectedTypes[i], actualTypes[i]))
                return false;
        }

        if (expected.RestElementType != null)
        {
            for (int i = expectedTypes.Count; i < actualTypes.Count; i++)
            {
                if (!IsCompatible(expected.RestElementType, actualTypes[i]))
                    return false;
            }
            if (actual.RestElementType != null &&
                !IsCompatible(expected.RestElementType, actual.RestElementType))
                return false;
        }

        return true;
    }

    private bool IsVariadicTupleCompatible(TypeInfo.Tuple expected, TypeInfo.Tuple actual)
    {
        var expectedLeading = expected.Elements.TakeWhile(e => e.Kind != TupleElementKind.Spread).ToList();
        var actualLeading = actual.Elements.TakeWhile(e => e.Kind != TupleElementKind.Spread).ToList();

        int leadingMin = Math.Min(expectedLeading.Count, actualLeading.Count);
        for (int i = 0; i < leadingMin; i++)
        {
            if (!IsCompatible(expectedLeading[i].Type, actualLeading[i].Type))
                return false;
        }

        var expectedTrailing = expected.Elements.AsEnumerable().Reverse().TakeWhile(e => e.Kind != TupleElementKind.Spread).Reverse().ToList();
        var actualTrailing = actual.Elements.AsEnumerable().Reverse().TakeWhile(e => e.Kind != TupleElementKind.Spread).Reverse().ToList();

        int trailingMin = Math.Min(expectedTrailing.Count, actualTrailing.Count);
        for (int i = 0; i < trailingMin; i++)
        {
            int expectedIdx = expectedTrailing.Count - 1 - i;
            int actualIdx = actualTrailing.Count - 1 - i;
            if (!IsCompatible(expectedTrailing[expectedIdx].Type, actualTrailing[actualIdx].Type))
                return false;
        }

        var expectedSpreads = expected.Elements.Where(e => e.Kind == TupleElementKind.Spread).Select(e => e.Type).ToList();
        var actualSpreads = actual.Elements.Where(e => e.Kind == TupleElementKind.Spread).Select(e => e.Type).ToList();

        if (expectedSpreads.Count > 0 && actualSpreads.Count > 0)
        {
            for (int i = 0; i < expectedSpreads.Count && i < actualSpreads.Count; i++)
            {
                if (!IsCompatible(expectedSpreads[i], actualSpreads[i]))
                    return false;
            }
        }

        return true;
    }

    private bool IsTupleToArrayCompatible(TypeInfo.Array expected, TypeInfo.Tuple actual)
    {
        foreach (var elem in actual.Elements)
        {
            var elemType = elem.IsSpread && elem.Type is TypeInfo.Array arrType
                ? arrType.ElementType
                : elem.Type;
            if (!IsCompatible(expected.ElementType, elemType))
                return false;
        }
        if (actual.RestElementType != null &&
            !IsCompatible(expected.ElementType, actual.RestElementType))
            return false;
        return true;
    }

    private bool IsArrayToTupleCompatible(TypeInfo.Tuple expected, TypeInfo.Array actual)
    {
        if (expected.RequiredCount > 0 && expected.RestElementType == null && !expected.HasSpread)
            return false;

        foreach (var elem in expected.Elements)
        {
            var elemType = elem.IsSpread && elem.Type is TypeInfo.Array arrType
                ? arrType.ElementType
                : elem.Type;
            if (!IsCompatible(elemType, actual.ElementType))
                return false;
        }
        if (expected.RestElementType != null &&
            !IsCompatible(expected.RestElementType, actual.ElementType))
            return false;
        return true;
    }

    #endregion

    #region Template Literal Compatibility

    private bool MatchesTemplateLiteralPattern(TypeInfo.TemplateLiteralType pattern, string value)
    {
        int pos = 0;

        for (int i = 0; i < pattern.InterpolatedTypes.Count; i++)
        {
            string prefix = pattern.Strings[i];

            if (pos + prefix.Length > value.Length || !value[pos..].StartsWith(prefix))
                return false;
            pos += prefix.Length;

            string nextStatic = pattern.Strings[i + 1];
            int nextPos;

            if (string.IsNullOrEmpty(nextStatic))
            {
                if (i == pattern.InterpolatedTypes.Count - 1)
                {
                    nextPos = value.Length;
                }
                else
                {
                    int foundIdx = -1;
                    for (int j = i + 2; j < pattern.Strings.Count; j++)
                    {
                        if (!string.IsNullOrEmpty(pattern.Strings[j]))
                        {
                            foundIdx = value.IndexOf(pattern.Strings[j], pos);
                            break;
                        }
                    }
                    nextPos = foundIdx >= pos ? foundIdx : value.Length;
                }
            }
            else
            {
                nextPos = value.IndexOf(nextStatic, pos);
            }

            if (nextPos < pos) return false;

            string matched = value[pos..nextPos];
            TypeInfo interpolatedType = pattern.InterpolatedTypes[i];

            if (!MatchesInterpolatedType(matched, interpolatedType))
                return false;

            pos = nextPos;
        }

        return value[pos..] == pattern.Strings[^1];
    }

    private bool MatchesInterpolatedType(string value, TypeInfo type) => type switch
    {
        TypeInfo.String => true,
        TypeInfo.StringLiteral sl => sl.Value == value,
        TypeInfo.NumberLiteral nl => double.TryParse(value, out var d) && d == nl.Value,
        TypeInfo.BooleanLiteral bl => (bl.Value ? "true" : "false") == value,
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => double.TryParse(value, out _),
        TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => value is "true" or "false",
        TypeInfo.Union u => u.FlattenedTypes.Any(t => MatchesInterpolatedType(value, t)),
        _ => false
    };

    private bool TemplatePatternStructurallyCompatible(TypeInfo.TemplateLiteralType expected, TypeInfo.TemplateLiteralType actual)
    {
        if (expected.Strings.Count != actual.Strings.Count ||
            expected.InterpolatedTypes.Count != actual.InterpolatedTypes.Count)
            return false;

        for (int i = 0; i < expected.Strings.Count; i++)
        {
            if (expected.Strings[i] != actual.Strings[i])
                return false;
        }

        for (int i = 0; i < expected.InterpolatedTypes.Count; i++)
        {
            if (!IsCompatible(expected.InterpolatedTypes[i], actual.InterpolatedTypes[i]))
                return false;
        }

        return true;
    }

    #endregion

    #region Structural Compatibility

    /// <inheritdoc/>
    public bool CheckStructuralCompatibility(
        IReadOnlyDictionary<string, TypeInfo> requiredMembers,
        TypeInfo actual,
        IReadOnlySet<string>? optionalMembers = null)
    {
        foreach (var member in requiredMembers)
        {
            TypeInfo? actualMemberType = GetMemberType(actual, member.Key);

            if (actualMemberType == null && (optionalMembers?.Contains(member.Key) ?? false))
            {
                continue;
            }

            if (actualMemberType == null || !IsCompatible(member.Value, actualMemberType))
            {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public void CheckExcessProperties(TypeInfo.Record actual, TypeInfo expected, Expr sourceExpr)
    {
        HashSet<string> expectedKeys = [];
        bool hasStringIndex = false;
        bool hasNumberIndex = false;

        if (expected is TypeInfo.Record expRecord)
        {
            foreach (var key in expRecord.Fields.Keys)
            {
                expectedKeys.Add(key);
            }
            hasStringIndex = expRecord.StringIndexType != null;
            hasNumberIndex = expRecord.NumberIndexType != null;
        }
        else if (expected is TypeInfo.Interface iface)
        {
            foreach (var member in iface.GetAllMembers())
            {
                expectedKeys.Add(member.Key);
            }
            hasStringIndex = iface.StringIndexType != null;
            hasNumberIndex = iface.NumberIndexType != null;
        }
        else if (expected is TypeInfo.Class cls)
        {
            foreach (var field in cls.FieldTypes)
            {
                expectedKeys.Add(field.Key);
            }
            foreach (var method in cls.Methods)
            {
                expectedKeys.Add(method.Key);
            }
            foreach (var getter in cls.Getters)
            {
                expectedKeys.Add(getter.Key);
            }
            foreach (var setter in cls.Setters)
            {
                expectedKeys.Add(setter.Key);
            }
        }
        else
        {
            return;
        }

        if (hasStringIndex || hasNumberIndex)
        {
            return;
        }

        List<string> excessKeys = [];
        foreach (var actualKey in actual.Fields.Keys)
        {
            if (!expectedKeys.Contains(actualKey))
            {
                excessKeys.Add(actualKey);
            }
        }

        if (excessKeys.Count > 0)
        {
            string excessList = string.Join(", ", excessKeys.Select(k => $"'{k}'"));
            throw new TypeCheckException(
                $"Object literal may only specify known properties. " +
                $"Excess {(excessKeys.Count == 1 ? "property" : "properties")}: {excessList}"
            );
        }
    }

    /// <inheritdoc/>
    public TypeInfo? GetMemberType(TypeInfo type, string name)
    {
        if (type is TypeInfo.Record record)
        {
            return record.Fields.TryGetValue(name, out var t) ? t : null;
        }

        if (type is TypeInfo.String or TypeInfo.StringLiteral)
        {
            return BuiltInTypes.GetStringMemberType(name);
        }

        if (type is TypeInfo.Array arr)
        {
            return BuiltInTypes.GetArrayMemberType(name, arr.ElementType);
        }

        if (type is TypeInfo.Tuple tuple)
        {
            var allTypes = tuple.ElementTypes.ToList();
            if (tuple.RestElementType != null)
                allTypes.Add(tuple.RestElementType);
            var unique = allTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            TypeInfo unionElem = unique.Count == 0
                ? new TypeInfo.Any()
                : (unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique));
            return BuiltInTypes.GetArrayMemberType(name, unionElem);
        }

        if (type is TypeInfo.TypeParameter tp && tp.Constraint != null)
        {
            return GetMemberType(tp.Constraint, name);
        }

        if (type is TypeInfo.Interface itf)
        {
            foreach (var member in itf.GetAllMembers())
            {
                if (member.Key == name) return member.Value;
            }
            return null;
        }

        if (type is TypeInfo.Instance instance)
        {
            return GetInstanceMemberType(instance, name);
        }

        return null;
    }

    private TypeInfo? GetInstanceMemberType(TypeInfo.Instance instance, string name)
    {
        if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
            ig.GenericDefinition is TypeInfo.GenericClass gc)
        {
            if (gc.FieldTypes.TryGetValue(name, out var fieldType)) return fieldType;
            if (gc.Methods.TryGetValue(name, out var methodType)) return methodType;
            TypeInfo? current = gc.Superclass;
            while (current != null)
            {
                var fields = GetFieldTypes(current);
                if (fields != null && fields.TryGetValue(name, out var superField)) return superField;
                var methods = GetMethods(current);
                if (methods != null && methods.TryGetValue(name, out var superMethod)) return superMethod;
                current = GetSuperclass(current);
            }
        }
        else if (instance.ClassType is TypeInfo.Class classType)
        {
            TypeInfo? current = classType;
            while (current != null)
            {
                var fields = GetFieldTypes(current);
                if (fields != null && fields.TryGetValue(name, out var fieldType)) return fieldType;
                var methods = GetMethods(current);
                if (methods != null && methods.TryGetValue(name, out var methodType)) return methodType;
                current = GetSuperclass(current);
            }
        }
        else if (instance.ClassType is TypeInfo.MutableClass mutableClass)
        {
            if (mutableClass.FieldTypes.TryGetValue(name, out var fieldType)) return fieldType;
            if (mutableClass.Methods.TryGetValue(name, out var methodType)) return methodType;
            if (mutableClass.Frozen is TypeInfo.Class frozen)
            {
                TypeInfo? current = frozen.Superclass;
                while (current != null)
                {
                    var fields = GetFieldTypes(current);
                    if (fields != null && fields.TryGetValue(name, out var superField)) return superField;
                    var methods = GetMethods(current);
                    if (methods != null && methods.TryGetValue(name, out var superMethod)) return superMethod;
                    current = GetSuperclass(current);
                }
            }
        }
        return null;
    }

    #endregion

    #region Type Guard Analysis

    /// <inheritdoc/>
    public (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeTypeGuard(
        Expr condition,
        TypeEnvironment environment,
        Func<Expr, TypeInfo> checkExpr)
    {
        // Pattern 1: typeof x === "string"
        if (condition is Expr.Binary bin &&
            bin.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin.Left is Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable v } &&
            bin.Right is Expr.Literal { Value: string typeStr })
        {
            return AnalyzeTypeofGuard(v.Name.Lexeme, typeStr, negated: false, environment);
        }

        // Pattern 1b: "string" === typeof x
        if (condition is Expr.Binary bin1b &&
            bin1b.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin1b.Right is Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable v1b } &&
            bin1b.Left is Expr.Literal { Value: string typeStr1b })
        {
            return AnalyzeTypeofGuard(v1b.Name.Lexeme, typeStr1b, negated: false, environment);
        }

        // Pattern 2: typeof x !== "string"
        if (condition is Expr.Binary bin2 &&
            bin2.Operator.Type is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL &&
            bin2.Left is Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable v2 } &&
            bin2.Right is Expr.Literal { Value: string typeStr2 })
        {
            return AnalyzeTypeofGuard(v2.Name.Lexeme, typeStr2, negated: true, environment);
        }

        // Pattern 3: x instanceof ClassName
        if (condition is Expr.Binary bin3 &&
            bin3.Operator.Type == TokenType.INSTANCEOF &&
            bin3.Left is Expr.Variable v3 &&
            bin3.Right is Expr.Variable classVar)
        {
            return AnalyzeInstanceofGuard(v3.Name.Lexeme, classVar.Name, environment);
        }

        // Pattern 4: x === null
        if (condition is Expr.Binary bin4 &&
            bin4.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin4.Left is Expr.Variable v4 &&
            bin4.Right is Expr.Literal { Value: null })
        {
            return AnalyzeNullCheck(v4.Name.Lexeme, checkingForNull: true, negated: false, environment);
        }

        // Pattern 4b: null === x
        if (condition is Expr.Binary bin4b &&
            bin4b.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin4b.Right is Expr.Variable v4b &&
            bin4b.Left is Expr.Literal { Value: null })
        {
            return AnalyzeNullCheck(v4b.Name.Lexeme, checkingForNull: true, negated: false, environment);
        }

        // Pattern 5: x !== null
        if (condition is Expr.Binary bin5 &&
            bin5.Operator.Type is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL &&
            bin5.Left is Expr.Variable v5 &&
            bin5.Right is Expr.Literal { Value: null })
        {
            return AnalyzeNullCheck(v5.Name.Lexeme, checkingForNull: true, negated: true, environment);
        }

        // Pattern 6: x === undefined
        if (condition is Expr.Binary bin6 &&
            bin6.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin6.Left is Expr.Variable v6 &&
            bin6.Right is Expr.Literal { Value: Runtime.Types.SharpTSUndefined })
        {
            return AnalyzeNullCheck(v6.Name.Lexeme, checkingForNull: false, negated: false, environment);
        }

        // Pattern 6b: undefined === x
        if (condition is Expr.Binary bin6b &&
            bin6b.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin6b.Right is Expr.Variable v6b &&
            bin6b.Left is Expr.Literal { Value: Runtime.Types.SharpTSUndefined })
        {
            return AnalyzeNullCheck(v6b.Name.Lexeme, checkingForNull: false, negated: false, environment);
        }

        // Pattern 7: x !== undefined
        if (condition is Expr.Binary bin7 &&
            bin7.Operator.Type is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL &&
            bin7.Left is Expr.Variable v7 &&
            bin7.Right is Expr.Literal { Value: Runtime.Types.SharpTSUndefined })
        {
            return AnalyzeNullCheck(v7.Name.Lexeme, checkingForNull: false, negated: true, environment);
        }

        // Pattern 8: "prop" in x
        if (condition is Expr.Binary bin8 &&
            bin8.Operator.Type == TokenType.IN &&
            bin8.Left is Expr.Literal { Value: string propName } &&
            bin8.Right is Expr.Variable v8)
        {
            return AnalyzeInOperatorGuard(v8.Name.Lexeme, propName, environment);
        }

        // Pattern 9: x.kind === "literal"
        if (condition is Expr.Binary bin9 &&
            bin9.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin9.Left is Expr.Get { Object: Expr.Variable v9, Name: var propToken } &&
            bin9.Right is Expr.Literal { Value: string literalValue })
        {
            return AnalyzeDiscriminatedUnionGuard(v9.Name.Lexeme, propToken.Lexeme, literalValue, environment);
        }

        // Pattern 9b: "literal" === x.kind
        if (condition is Expr.Binary bin9b &&
            bin9b.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin9b.Right is Expr.Get { Object: Expr.Variable v9b, Name: var propToken9b } &&
            bin9b.Left is Expr.Literal { Value: string literalValue9b })
        {
            return AnalyzeDiscriminatedUnionGuard(v9b.Name.Lexeme, propToken9b.Lexeme, literalValue9b, environment);
        }

        // Pattern 10: Array.isArray(x)
        if (condition is Expr.Call call &&
            call.Callee is Expr.Get { Object: Expr.Variable { Name.Lexeme: "Array" }, Name.Lexeme: "isArray" } &&
            call.Arguments.Count == 1 &&
            call.Arguments[0] is Expr.Variable v10)
        {
            return AnalyzeArrayIsArrayGuard(v10.Name.Lexeme, environment);
        }

        // Pattern 11: User-defined type predicate
        if (condition is Expr.Call predicateCall)
        {
            return AnalyzeTypePredicateCall(predicateCall, environment, checkExpr);
        }

        return (null, null, null);
    }

    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeTypeofGuard(
        string varName, string typeStr, bool negated, TypeEnvironment environment)
    {
        var currentType = environment.Get(varName);
        if (currentType == null) return (null, null, null);

        if (currentType is TypeInfo.Unknown)
        {
            TypeInfo? narrowedType = TypeofStringToType(typeStr);
            if (negated)
                return (varName, new TypeInfo.Unknown(), narrowedType);
            return (varName, narrowedType, new TypeInfo.Unknown());
        }

        if (currentType is TypeInfo.Any)
        {
            TypeInfo? narrowedType = TypeofStringToType(typeStr);
            if (negated)
                return (varName, new TypeInfo.Any(), narrowedType);
            return (varName, narrowedType, new TypeInfo.Any());
        }

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var matching = flattenedTypes.Where(t => TypeMatchesTypeof(t, typeStr)).ToList();
            var nonMatching = flattenedTypes.Where(t => !TypeMatchesTypeof(t, typeStr)).ToList();

            TypeInfo? narrowedType = matching.Count == 0 ? null :
                matching.Count == 1 ? matching[0] : new TypeInfo.Union(matching);
            TypeInfo? excludedType = nonMatching.Count == 0 ? null :
                nonMatching.Count == 1 ? nonMatching[0] : new TypeInfo.Union(nonMatching);

            if (negated)
                return (varName, excludedType, narrowedType);
            return (varName, narrowedType, excludedType);
        }

        if (TypeMatchesTypeof(currentType, typeStr))
        {
            if (negated)
                return (varName, new TypeInfo.Never(), currentType);
            return (varName, currentType, new TypeInfo.Never());
        }
        else
        {
            if (negated)
                return (varName, currentType, new TypeInfo.Never());
            return (varName, new TypeInfo.Never(), currentType);
        }
    }

    private static TypeInfo? TypeofStringToType(string typeStr) => typeStr switch
    {
        "string" => new TypeInfo.String(),
        "number" => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
        "boolean" => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
        "bigint" => new TypeInfo.BigInt(),
        "function" => new TypeInfo.Function([], new TypeInfo.Any(), 0, false),
        "object" => new TypeInfo.Object(),
        "symbol" => new TypeInfo.Symbol(),
        "undefined" => new TypeInfo.Undefined(),
        _ => null
    };

    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeInstanceofGuard(
        string varName, Token classToken, TypeEnvironment environment)
    {
        var currentType = environment.Get(varName);
        if (currentType == null) return (null, null, null);

        var classType = environment.Get(classToken.Lexeme);
        if (classType == null) return (null, null, null);

        TypeInfo instanceType;
        if (classType is TypeInfo.Class cls)
        {
            instanceType = new TypeInfo.Instance(cls);
        }
        else if (classType is TypeInfo.MutableClass mc)
        {
            instanceType = new TypeInfo.Instance(mc.Frozen ?? (TypeInfo)mc);
        }
        else if (classType is TypeInfo.Instance inst)
        {
            instanceType = inst;
        }
        else
        {
            return (null, null, null);
        }

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var matching = new List<TypeInfo>();
            var nonMatching = new List<TypeInfo>();

            foreach (var t in flattenedTypes)
            {
                if (IsInstanceOf(t, classType))
                    matching.Add(t);
                else
                    nonMatching.Add(t);
            }

            TypeInfo? narrowedType = matching.Count == 0 ? instanceType :
                matching.Count == 1 ? matching[0] : new TypeInfo.Union(matching);
            TypeInfo? excludedType = nonMatching.Count == 0 ? null :
                nonMatching.Count == 1 ? nonMatching[0] : new TypeInfo.Union(nonMatching);

            return (varName, narrowedType, excludedType);
        }

        if (currentType is TypeInfo.Instance)
        {
            return (varName, instanceType, currentType);
        }

        if (currentType is TypeInfo.Any or TypeInfo.Unknown)
        {
            return (varName, instanceType, currentType);
        }

        return (varName, instanceType, currentType);
    }

    private bool IsInstanceOf(TypeInfo type, TypeInfo classType)
    {
        if (type is not TypeInfo.Instance inst) return false;

        TypeInfo.Class? targetClass = classType switch
        {
            TypeInfo.Class c => c,
            TypeInfo.MutableClass mc => mc.Frozen,
            TypeInfo.Instance i when i.ClassType is TypeInfo.Class ic => ic,
            _ => null
        };

        if (targetClass == null) return false;

        TypeInfo? current = inst.ClassType switch
        {
            TypeInfo.Class c => c,
            TypeInfo.MutableClass mc => mc.Frozen,
            _ => null
        };

        while (current != null)
        {
            if (GetClassName(current) == targetClass.Name) return true;
            current = GetSuperclass(current);
        }

        return false;
    }

    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeNullCheck(
        string varName, bool checkingForNull, bool negated, TypeEnvironment environment)
    {
        var currentType = environment.Get(varName);
        if (currentType == null) return (null, null, null);

        TypeInfo nullishType = checkingForNull ? new TypeInfo.Null() : new TypeInfo.Undefined();

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var nonNullish = flattenedTypes.Where(t =>
                checkingForNull ? t is not TypeInfo.Null : t is not TypeInfo.Undefined).ToList();
            var nullish = flattenedTypes.Where(t =>
                checkingForNull ? t is TypeInfo.Null : t is TypeInfo.Undefined).ToList();

            TypeInfo? nonNullishType = nonNullish.Count == 0 ? new TypeInfo.Never() :
                nonNullish.Count == 1 ? nonNullish[0] : new TypeInfo.Union(nonNullish);
            TypeInfo? nullishResultType = nullish.Count == 0 ? nullishType :
                nullish.Count == 1 ? nullish[0] : new TypeInfo.Union(nullish);

            if (negated)
                return (varName, nonNullishType, nullishResultType);
            return (varName, nullishResultType, nonNullishType);
        }

        if (currentType is TypeInfo.Null && checkingForNull)
        {
            if (negated)
                return (varName, new TypeInfo.Never(), currentType);
            return (varName, currentType, new TypeInfo.Never());
        }
        if (currentType is TypeInfo.Undefined && !checkingForNull)
        {
            if (negated)
                return (varName, new TypeInfo.Never(), currentType);
            return (varName, currentType, new TypeInfo.Never());
        }

        return (null, null, null);
    }

    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeInOperatorGuard(
        string varName, string propertyName, TypeEnvironment environment)
    {
        var currentType = environment.Get(varName);
        if (currentType == null) return (null, null, null);

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var hasProperty = new List<TypeInfo>();
            var lacksProperty = new List<TypeInfo>();

            foreach (var t in flattenedTypes)
            {
                if (TypeHasProperty(t, propertyName))
                    hasProperty.Add(t);
                else
                    lacksProperty.Add(t);
            }

            TypeInfo? narrowedType = hasProperty.Count == 0 ? null :
                hasProperty.Count == 1 ? hasProperty[0] : new TypeInfo.Union(hasProperty);
            TypeInfo? excludedType = lacksProperty.Count == 0 ? null :
                lacksProperty.Count == 1 ? lacksProperty[0] : new TypeInfo.Union(lacksProperty);

            return (varName, narrowedType, excludedType);
        }

        if (TypeHasProperty(currentType, propertyName))
        {
            return (varName, currentType, new TypeInfo.Never());
        }

        return (null, null, null);
    }

    private bool TypeHasProperty(TypeInfo type, string propertyName)
    {
        return type switch
        {
            TypeInfo.Interface iface => iface.Members.ContainsKey(propertyName),
            TypeInfo.Record record => record.Fields.ContainsKey(propertyName),
            TypeInfo.Class cls => cls.FieldTypes.ContainsKey(propertyName) ||
                                  cls.Methods.ContainsKey(propertyName) ||
                                  cls.Getters.ContainsKey(propertyName),
            TypeInfo.Instance inst => TypeHasProperty(inst.ResolvedClassType, propertyName),
            _ => false
        };
    }

    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeDiscriminatedUnionGuard(
        string varName, string discriminantProp, string literalValue, TypeEnvironment environment)
    {
        var currentType = environment.Get(varName);
        if (currentType == null) return (null, null, null);

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var matching = new List<TypeInfo>();
            var nonMatching = new List<TypeInfo>();

            foreach (var t in flattenedTypes)
            {
                var propType = GetDiscriminantPropertyType(t, discriminantProp);
                if (propType != null && DiscriminantMatches(propType, literalValue))
                    matching.Add(t);
                else
                    nonMatching.Add(t);
            }

            TypeInfo? narrowedType = matching.Count == 0 ? null :
                matching.Count == 1 ? matching[0] : new TypeInfo.Union(matching);
            TypeInfo? excludedType = nonMatching.Count == 0 ? null :
                nonMatching.Count == 1 ? nonMatching[0] : new TypeInfo.Union(nonMatching);

            return (varName, narrowedType, excludedType);
        }

        return (null, null, null);
    }

    private static TypeInfo? GetDiscriminantPropertyType(TypeInfo type, string propertyName)
    {
        return type switch
        {
            TypeInfo.Interface iface => iface.Members.TryGetValue(propertyName, out var t) ? t : null,
            TypeInfo.Record record => record.Fields.TryGetValue(propertyName, out var t) ? t : null,
            TypeInfo.Class cls => cls.FieldTypes.TryGetValue(propertyName, out var t) ? t : null,
            TypeInfo.Instance inst => GetDiscriminantPropertyType(inst.ResolvedClassType, propertyName),
            _ => null
        };
    }

    private static bool DiscriminantMatches(TypeInfo propType, string literalValue)
    {
        return propType switch
        {
            TypeInfo.StringLiteral sl => sl.Value == literalValue,
            TypeInfo.String => true,
            _ => false
        };
    }

    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeArrayIsArrayGuard(
        string varName, TypeEnvironment environment)
    {
        var currentType = environment.Get(varName);
        if (currentType == null) return (null, null, null);

        if (currentType is TypeInfo.Union union)
        {
            var flattenedTypes = union.FlattenedTypes;
            var arrays = flattenedTypes.Where(t => t is TypeInfo.Array or TypeInfo.Tuple).ToList();
            var nonArrays = flattenedTypes.Where(t => t is not TypeInfo.Array and not TypeInfo.Tuple).ToList();

            TypeInfo? narrowedType = arrays.Count == 0 ? null :
                arrays.Count == 1 ? arrays[0] : new TypeInfo.Union(arrays);
            TypeInfo? excludedType = nonArrays.Count == 0 ? null :
                nonArrays.Count == 1 ? nonArrays[0] : new TypeInfo.Union(nonArrays);

            return (varName, narrowedType, excludedType);
        }

        if (currentType is TypeInfo.Array or TypeInfo.Tuple)
        {
            return (varName, currentType, new TypeInfo.Never());
        }

        if (currentType is TypeInfo.Any or TypeInfo.Unknown)
        {
            return (varName, new TypeInfo.Array(new TypeInfo.Any()), currentType);
        }

        return (null, null, null);
    }

    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeTypePredicateCall(
        Expr.Call call, TypeEnvironment environment, Func<Expr, TypeInfo> checkExpr)
    {
        TypeInfo? calleeType = null;

        if (call.Callee is Expr.Variable funcVar)
        {
            calleeType = environment.Get(funcVar.Name.Lexeme);
        }
        else if (call.Callee is Expr.Get getExpr)
        {
            var objType = checkExpr(getExpr.Object);
            calleeType = GetMemberType(objType, getExpr.Name.Lexeme);
        }

        if (calleeType == null) return (null, null, null);

        TypeInfo? returnType = calleeType switch
        {
            TypeInfo.Function func => func.ReturnType,
            TypeInfo.GenericFunction gf => gf.ReturnType,
            _ => null
        };

        if (returnType is TypeInfo.TypePredicate pred && !pred.IsAssertion)
        {
            int paramIndex = FindParameterIndex(calleeType, pred.ParameterName);
            if (paramIndex >= 0 && paramIndex < call.Arguments.Count &&
                call.Arguments[paramIndex] is Expr.Variable argVar)
            {
                string varName = argVar.Name.Lexeme;
                TypeInfo? currentType = environment.Get(varName);

                TypeInfo narrowedType = pred.PredicateType;
                TypeInfo? excludedType = currentType != null
                    ? ExcludeTypeFromUnion(currentType, narrowedType)
                    : null;

                return (varName, narrowedType, excludedType);
            }
        }

        return (null, null, null);
    }

    private static int FindParameterIndex(TypeInfo funcType, string paramName)
    {
        List<string>? paramNames = funcType switch
        {
            TypeInfo.Function f => f.ParamNames,
            TypeInfo.GenericFunction gf => gf.ParamNames,
            _ => null
        };

        if (paramNames == null) return 0;
        int index = paramNames.IndexOf(paramName);
        return index >= 0 ? index : 0;
    }

    private static TypeInfo? ExcludeTypeFromUnion(TypeInfo source, TypeInfo toExclude)
    {
        if (source is TypeInfo.Union union)
        {
            var remaining = union.FlattenedTypes
                .Where(t => t.ToString() != toExclude.ToString())
                .ToList();

            if (remaining.Count == 0) return new TypeInfo.Never();
            if (remaining.Count == 1) return remaining[0];
            return new TypeInfo.Union(remaining);
        }

        if (source.ToString() == toExclude.ToString())
            return new TypeInfo.Never();

        return source;
    }

    private bool TypeMatchesTypeof(TypeInfo type, string typeofResult) => typeofResult switch
    {
        "string" => type is TypeInfo.String or TypeInfo.StringLiteral,
        "number" => type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral,
        "boolean" => type is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } or TypeInfo.BooleanLiteral,
        "bigint" => type is TypeInfo.BigInt,
        "object" => type is TypeInfo.Null or TypeInfo.Record or TypeInfo.Array or TypeInfo.Instance or TypeInfo.Object,
        "function" => type is TypeInfo.Function,
        _ => false
    };

    #endregion

    #region Type Predicates

    /// <inheritdoc/>
    public bool IsNumber(TypeInfo t) =>
        IsTypeOfKind(t, type =>
            type is TypeInfo.Primitive p && p.Type == TokenType.TYPE_NUMBER ||
            type is TypeInfo.NumberLiteral);

    /// <inheritdoc/>
    public bool IsString(TypeInfo t) =>
        IsTypeOfKind(t, type =>
            type is TypeInfo.String ||
            type is TypeInfo.StringLiteral);

    /// <inheritdoc/>
    public bool IsBigInt(TypeInfo t) =>
        IsTypeOfKind(t, type => type is TypeInfo.BigInt);

    /// <inheritdoc/>
    public bool IsPrimitiveType(TypeInfo t) => t is TypeInfo.String or TypeInfo.Primitive or TypeInfo.StringLiteral or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigInt or TypeInfo.Symbol or TypeInfo.UniqueSymbol;

    private bool IsTypeOfKind(TypeInfo t, Func<TypeInfo, bool> baseTypeCheck) =>
        baseTypeCheck(t) ||
        t is TypeInfo.Any ||
        (t is TypeInfo.Union u && u.FlattenedTypes.All(inner => IsTypeOfKind(inner, baseTypeCheck))) ||
        (t is TypeInfo.TypeParameter tp && tp.Constraint != null && IsTypeOfKind(tp.Constraint, baseTypeCheck));

    #endregion

    #region Helper Methods

    private static TypeInfo? GetSuperclass(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Superclass,
        TypeInfo.GenericClass gc => gc.Superclass,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Superclass,
            _ => null
        },
        _ => null
    };

    private static FrozenDictionary<string, TypeInfo>? GetMethods(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Methods,
        TypeInfo.GenericClass gc => gc.Methods,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Methods,
            _ => null
        },
        _ => null
    };

    private static string? GetClassName(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.Name,
        TypeInfo.GenericClass gc => gc.Name,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.Name,
            _ => null
        },
        _ => null
    };

    private static FrozenDictionary<string, TypeInfo>? GetFieldTypes(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.FieldTypes,
        TypeInfo.GenericClass gc => gc.FieldTypes,
        TypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TypeInfo.GenericClass gc => gc.FieldTypes,
            _ => null
        },
        _ => null
    };

    private bool IsInSuperclassChain(TypeInfo classType, TypeInfo.InstantiatedGeneric target)
    {
        TypeInfo? current = classType switch
        {
            TypeInfo.Class c => c.Superclass,
            TypeInfo.InstantiatedGeneric ig => GetSuperclass(ig),
            _ => null
        };

        while (current != null)
        {
            if (current is TypeInfo.InstantiatedGeneric ig)
            {
                if (InstantiatedGenericsMatch(ig, target))
                    return true;

                current = GetSuperclass(ig);
            }
            else if (current is TypeInfo.Class c)
            {
                current = c.Superclass;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    private bool InstantiatedGenericsMatch(TypeInfo.InstantiatedGeneric a, TypeInfo.InstantiatedGeneric b)
    {
        if (a.GenericDefinition is TypeInfo.GenericClass gcA &&
            b.GenericDefinition is TypeInfo.GenericClass gcB &&
            gcA.Name == gcB.Name)
        {
            if (a.TypeArguments.Count != b.TypeArguments.Count)
                return false;

            for (int i = 0; i < a.TypeArguments.Count; i++)
            {
                if (!IsCompatible(a.TypeArguments[i], b.TypeArguments[i]))
                    return false;
            }
            return true;
        }

        return false;
    }

    private bool AreTypeArgumentsCompatible(
        List<TypeInfo.TypeParameter> typeParams,
        List<TypeInfo> expectedArgs,
        List<TypeInfo> actualArgs)
    {
        for (int i = 0; i < expectedArgs.Count; i++)
        {
            var expectedArg = expectedArgs[i];
            var actualArg = actualArgs[i];
            var variance = i < typeParams.Count ? typeParams[i].Variance : TypeParameterVariance.Invariant;

            bool compatible = variance switch
            {
                TypeParameterVariance.Out => IsCompatible(expectedArg, actualArg),
                TypeParameterVariance.In => IsCompatible(actualArg, expectedArg),
                TypeParameterVariance.InOut =>
                    IsCompatible(expectedArg, actualArg) || IsCompatible(actualArg, expectedArg),
                _ => IsCompatible(expectedArg, actualArg) && IsCompatible(actualArg, expectedArg)
            };

            if (!compatible) return false;
        }

        return true;
    }

    /// <summary>
    /// Substitutes type parameters in a type with concrete types.
    /// </summary>
    private TypeInfo Substitute(TypeInfo type, Dictionary<string, TypeInfo> subs)
    {
        return type switch
        {
            TypeInfo.TypeParameter tp when subs.TryGetValue(tp.Name, out var sub) => sub,
            TypeInfo.Array arr => new TypeInfo.Array(Substitute(arr.ElementType, subs)),
            TypeInfo.Function f => new TypeInfo.Function(
                f.ParamTypes.Select(p => Substitute(p, subs)).ToList(),
                Substitute(f.ReturnType, subs),
                f.MinArity,
                f.HasRestParam,
                f.ThisType != null ? Substitute(f.ThisType, subs) : null,
                f.ParamNames
            ),
            TypeInfo.Union u => new TypeInfo.Union(u.Types.Select(t => Substitute(t, subs)).ToList()),
            TypeInfo.Intersection i => new TypeInfo.Intersection(i.Types.Select(t => Substitute(t, subs)).ToList()),
            _ => type
        };
    }

    #endregion
}
