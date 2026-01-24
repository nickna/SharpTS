using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Structural compatibility checking (duck typing) and member access type resolution.
/// </summary>
public partial class TypeChecker
{
    private bool CheckStructuralCompatibility(IReadOnlyDictionary<string, TypeInfo> requiredMembers, TypeInfo actual, IReadOnlySet<string>? optionalMembers = null)
    {
        foreach (var member in requiredMembers)
        {
            TypeInfo? actualMemberType = GetMemberType(actual, member.Key);

            // If member is optional and not present, that's OK
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

    /// <summary>
    /// Checks for excess properties in fresh object literals assigned to typed variables.
    /// TypeScript performs this check to catch typos and enforce exact object shapes.
    /// </summary>
    /// <param name="actual">The actual object record type from the literal</param>
    /// <param name="expected">The expected type from the variable declaration</param>
    /// <param name="sourceExpr">The source expression for error context</param>
    private void CheckExcessProperties(TypeInfo.Record actual, TypeInfo expected, Expr sourceExpr)
    {
        // Get all valid property names and check for index signatures
        HashSet<string> expectedKeys = [];
        bool hasStringIndex = false;
        bool hasNumberIndex = false;

        if (expected is TypeInfo.Record expRecord)
        {
            foreach (var key in expRecord.Fields.Keys)
            {
                expectedKeys.Add(key);
            }
            // Check for index signatures
            hasStringIndex = expRecord.StringIndexType != null;
            hasNumberIndex = expRecord.NumberIndexType != null;
        }
        else if (expected is TypeInfo.Interface iface)
        {
            // Include inherited members from extended interfaces
            foreach (var member in iface.GetAllMembers())
            {
                expectedKeys.Add(member.Key);
            }
            // Check for index signatures in interfaces
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
            // For other types (primitives, unions, etc.), skip excess property check
            return;
        }

        // If type has index signatures, all properties are valid
        if (hasStringIndex || hasNumberIndex)
        {
            return;
        }

        // Find properties in actual that are not in expected
        List<string> excessKeys = [];
        foreach (var actualKey in actual.Fields.Keys)
        {
            if (!expectedKeys.Contains(actualKey))
            {
                excessKeys.Add(actualKey);
            }
        }

        // Throw error if excess properties found
        if (excessKeys.Count > 0)
        {
            string excessList = string.Join(", ", excessKeys.Select(k => $"'{k}'"));
            throw new TypeCheckException(
                $"Object literal may only specify known properties. " +
                $"Excess {(excessKeys.Count == 1 ? "property" : "properties")}: {excessList}"
            );
        }
    }

    private TypeInfo? GetMemberType(TypeInfo type, string name)
    {
        if (type is TypeInfo.Record record)
        {
            return record.Fields.TryGetValue(name, out var t) ? t : null;
        }

        // Handle String type - has length property and string methods
        if (type is TypeInfo.String or TypeInfo.StringLiteral)
        {
            return BuiltInTypes.GetStringMemberType(name);
        }

        // Handle Array type - has length property and array methods
        if (type is TypeInfo.Array arr)
        {
            return BuiltInTypes.GetArrayMemberType(name, arr.ElementType);
        }

        // Handle Tuple type - treat as array for member access
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

        // Handle TypeParameter with constraint - delegate to constraint
        if (type is TypeInfo.TypeParameter tp && tp.Constraint != null)
        {
            return GetMemberType(tp.Constraint, name);
        }

        // Handle Interface type
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
            // Handle InstantiatedGeneric
            if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                ig.GenericDefinition is TypeInfo.GenericClass gc)
            {
                // Check fields first, then methods
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
                    // Check fields first, then methods
                    var fields = GetFieldTypes(current);
                    if (fields != null && fields.TryGetValue(name, out var fieldType)) return fieldType;
                    var methods = GetMethods(current);
                    if (methods != null && methods.TryGetValue(name, out var methodType)) return methodType;
                    current = GetSuperclass(current);
                }
            }
            // Handle MutableClass (during signature collection)
            else if (instance.ClassType is TypeInfo.MutableClass mutableClass)
            {
                // Check fields first, then methods
                if (mutableClass.FieldTypes.TryGetValue(name, out var fieldType)) return fieldType;
                if (mutableClass.Methods.TryGetValue(name, out var methodType)) return methodType;
                // Check frozen version if available (may have superclass methods)
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
        }
        return null;
    }

    private bool IsSubclassOf(TypeInfo.Class? subclass, TypeInfo.Class target)
    {
        if (subclass == null) return false;
        TypeInfo? current = subclass;
        while (current != null)
        {
            if (current is TypeInfo.Class cls && cls.Name == target.Name) return true;
            current = GetSuperclass(current);
        }
        return false;
    }

    /// <summary>
    /// Checks if a target InstantiatedGeneric is in the superclass chain of a class.
    /// Used for checking if NumberBox (extends Box&lt;number&gt;) is assignable to Box&lt;number&gt;.
    /// </summary>
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
                // Check if this InstantiatedGeneric matches the target
                if (InstantiatedGenericsMatch(ig, target))
                    return true;

                // Continue up the chain
                current = GetSuperclass(ig);
            }
            else if (current is TypeInfo.Class c)
            {
                // Regular class in chain, continue up
                current = c.Superclass;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if two InstantiatedGeneric types match (same generic definition and compatible type arguments).
    /// </summary>
    private bool InstantiatedGenericsMatch(TypeInfo.InstantiatedGeneric a, TypeInfo.InstantiatedGeneric b)
    {
        // Must be the same generic definition
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

    /// <summary>
    /// Checks if type arguments are compatible according to variance annotations.
    /// </summary>
    /// <param name="typeParams">The type parameters with variance annotations.</param>
    /// <param name="expectedArgs">Expected type arguments.</param>
    /// <param name="actualArgs">Actual type arguments.</param>
    /// <returns>True if all type arguments are compatible respecting variance.</returns>
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
                // Covariant (out T): actual can be subtype of expected (normal direction)
                // Producer<Dog> assignable to Producer<Animal>
                TypeParameterVariance.Out => IsCompatible(expectedArg, actualArg),

                // Contravariant (in T): actual can be supertype of expected (reversed direction)
                // Consumer<Animal> assignable to Consumer<Dog>
                TypeParameterVariance.In => IsCompatible(actualArg, expectedArg),

                // Bivariant (in out T): either direction works
                TypeParameterVariance.InOut =>
                    IsCompatible(expectedArg, actualArg) || IsCompatible(actualArg, expectedArg),

                // Invariant (no modifier): must be exactly compatible both ways
                _ => IsCompatible(expectedArg, actualArg) && IsCompatible(actualArg, expectedArg)
            };

            if (!compatible) return false;
        }

        return true;
    }
}
