namespace SharpTS.TypeSystem;

/// <summary>
/// Callable and constructable interface matching for type compatibility.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Checks that every call signature required by the target is satisfied by one of the source's
    /// signatures — the general overload-aware rule (a single-signature source must therefore match
    /// every target overload).
    /// </summary>
    private bool CallSignaturesSatisfiedBy(
        List<TypeInfo.CallSignature> targetSignatures,
        IReadOnlyList<NormalizedSignature> sourceSignatures)
    {
        // Full contextual instantiation only relates single signature to single signature; with
        // overloads on either side, each pairing relates with erased type parameters (tsc rule).
        bool erase = targetSignatures.Count != 1 || sourceSignatures.Count != 1;
        foreach (var ts in targetSignatures)
        {
            var targetSig = NormalizeCallSignature(ts);
            if (erase) targetSig = EraseSignature(targetSig);
            if (!sourceSignatures.Any(ss => SignatureRelatedTo(erase ? EraseSignature(ss) : ss, targetSig)))
                return false;
        }
        return true;
    }

    /// <summary>Views a call signature as the function type it denotes.</summary>
    private static TypeInfo.Function CallSignatureToFunction(TypeInfo.CallSignature sig) =>
        new(sig.ParamTypes, sig.ReturnType, sig.RequiredParams, sig.HasRestParam, ThisType: null, sig.ParamNames);

    /// <summary>Views a call signature as a normalized signature, type parameters included.</summary>
    private static NormalizedSignature NormalizeCallSignature(TypeInfo.CallSignature sig) =>
        new(sig.TypeParams, CallSignatureToFunction(sig));

    /// <summary>Views a construct signature as a normalized signature, type parameters included.</summary>
    private static NormalizedSignature NormalizeConstructorSignature(TypeInfo.ConstructorSignature sig) =>
        new(sig.TypeParams, ConstructorSignatureToFunction(sig));

    /// <summary>
    /// Call signatures carried by a callable type (interface or inline object type), or null if the
    /// type is not callable. Used to make callable interfaces and object types interchangeable in
    /// assignability checks.
    /// </summary>
    private static List<TypeInfo.CallSignature>? GetCallSignatures(TypeInfo type) => type switch
    {
        TypeInfo.Interface { IsCallable: true } itf => itf.CallSignatures,
        TypeInfo.Record { IsCallable: true } rec => rec.CallSignatures,
        _ => null,
    };

    /// <summary>
    /// Checks that some call signature of a callable source type is assignable to the target
    /// function type (plain or generic) — i.e. the callable can stand in for the function.
    /// </summary>
    private bool CallableAssignableToFunction(List<TypeInfo.CallSignature> sourceSignatures, NormalizedSignature target)
    {
        bool erase = sourceSignatures.Count != 1;
        var targetSig = erase ? EraseSignature(target) : target;
        foreach (var sig in sourceSignatures)
        {
            var ss = NormalizeCallSignature(sig);
            if (erase) ss = EraseSignature(ss);
            if (SignatureRelatedTo(ss, targetSig))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Construct signatures carried by a constructable type (interface or inline object type), or
    /// null if the type is not constructable.
    /// </summary>
    private static List<TypeInfo.ConstructorSignature>? GetConstructorSignatures(TypeInfo type) => type switch
    {
        TypeInfo.Interface { IsConstructable: true } itf => itf.ConstructorSignatures,
        TypeInfo.Record { IsConstructable: true } rec => rec.ConstructorSignatures,
        _ => null,
    };

    /// <summary>Views a construct signature as the (constructor) function type it denotes.</summary>
    private static TypeInfo.Function ConstructorSignatureToFunction(TypeInfo.ConstructorSignature sig) =>
        new(sig.ParamTypes, sig.ReturnType, sig.RequiredParams, sig.HasRestParam, ThisType: null, sig.ParamNames);

    /// <summary>
    /// Checks that every construct signature required by the target is satisfied by one of the
    /// source's construct signatures (parameter contravariance, return covariance via the shared
    /// function-compatibility logic).
    /// </summary>
    private bool ConstructorSignaturesSatisfiedBy(
        List<TypeInfo.ConstructorSignature> targetSignatures,
        List<TypeInfo.ConstructorSignature> sourceSignatures)
    {
        // Same single-vs-single rule as call signatures: overloads on either side relate erased.
        bool erase = targetSignatures.Count != 1 || sourceSignatures.Count != 1;
        foreach (var ts in targetSignatures)
        {
            var targetSig = NormalizeConstructorSignature(ts);
            if (erase) targetSig = EraseSignature(targetSig);
            if (!sourceSignatures.Any(ss => SignatureRelatedTo(
                    erase ? EraseSignature(NormalizeConstructorSignature(ss)) : NormalizeConstructorSignature(ss),
                    targetSig)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Checks if a class matches any of the constructor signatures in an interface.
    /// Used for assigning classes to constructable interface types.
    /// </summary>
    private bool ClassMatchesConstructorSignatures(TypeInfo.Class cls, List<TypeInfo.ConstructorSignature> constructorSignatures)
    {
        return constructorSignatures.Any(sig => ClassMatchesConstructorSignature(cls, sig));
    }

    /// <summary>
    /// Checks if a class matches a single constructor signature.
    /// </summary>
    private bool ClassMatchesConstructorSignature(TypeInfo.Class cls, TypeInfo.ConstructorSignature sig)
    {
        // Generic signatures need special handling
        if (sig.IsGeneric)
        {
            // For generic constructor signatures, defer to actual instantiation
            return false;
        }

        // Get the class constructor
        if (!cls.Methods.TryGetValue("constructor", out var ctorTypeInfo))
        {
            // No constructor - check if signature accepts zero arguments
            return sig.MinArity == 0;
        }

        // Handle constructor type (may be Function or OverloadedFunction)
        if (ctorTypeInfo is TypeInfo.OverloadedFunction overloadedCtor)
        {
            // Check if any overload matches
            return overloadedCtor.Signatures.Any(ctorSig => ConstructorSignatureMatches(ctorSig, sig));
        }
        else if (ctorTypeInfo is TypeInfo.Function ctorFunc)
        {
            return ConstructorSignatureMatches(ctorFunc, sig);
        }

        return false;
    }

    /// <summary>
    /// Checks if a constructor function signature matches a constructor signature from an interface.
    /// </summary>
    private bool ConstructorSignatureMatches(TypeInfo.Function ctorFunc, TypeInfo.ConstructorSignature sig)
    {
        // Check parameter count compatibility
        if (ctorFunc.MinArity > sig.ParamTypes.Count)
            return false;

        if (ctorFunc.ParamTypes.Count < sig.MinArity)
            return false;

        // Check parameter type compatibility (contravariant)
        int paramCount = Math.Min(ctorFunc.ParamTypes.Count, sig.ParamTypes.Count);
        for (int i = 0; i < paramCount; i++)
        {
            if (!IsCompatible(ctorFunc.ParamTypes[i], sig.ParamTypes[i]))
                return false;
        }

        // Note: Constructor return type is handled by the class - we don't check it here
        // The sig.ReturnType specifies what the new expression produces, which is determined by the class itself
        return true;
    }
}
