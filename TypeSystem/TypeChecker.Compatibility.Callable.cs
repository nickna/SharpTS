namespace SharpTS.TypeSystem;

/// <summary>
/// Callable and constructable interface matching for type compatibility.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Checks if a function type matches any of the call signatures in an interface.
    /// Used for assigning functions to callable interface types.
    /// </summary>
    private bool FunctionMatchesCallSignatures(TypeInfo.Function func, List<TypeInfo.CallSignature> callSignatures)
    {
        return callSignatures.Any(sig => FunctionMatchesCallSignature(func, sig));
    }

    /// <summary>
    /// Checks if a function type matches a single call signature.
    /// </summary>
    private bool FunctionMatchesCallSignature(TypeInfo.Function func, TypeInfo.CallSignature sig)
    {
        // Generic signatures need special handling
        if (sig.IsGeneric)
        {
            // For generic call signatures, check if function can satisfy the signature
            // For now, require exact structural match (this could be relaxed later)
            return false; // Generic call signatures are complex to match - defer to actual call
        }

        // Check parameter count compatibility
        if (func.ParamTypes.Count < sig.MinArity)
            return false;

        if (!sig.HasRestParam && func.ParamTypes.Count > sig.ParamTypes.Count)
            return false;

        // Check parameter type compatibility (contravariant - signature params must be assignable FROM function params)
        int paramCount = Math.Min(func.ParamTypes.Count, sig.ParamTypes.Count);
        for (int i = 0; i < paramCount; i++)
        {
            // Function parameter type should accept what the signature requires
            if (!IsCompatible(func.ParamTypes[i], sig.ParamTypes[i]))
                return false;
        }

        // Check return type compatibility (covariant - function return must be assignable TO signature return)
        return IsCompatible(sig.ReturnType, func.ReturnType);
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
        if (ctorFunc.ParamTypes.Count < sig.MinArity)
            return false;

        if (!sig.HasRestParam && ctorFunc.ParamTypes.Count > sig.ParamTypes.Count)
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
