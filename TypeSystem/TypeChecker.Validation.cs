using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Validation helpers - interface implementation, abstract members, override validation.
/// </summary>
/// <remarks>
/// Contains validation methods:
/// ValidateInterfaceImplementation, ValidateAbstractMemberImplementation,
/// IsMethodImplemented, IsGetterImplemented, IsSetterImplemented,
/// FindMemberInClass, ValidateOverrideMembers,
/// HasParentMethod, HasParentGetter, HasParentSetter.
/// </remarks>
public partial class TypeChecker
{
    private void ValidateInterfaceImplementation(TypeInfo.Class classType, TypeInfo.Interface interfaceType, string className)
    {
        foreach (var member in interfaceType.Members)
        {
            string memberName = member.Key;
            TypeInfo expectedType = member.Value;
            bool isOptional = interfaceType.OptionalMembers.Contains(memberName);

            // Check: field, getter, or method (including inheritance chain)
            TypeInfo? actualType = FindMemberInClass(classType, memberName);

            if (actualType == null && !isOptional)
            {
                throw new TypeCheckException($" Class '{className}' does not implement '{memberName}' from interface '{interfaceType.Name}'.");
            }

            if (actualType != null)
            {
                // Special handling for method signature validation
                if (expectedType is TypeInfo.Function expectedFunc && actualType is TypeInfo.Function actualFunc)
                {
                    ValidateMethodSignature(expectedFunc, actualFunc, memberName, className, interfaceType.Name);
                }
                else if (expectedType is TypeInfo.OverloadedFunction expectedOverload && actualType is TypeInfo.Function actualFuncForOverload)
                {
                    // For overloaded interface methods, check against each signature
                    bool matchesAny = false;
                    foreach (var signature in expectedOverload.Signatures)
                    {
                        if (IsMethodSignatureCompatible(signature, actualFuncForOverload))
                        {
                            matchesAny = true;
                            break;
                        }
                    }
                    if (!matchesAny)
                    {
                        // Use first signature for error message
                        ValidateMethodSignature(expectedOverload.Signatures[0], actualFuncForOverload, memberName, className, interfaceType.Name);
                    }
                }
                else if (!IsCompatible(expectedType, actualType))
                {
                    throw new TypeCheckException($" '{className}.{memberName}' has incompatible type. Expected '{expectedType}', got '{actualType}'.");
                }
            }
        }
    }

    /// <summary>
    /// Validates that a class method signature is compatible with an interface method signature.
    /// For interface implementation, the class method must:
    /// 1. Accept at least as many required parameters as the interface method requires
    /// 2. Have compatible parameter types at each position
    /// 3. Have a compatible return type
    /// </summary>
    private void ValidateMethodSignature(TypeInfo.Function expected, TypeInfo.Function actual, string methodName, string className, string interfaceName)
    {
        // Check parameter count: actual must declare at least the required params from expected
        // The interface's MinArity tells us how many parameters callers will pass
        if (actual.ParamTypes.Count < expected.MinArity)
        {
            throw new TypeCheckException(
                $" Method '{className}.{methodName}' has {actual.ParamTypes.Count} parameter(s) but interface '{interfaceName}' requires at least {expected.MinArity}.");
        }

        // Check parameter types at each position (up to what the actual method declares)
        // Parameter types are contravariant: actual's param types can be supertypes of expected's
        // But for simplicity and TypeScript compatibility, we check that expected param is compatible with actual param
        int paramsToCheck = Math.Min(expected.ParamTypes.Count, actual.ParamTypes.Count);
        for (int i = 0; i < paramsToCheck; i++)
        {
            TypeInfo expectedParamType = expected.ParamTypes[i];
            TypeInfo actualParamType = actual.ParamTypes[i];

            // Check bidirectional compatibility for parameters (TypeScript uses bivariant checking for method params)
            if (!IsCompatible(expectedParamType, actualParamType) && !IsCompatible(actualParamType, expectedParamType))
            {
                throw new TypeCheckException(
                    $" Parameter {i + 1} of '{className}.{methodName}' has incompatible type. Interface '{interfaceName}' expects '{expectedParamType}', but got '{actualParamType}'.");
            }
        }

        // Check return type compatibility (covariant: actual's return can be subtype of expected's)
        if (!IsCompatible(expected.ReturnType, actual.ReturnType))
        {
            throw new TypeCheckException(
                $" Return type of '{className}.{methodName}' is incompatible. Interface '{interfaceName}' expects '{expected.ReturnType}', but got '{actual.ReturnType}'.");
        }
    }

    /// <summary>
    /// Checks if a method signature is compatible with an expected signature (non-throwing version).
    /// </summary>
    private bool IsMethodSignatureCompatible(TypeInfo.Function expected, TypeInfo.Function actual)
    {
        // Check parameter count
        if (actual.ParamTypes.Count < expected.MinArity)
            return false;

        // Check parameter types
        int paramsToCheck = Math.Min(expected.ParamTypes.Count, actual.ParamTypes.Count);
        for (int i = 0; i < paramsToCheck; i++)
        {
            if (!IsCompatible(expected.ParamTypes[i], actual.ParamTypes[i]) &&
                !IsCompatible(actual.ParamTypes[i], expected.ParamTypes[i]))
                return false;
        }

        // Check return type
        return IsCompatible(expected.ReturnType, actual.ReturnType);
    }

    /// <summary>
    /// Validates that a non-abstract class implements all abstract members from its superclass chain.
    /// </summary>
    private void ValidateAbstractMemberImplementation(TypeInfo.Class classType, string className)
    {
        // Collect all unimplemented abstract members from the superclass chain
        List<string> missingMembers = [];

        TypeInfo? current = classType.Superclass;
        while (current != null)
        {
            // Check abstract methods from this superclass
            var abstractMethods = GetAbstractMethods(current);
            if (abstractMethods != null)
            {
                foreach (var abstractMethod in abstractMethods)
                {
                    // Check if this class or any class in between implements it
                    if (!IsMethodImplemented(classType, abstractMethod, current))
                    {
                        missingMembers.Add(abstractMethod + "()");
                    }
                }
            }

            // Check abstract getters
            var abstractGetters = GetAbstractGetters(current);
            if (abstractGetters != null)
            {
                foreach (var abstractGetter in abstractGetters)
                {
                    if (!IsGetterImplemented(classType, abstractGetter, current))
                    {
                        missingMembers.Add("get " + abstractGetter);
                    }
                }
            }

            // Check abstract setters
            var abstractSetters = GetAbstractSetters(current);
            if (abstractSetters != null)
            {
                foreach (var abstractSetter in abstractSetters)
                {
                    if (!IsSetterImplemented(classType, abstractSetter, current))
                    {
                        missingMembers.Add("set " + abstractSetter);
                    }
                }
            }

            current = GetSuperclass(current);
        }

        if (missingMembers.Count > 0)
        {
            throw new TypeCheckException($" Class '{className}' must implement the following abstract members: {string.Join(", ", missingMembers)}");
        }
    }

    /// <summary>
    /// Checks if a method is implemented in the class chain between classType and the abstract superclass.
    /// </summary>
    private bool IsMethodImplemented(TypeInfo.Class classType, string methodName, TypeInfo abstractSuperclass)
    {
        TypeInfo? current = classType;
        while (current != null && current != abstractSuperclass)
        {
            // Check if this class has the method and it's NOT abstract
            var methods = GetMethods(current);
            var abstractMethods = GetAbstractMethods(current);
            if (methods != null && methods.ContainsKey(methodName) && (abstractMethods == null || !abstractMethods.Contains(methodName)))
            {
                return true;
            }
            current = GetSuperclass(current);
        }
        return false;
    }

    private bool IsGetterImplemented(TypeInfo.Class classType, string propertyName, TypeInfo abstractSuperclass)
    {
        TypeInfo? current = classType;
        while (current != null && current != abstractSuperclass)
        {
            var getters = GetGetters(current);
            var abstractGetters = GetAbstractGetters(current);
            if (getters != null && getters.ContainsKey(propertyName) && (abstractGetters == null || !abstractGetters.Contains(propertyName)))
            {
                return true;
            }
            current = GetSuperclass(current);
        }
        return false;
    }

    private bool IsSetterImplemented(TypeInfo.Class classType, string propertyName, TypeInfo abstractSuperclass)
    {
        TypeInfo? current = classType;
        while (current != null && current != abstractSuperclass)
        {
            var setters = GetSetters(current);
            var abstractSetters = GetAbstractSetters(current);
            if (setters != null && setters.ContainsKey(propertyName) && (abstractSetters == null || !abstractSetters.Contains(propertyName)))
            {
                return true;
            }
            current = GetSuperclass(current);
        }
        return false;
    }

    private TypeInfo? FindMemberInClass(TypeInfo.Class classType, string name)
    {
        TypeInfo? current = classType;
        while (current != null)
        {
            var fieldTypes = GetFieldTypes(current);
            var getters = GetGetters(current);
            var methods = GetMethods(current);
            if (fieldTypes != null && fieldTypes.TryGetValue(name, out var ft)) return ft;
            if (getters != null && getters.TryGetValue(name, out var gt)) return gt;
            if (methods != null && methods.TryGetValue(name, out var mt)) return mt;
            current = GetSuperclass(current);
        }
        return null;
    }

    /// <summary>
    /// Validates that methods/accessors marked with 'override' actually override a member in the superclass chain.
    /// </summary>
    private void ValidateOverrideMembers(Stmt.Class classStmt, TypeInfo.Class classType)
    {
        // Check methods marked with override
        foreach (var method in classStmt.Methods)
        {
            if (method.IsOverride)
            {
                string methodName = method.Name.Lexeme;
                if (!HasParentMethod(classType.Superclass, methodName))
                {
                    throw new TypeCheckException($" Method '{methodName}' is marked as override but does not override any method in a base class.");
                }
            }
        }

        // Check accessors marked with override
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                if (accessor.IsOverride)
                {
                    string propertyName = accessor.Name.Lexeme;
                    bool isGetter = accessor.Kind.Type == Parsing.TokenType.GET;

                    if (isGetter)
                    {
                        if (!HasParentGetter(classType.Superclass, propertyName))
                        {
                            throw new TypeCheckException($" Getter '{propertyName}' is marked as override but does not override any getter in a base class.");
                        }
                    }
                    else
                    {
                        if (!HasParentSetter(classType.Superclass, propertyName))
                        {
                            throw new TypeCheckException($" Setter '{propertyName}' is marked as override but does not override any setter in a base class.");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if a method exists in the superclass chain.
    /// </summary>
    private bool HasParentMethod(TypeInfo? superclass, string methodName)
    {
        TypeInfo? current = superclass;
        while (current != null)
        {
            var methods = GetMethods(current);
            if (methods != null && methods.ContainsKey(methodName))
            {
                return true;
            }
            current = GetSuperclass(current);
        }
        return false;
    }

    /// <summary>
    /// Checks if a getter exists in the superclass chain.
    /// </summary>
    private bool HasParentGetter(TypeInfo? superclass, string propertyName)
    {
        TypeInfo? current = superclass;
        while (current != null)
        {
            var getters = GetGetters(current);
            if (getters != null && getters.ContainsKey(propertyName))
            {
                return true;
            }
            current = GetSuperclass(current);
        }
        return false;
    }

    /// <summary>
    /// Checks if a setter exists in the superclass chain.
    /// </summary>
    private bool HasParentSetter(TypeInfo? superclass, string propertyName)
    {
        TypeInfo? current = superclass;
        while (current != null)
        {
            var setters = GetSetters(current);
            if (setters != null && setters.ContainsKey(propertyName))
            {
                return true;
            }
            current = GetSuperclass(current);
        }
        return false;
    }
}
