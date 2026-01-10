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
                throw new Exception($"Type Error: Class '{className}' does not implement '{memberName}' from interface '{interfaceType.Name}'.");
            }

            if (actualType != null && !IsCompatible(expectedType, actualType))
            {
                throw new Exception($"Type Error: '{className}.{memberName}' has incompatible type. Expected '{expectedType}', got '{actualType}'.");
            }
        }
    }

    /// <summary>
    /// Validates that a non-abstract class implements all abstract members from its superclass chain.
    /// </summary>
    private void ValidateAbstractMemberImplementation(TypeInfo.Class classType, string className)
    {
        // Collect all unimplemented abstract members from the superclass chain
        List<string> missingMembers = [];

        TypeInfo.Class? current = classType.Superclass;
        while (current != null)
        {
            // Check abstract methods from this superclass
            foreach (var abstractMethod in current.AbstractMethodSet)
            {
                // Check if this class or any class in between implements it
                if (!IsMethodImplemented(classType, abstractMethod, current))
                {
                    missingMembers.Add(abstractMethod + "()");
                }
            }

            // Check abstract getters
            foreach (var abstractGetter in current.AbstractGetterSet)
            {
                if (!IsGetterImplemented(classType, abstractGetter, current))
                {
                    missingMembers.Add("get " + abstractGetter);
                }
            }

            // Check abstract setters
            foreach (var abstractSetter in current.AbstractSetterSet)
            {
                if (!IsSetterImplemented(classType, abstractSetter, current))
                {
                    missingMembers.Add("set " + abstractSetter);
                }
            }

            current = current.Superclass;
        }

        if (missingMembers.Count > 0)
        {
            throw new Exception($"Type Error: Class '{className}' must implement the following abstract members: {string.Join(", ", missingMembers)}");
        }
    }

    /// <summary>
    /// Checks if a method is implemented in the class chain between classType and the abstract superclass.
    /// </summary>
    private bool IsMethodImplemented(TypeInfo.Class classType, string methodName, TypeInfo.Class abstractSuperclass)
    {
        TypeInfo.Class? current = classType;
        while (current != null && current != abstractSuperclass)
        {
            // Check if this class has the method and it's NOT abstract
            if (current.Methods.ContainsKey(methodName) && !current.AbstractMethodSet.Contains(methodName))
            {
                return true;
            }
            current = current.Superclass;
        }
        return false;
    }

    private bool IsGetterImplemented(TypeInfo.Class classType, string propertyName, TypeInfo.Class abstractSuperclass)
    {
        TypeInfo.Class? current = classType;
        while (current != null && current != abstractSuperclass)
        {
            if (current.Getters.ContainsKey(propertyName) && !current.AbstractGetterSet.Contains(propertyName))
            {
                return true;
            }
            current = current.Superclass;
        }
        return false;
    }

    private bool IsSetterImplemented(TypeInfo.Class classType, string propertyName, TypeInfo.Class abstractSuperclass)
    {
        TypeInfo.Class? current = classType;
        while (current != null && current != abstractSuperclass)
        {
            if (current.Setters.ContainsKey(propertyName) && !current.AbstractSetterSet.Contains(propertyName))
            {
                return true;
            }
            current = current.Superclass;
        }
        return false;
    }

    private TypeInfo? FindMemberInClass(TypeInfo.Class classType, string name)
    {
        TypeInfo.Class? current = classType;
        while (current != null)
        {
            if (current.FieldTypes.TryGetValue(name, out var ft)) return ft;
            if (current.Getters.TryGetValue(name, out var gt)) return gt;
            if (current.Methods.TryGetValue(name, out var mt)) return mt;
            current = current.Superclass;
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
                    throw new Exception($"Type Error: Method '{methodName}' is marked as override but does not override any method in a base class.");
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
                            throw new Exception($"Type Error: Getter '{propertyName}' is marked as override but does not override any getter in a base class.");
                        }
                    }
                    else
                    {
                        if (!HasParentSetter(classType.Superclass, propertyName))
                        {
                            throw new Exception($"Type Error: Setter '{propertyName}' is marked as override but does not override any setter in a base class.");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if a method exists in the superclass chain.
    /// </summary>
    private bool HasParentMethod(TypeInfo.Class? superclass, string methodName)
    {
        TypeInfo.Class? current = superclass;
        while (current != null)
        {
            if (current.Methods.ContainsKey(methodName))
            {
                return true;
            }
            current = current.Superclass;
        }
        return false;
    }

    /// <summary>
    /// Checks if a getter exists in the superclass chain.
    /// </summary>
    private bool HasParentGetter(TypeInfo.Class? superclass, string propertyName)
    {
        TypeInfo.Class? current = superclass;
        while (current != null)
        {
            if (current.Getters.ContainsKey(propertyName))
            {
                return true;
            }
            current = current.Superclass;
        }
        return false;
    }

    /// <summary>
    /// Checks if a setter exists in the superclass chain.
    /// </summary>
    private bool HasParentSetter(TypeInfo.Class? superclass, string propertyName)
    {
        TypeInfo.Class? current = superclass;
        while (current != null)
        {
            if (current.Setters.ContainsKey(propertyName))
            {
                return true;
            }
            current = current.Superclass;
        }
        return false;
    }
}
