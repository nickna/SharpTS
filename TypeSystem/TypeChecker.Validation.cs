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
    /// <summary>
    /// Emits a type-check diagnostic, honoring recovery mode: in recovery mode it records (so sibling
    /// declarations are still checked and the diagnostic lands at <paramref name="line"/>) and the
    /// caller continues; otherwise it throws — fatal, aborting the non-recovery <see cref="Check"/>
    /// path before interpretation. Mirrors the established idiom in
    /// <c>TypeChecker.Statements.Interfaces.cs</c>.
    /// </summary>
    private void EmitOrThrow(string message, int? line, string tsCode)
    {
        var ex = new TypeCheckException(message, line: line, tsCode: tsCode);
        if (_recoveryMode)
            RecordTypeError(ex);
        else
            throw ex;
    }

    private void ValidateInterfaceImplementation(TypeInfo.Class classType, TypeInfo.Interface interfaceType, string className, int? classNameLine)
    {
        // Weak-type check (TS2559): an interface whose members are ALL optional is not satisfied by a
        // class that shares NONE of them, even though it would otherwise be vacuously satisfied. tsc
        // reports this in place of the per-member checks below, so short-circuit (#897).
        if (FailsWeakTypeCheck(interfaceType, classType))
        {
            RecordTypeError(new TypeCheckException(
                $" Type '{className}' has no properties in common with type '{interfaceType.Name}'.",
                line: classNameLine, tsCode: "TS2559"));
            return;
        }

        foreach (var member in interfaceType.Members)
        {
            string memberName = member.Key;
            TypeInfo expectedType = member.Value;
            bool isOptional = interfaceType.OptionalMembers.Contains(memberName);

            // Check: field, getter, or method (including inheritance chain)
            TypeInfo? actualType = FindMemberInClass(classType, memberName);

            if (actualType == null && !isOptional)
            {
                // tsc reports a single TS2420 per incorrectly-implemented interface (aggregating the
                // reasons). In recovery mode record at the class-name line and stop so sibling
                // declarations are still checked — throwing aborted the whole enclosing scope and
                // mis-attributed the diagnostic (#897). In non-recovery mode this stays fatal.
                EmitOrThrow(
                    $" Class '{className}' does not implement '{memberName}' from interface '{interfaceType.Name}'.",
                    classNameLine, "TS2420");
                return;
            }

            if (actualType != null)
            {
                // Special handling for method signature validation
                if (expectedType is TypeInfo.Function expectedFunc && actualType is TypeInfo.Function actualFunc)
                {
                    if (!ValidateMethodSignature(expectedFunc, actualFunc, memberName, className, interfaceType.Name, classNameLine))
                        return;
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
                        ValidateMethodSignature(expectedOverload.Signatures[0], actualFuncForOverload, memberName, className, interfaceType.Name, classNameLine);
                        return;
                    }
                }
                else if (!IsCompatible(expectedType, actualType))
                {
                    EmitOrThrow(
                        $" '{className}.{memberName}' has incompatible type. Expected '{expectedType}', got '{actualType}'.",
                        classNameLine, "TS2416");
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Re-checks that an un-annotated method implementing an interface member has a compatible
    /// INFERRED return type (TS2416), reported at the method's own line. The normal implements
    /// validation runs before the body pass, when an un-annotated return is still a <c>&lt;inferred&gt;</c>
    /// placeholder (≈ any) that hides the mismatch; this runs after inference. Only un-annotated
    /// implementations are considered, so annotated methods (already checked) aren't double-reported.
    /// </summary>
    private void RecheckImplementsInferredReturns(Stmt.Class classStmt, TypeInfo.Class classType, List<TypeInfo.Interface> interfaces)
    {
        foreach (var method in classStmt.Methods)
        {
            if (method.ReturnType != null || method.Body == null) continue;
            string name = method.ComputedKey != null
                ? (TryGetWellKnownSymbolMemberName(method.ComputedKey) ?? method.Name.Lexeme)
                : method.Name.Lexeme;
            if (FindMemberInClass(classType, name) is not TypeInfo.Function actualFunc) continue;
            int line = method.ComputedKey != null ? (TryGetExprLine(method.ComputedKey) ?? method.Name.Line) : method.Name.Line;
            foreach (var itf in interfaces)
            {
                if (itf.GetAllMembers().FirstOrDefault(m => m.Key == name).Value is TypeInfo.Function expectedFunc
                    && !IsCompatible(expectedFunc.ReturnType, actualFunc.ReturnType))
                {
                    string display = name.StartsWith("@@") ? $"[Symbol.{name[2..]}]" : name;
                    RecordTypeError(new TypeCheckException(
                        $" Property '{display}' in type '{classStmt.Name.Lexeme}' is not assignable to the same property in base type '{itf.Name}'.",
                        line: line, tsCode: "TS2416"));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The built-in iterable-protocol interfaces a class may legally list in its <c>implements</c>
    /// clause. These are NOT user-declared <c>interface</c>s (so they are absent from the type
    /// environment) but lib types resolved generically — a class satisfies them structurally via a
    /// <c>[Symbol.iterator]()</c> / <c>[Symbol.asyncIterator]()</c> member (#592, #756).
    /// </summary>
    private static readonly HashSet<string> IterableProtocolInterfaceNames =
    [
        "Iterable", "Iterator", "IterableIterator",
        "AsyncIterable", "AsyncIterator", "AsyncIterableIterator",
    ];

    /// <summary>
    /// Resolves an <c>implements</c>-clause name to a built-in iterable-protocol type when the name is
    /// one of the six protocol interfaces (<c>Iterable&lt;T&gt;</c>, <c>AsyncIterable&lt;T&gt;</c>, …).
    /// These names never reach the type environment as <see cref="TypeInfo.Interface"/>s, so the normal
    /// <c>implements</c> resolution rejects them as "not an interface" even when the class genuinely
    /// implements the protocol; this restores the lib-type resolution used in annotation position. The
    /// element type defaults to <c>any</c> when no type argument is supplied. Returns false for any
    /// other name (including other built-in generics such as <c>Array</c>/<c>Map</c>), leaving the
    /// caller's existing "is not an interface" diagnostic intact (#756).
    /// </summary>
    private bool TryResolveIterableProtocolInterface(string name, List<string>? typeArgStrings, out TypeInfo protocolType, List<TypeNode?>? typeArgNodes = null)
    {
        protocolType = null!;
        if (!IterableProtocolInterfaceNames.Contains(name))
            return false;

        List<TypeInfo> resolvedArgs = typeArgStrings is { Count: > 0 }
            ? typeArgStrings.Select((_, i) => ResolveTypeArg(typeArgStrings, typeArgNodes, i)).ToList()
            : [new TypeInfo.Any()];

        protocolType = ResolveGenericType(name, resolvedArgs);
        return true;
    }

    /// <summary>
    /// Validates that a class structurally implements a built-in iterable-protocol interface (resolved by
    /// <see cref="TryResolveIterableProtocolInterface"/>). Reuses the assignment-compatibility engine: a
    /// class instance is checked against the protocol target, which probes the class's
    /// <c>@@iterator</c>/<c>@@asyncIterator</c> member exactly as a <c>for...of</c>/<c>for await</c> would.
    /// Throws TS2420 (the same code the structural interface check uses) when the class does not satisfy
    /// the protocol (#756).
    /// </summary>
    private void ValidateProtocolInterfaceImplementation(TypeInfo classType, TypeInfo protocolType, string className)
    {
        if (!IsCompatible(protocolType, new TypeInfo.Instance(classType)))
            throw new TypeCheckException($" Class '{className}' incorrectly implements interface '{protocolType}'.", tsCode: "TS2420");
    }

    /// <summary>
    /// Validates that a class method signature is compatible with an interface method signature.
    /// For interface implementation, the class method must:
    /// 1. Accept at least as many required parameters as the interface method requires
    /// 2. Have compatible parameter types at each position
    /// 3. Have a compatible return type
    /// Records a TS2420 at the class-name line and returns <c>false</c> on the first incompatibility;
    /// returns <c>true</c> when compatible. Records rather than throws so sibling declarations are
    /// still checked (#897).
    /// </summary>
    private bool ValidateMethodSignature(TypeInfo.Function expected, TypeInfo.Function actual, string methodName, string className, string interfaceName, int? classNameLine)
    {
        // Check parameter count: actual must declare at least the required params from expected
        // The interface's MinArity tells us how many parameters callers will pass
        if (actual.ParamTypes.Count < expected.MinArity)
        {
            EmitOrThrow(
                $" Method '{className}.{methodName}' has {actual.ParamTypes.Count} parameter(s) but interface '{interfaceName}' requires at least {expected.MinArity}.",
                classNameLine, "TS2420");
            return false;
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
                EmitOrThrow(
                    $" Parameter {i + 1} of '{className}.{methodName}' has incompatible type. Interface '{interfaceName}' expects '{expectedParamType}', but got '{actualParamType}'.",
                    classNameLine, "TS2420");
                return false;
            }
        }

        // Check return type compatibility (covariant: actual's return can be subtype of expected's)
        if (!IsCompatible(expected.ReturnType, actual.ReturnType))
        {
            EmitOrThrow(
                $" Return type of '{className}.{methodName}' is incompatible. Interface '{interfaceName}' expects '{expected.ReturnType}', but got '{actual.ReturnType}'.",
                classNameLine, "TS2420");
            return false;
        }
        return true;
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
            throw new TypeCheckException($" Class '{className}' must implement the following abstract members: {string.Join(", ", missingMembers)}", tsCode: "TS2515");
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
    /// Validates that a class's own members are compatible with the members it overrides from its
    /// base class (the <c>extends</c> clause). Mirrors tsc's two-pronged reporting:
    /// <list type="bullet">
    /// <item>A named property whose <em>type</em> is not assignable to the base property's type is
    /// reported per-member as <b>TS2416</b> at the overriding member's declaration
    /// ("Property 'X' in type 'D' is not assignable to the same property in base type 'B'").</item>
    /// </list>
    /// Whole-class mismatches that cannot be pinned to a single property (index signatures,
    /// accessibility) are reported separately as TS2415; that path is handled elsewhere.
    /// Runs for generic classes too: a base member typed as an open type parameter can't be
    /// satisfied by a concrete override, and the base's parameters are substituted with the
    /// instantiation's type arguments so derived-vs-base parameter relations compare correctly.
    /// </summary>
    private void ValidateClassExtends(Stmt.Class classStmt, TypeInfo.Class classType)
    {
        TypeInfo? superclass = classType.Superclass;
        // Only meaningful when the base is an actual class (or a generic instantiation of one).
        if (superclass is not (TypeInfo.Class or TypeInfo.InstantiatedGeneric))
            return;

        // Whole-class accessibility mismatch is reported once, at the class declaration, as TS2415.
        bool accessibilityErrorReported = false;

        // For a generic-instantiated base (`extends C3<T>`), the base's member types are expressed
        // in the base's own type parameters; substitute the instantiation's type arguments so the
        // override comparison uses the derived class's parameters (e.g. `C3<T>.foo` resolves to the
        // derived `T`, not C3's own `T`). Without this, a legitimate override like
        // `class D<T extends U, U> extends C3<U> { foo: U }` would falsely compare `U` against C3's `T`.
        Dictionary<string, TypeInfo>? substitutedBaseMembers =
            superclass is TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass gc } ig
                ? CollectGenericClassMembers(gc, ig.TypeArguments, includeNonPublic: true)
                : null;

        foreach (var field in classStmt.Fields)
        {
            // Statics, ES #private fields and computed keys don't participate in structural
            // override checking against the base class.
            if (field.IsStatic || field.IsPrivate || field.ComputedKey != null)
                continue;

            string name = field.Name.Lexeme;
            TypeInfo? baseType = substitutedBaseMembers is not null
                ? (substitutedBaseMembers.TryGetValue(name, out var bt) ? bt : null)
                : FindMemberInBaseChain(superclass, name);
            if (baseType is null)
                continue; // not an override — nothing to relate against

            // Accessibility must match across the override. A `private`/`public` mismatch makes the
            // derived class incorrectly extend its base (tsc: "Property 'X' is private in type 'B'
            // but not in type 'A'"). Reported once per class as TS2415 at the class name.
            if (!accessibilityErrorReported)
            {
                AccessModifier? baseAccess = FindMemberAccessInBaseChain(superclass, name);
                if (baseAccess is not null && (field.Access == AccessModifier.Private) != (baseAccess == AccessModifier.Private))
                {
                    RecordTypeError(new TypeCheckException(
                        $" Class '{classStmt.Name.Lexeme}' incorrectly extends base class '{BaseTypeDisplayName(superclass)}'. Property '{name}' has mismatched accessibility.",
                        line: classStmt.Name.Line,
                        tsCode: "TS2415"));
                    accessibilityErrorReported = true;
                }
            }

            // A base property typed `any`/`undefined`/`null` accepts any override under the default
            // (non-strict) configuration — e.g. `foo: typeof undefined` widens to `any` in tsc, so
            // overriding it with a concrete type is not an error. Skip to avoid false positives.
            if (baseType is TypeInfo.Any or TypeInfo.Undefined or TypeInfo.Null)
                continue;

            if (!classType.FieldTypes.TryGetValue(name, out var derivedType))
                continue;

            // Derived member must be assignable TO the base member (covariant property override).
            if (!IsCompatible(baseType, derivedType))
            {
                RecordTypeError(new TypeCheckException(
                    $" Property '{name}' in type '{classStmt.Name.Lexeme}' is not assignable to the same property in base type '{BaseTypeDisplayName(superclass)}'.",
                    line: field.Name.Line,
                    tsCode: "TS2416"));
            }
        }
    }

    /// <summary>
    /// Validates that a class's own index signatures are compatible with those it overrides from its
    /// base class (TS2415 "Class 'D' incorrectly extends base class 'B'", index-signature variant).
    /// The derived index value type must be assignable to the base's (after substituting the base's
    /// type arguments). Unlike <see cref="ValidateClassExtends"/> this also runs for <em>generic</em>
    /// classes: `class B3&lt;T extends Base&gt; extends A&lt;T&gt; { [x: number]: Derived }` is an error
    /// because the base index resolves to the open `T`, and a concrete `Derived` is not assignable to
    /// `T` (T could be instantiated with a different subtype of its constraint).
    /// </summary>
    private void ValidateClassIndexSignatureExtends(Stmt.Class classStmt, TypeInfo.Class classType)
    {
        TypeInfo? superclass = classType.Superclass;
        if (superclass is not (TypeInfo.Class or TypeInfo.InstantiatedGeneric))
            return;

        // tsc reports a single TS2415 per class even when both index kinds mismatch.
        foreach (var (derived, baseSub, kind) in new[]
        {
            (classType.StringIndexType, StringIndexOf(superclass), "string"),
            (classType.NumberIndexType, NumberIndexOf(superclass), "number"),
        })
        {
            // Only an *overriding* index signature is checked — if the base has none, or the
            // derived doesn't redeclare one, there's nothing to relate.
            if (derived is null || baseSub is null) continue;
            if (!IsCompatible(baseSub, derived))
            {
                RecordTypeError(new TypeCheckException(
                    $" Class '{classStmt.Name.Lexeme}' incorrectly extends base class '{BaseTypeDisplayName(superclass)}'. The '{kind}' index signatures are incompatible.",
                    line: classStmt.Name.Line,
                    tsCode: "TS2415"));
                return;
            }
        }
    }

    /// <summary>
    /// Validates that a class's index signatures conform to those of an interface it <c>implements</c>
    /// (TS2420 "Class 'B' incorrectly implements interface 'A'", index-signature variant) — the
    /// implements-path counterpart of <see cref="ValidateClassIndexSignatureExtends"/>. The class's
    /// index value type must be assignable to the interface's (after the interface's type arguments are
    /// substituted). Because a numeric key stringifies, a class <c>[x: string]</c> index also supplies
    /// the value type for the interface's numeric index. Unlike the named-member check this runs for
    /// <em>generic</em> classes too: `class B&lt;T extends Derived&gt; implements A&lt;T&gt; { [x: string]: Base }`
    /// is an error because the interface index resolves to the open `T`, and a concrete `Base` is not
    /// assignable to `T` (#897).
    /// </summary>
    private void ValidateInterfaceIndexSignatureImplementation(Stmt.Class classStmt, TypeInfo.Class classType, TypeInfo interfaceResolved)
    {
        // The string-index check uses the class's own string index. For the numeric-index check the
        // class's effective value type is its numeric index, falling back to its string index (which
        // covers numeric keys).
        TypeInfo? classString = StringIndexOf(classType);
        TypeInfo? classNumber = NumberIndexOf(classType) ?? classString;

        foreach (var (ifaceIndex, classEffective, kind) in new[]
        {
            (StringIndexOf(interfaceResolved), classString, "string"),
            (NumberIndexOf(interfaceResolved), classNumber, "number"),
        })
        {
            // Only check when the interface declares this index kind and the class supplies an
            // effective value for it; a class missing an index the interface requires is left alone
            // (conservative — avoids over-reporting beyond the tsc baselines this targets).
            if (ifaceIndex is null || classEffective is null) continue;
            // The class's index value must be assignable TO the interface's index value.
            if (!IsCompatible(ifaceIndex, classEffective))
            {
                RecordTypeError(new TypeCheckException(
                    $" Class '{classStmt.Name.Lexeme}' incorrectly implements interface '{InterfaceDisplayName(interfaceResolved)}'. The '{kind}' index signatures are incompatible.",
                    line: classStmt.Name.Line,
                    tsCode: "TS2420"));
                return;
            }
        }
    }

    /// <summary>Best-effort display name for an interface (plain or generic-instantiated) in diagnostics.</summary>
    private static string InterfaceDisplayName(TypeInfo interfaceType) => interfaceType switch
    {
        TypeInfo.Interface i => i.Name,
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface gi } => gi.Name,
        _ => interfaceType.ToString() ?? "interface"
    };

    /// <summary>
    /// Validates that a class's own declared instance properties are assignable to its own string
    /// index signature (TS2411 "Property 'X' of type '…' is not assignable to 'string' index type
    /// '…'"). Mirrors the interface check in <c>CheckInterfaceDeclaration</c>; reported per-property
    /// at the property's own declaration line. Runs for generic classes too, where the index type is
    /// an open type parameter (e.g. `class D&lt;T extends U, U&gt; { [x: string]: T; foo: U }` is an
    /// error because `U` is not assignable to the index type `T`).
    /// </summary>
    private void ValidateClassPropertiesAgainstIndex(Stmt.Class classStmt, TypeInfo.Class classType)
    {
        if (classType.StringIndexType is not { } stringIndexType)
            return;

        foreach (var field in classStmt.Fields)
        {
            // Index signatures relate to instance, non-#private, named properties only.
            if (field.IsStatic || field.IsPrivate || field.ComputedKey != null)
                continue;
            if (!classType.FieldTypes.TryGetValue(field.Name.Lexeme, out var fieldType))
                continue;
            if (!IsCompatible(stringIndexType, fieldType))
            {
                RecordTypeError(new TypeCheckException(
                    $" Property '{field.Name.Lexeme}' of type '{fieldType}' is not assignable to 'string' index type '{stringIndexType}'.",
                    line: field.Name.Line,
                    tsCode: "TS2411"));
            }
        }
    }
    /// <summary>
    /// Validates that a class's symbol-keyed members (own and inherited) are assignable to its
    /// effective <c>[s: symbol]</c> index type (own or inherited) — TS2411, the symbol-index analogue
    /// of <see cref="ValidateClassPropertiesAgainstIndex"/>. Runs after method bodies so inferred
    /// return types are resolved. tsc attributes the diagnostic to whichever of {member, index
    /// signature} is declared ON this class: an OWN member reports at the member, an INHERITED member
    /// (necessarily paired with an OWN index signature) reports at the index signature. A member+index
    /// pair both inherited from the same base was already reported on that base and is skipped.
    /// </summary>
    private void ValidateClassMembersAgainstSymbolIndex(Stmt.Class classStmt, TypeInfo.Class classType)
    {
        TypeInfo? ownSymIndex = classType.SymbolIndexType;
        TypeInfo? effectiveIndex = ownSymIndex ?? FindSymbolIndexInBaseChain(classType.Superclass);
        if (effectiveIndex is null) return;

        void CheckOwnMember(string atName, TypeInfo memberType, int line)
        {
            if (!IsCompatible(effectiveIndex, memberType))
                RecordTypeError(new TypeCheckException(
                    $" Property '[Symbol.{atName[2..]}]' of type '{memberType}' is not assignable to 'symbol' index type '{effectiveIndex}'.",
                    line: line, tsCode: "TS2411"));
        }

        // Own symbol-keyed members: methods and fields whose computed key is a well-known symbol.
        var ownNames = new HashSet<string>();
        foreach (var method in classStmt.Methods)
        {
            if (method.ComputedKey is null) continue;
            string? name = TryGetWellKnownSymbolMemberName(method.ComputedKey);
            if (name is null || !classType.Methods.TryGetValue(name, out var mType)) continue;
            ownNames.Add(name);
            CheckOwnMember(name, mType, TryGetExprLine(method.ComputedKey) ?? method.Name.Line);
        }
        foreach (var field in classStmt.Fields)
        {
            if (field.IsStatic || field.ComputedKey is null) continue;
            string? name = TryGetWellKnownSymbolMemberName(field.ComputedKey);
            if (name is null || !classType.FieldTypes.TryGetValue(name, out var fType)) continue;
            ownNames.Add(name);
            CheckOwnMember(name, fType, TryGetExprLine(field.ComputedKey) ?? field.Name.Line);
        }

        // Inherited symbol-keyed members are checked only when the index signature is OWN to this
        // class (otherwise the member+index pair is fully inherited and the base already reported).
        // They are attributed to this class's index-signature line.
        if (ownSymIndex is not null)
        {
            int? indexLine = classStmt.IndexSignatures?
                .FirstOrDefault(s => s.KeyType == TokenType.TYPE_SYMBOL)?.KeyName.Line;
            foreach (var (name, mType) in CollectInheritedSymbolMembers(classType.Superclass))
            {
                if (ownNames.Contains(name)) continue; // overridden here → own-member path handled it
                if (!IsCompatible(ownSymIndex, mType))
                    RecordTypeError(new TypeCheckException(
                        $" Property '[Symbol.{name[2..]}]' of type '{mType}' is not assignable to 'symbol' index type '{ownSymIndex}'.",
                        line: indexLine, tsCode: "TS2411"));
            }
        }
    }

    private static TypeInfo? GetSymbolIndexType(TypeInfo? classType) => classType switch
    {
        TypeInfo.Class c => c.SymbolIndexType,
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass gc } => gc.SymbolIndexType,
        TypeInfo.GenericClass g => g.SymbolIndexType,
        _ => null
    };

    private TypeInfo? FindSymbolIndexInBaseChain(TypeInfo? baseType)
    {
        for (TypeInfo? current = baseType; current != null; current = GetSuperclass(current))
            if (GetSymbolIndexType(current) is { } idx) return idx;
        return null;
    }

    /// <summary>Symbol-keyed (<c>@@name</c>) methods and fields inherited from the base-class chain,
    /// each yielded once (nearest override wins).</summary>
    private IEnumerable<(string Name, TypeInfo Type)> CollectInheritedSymbolMembers(TypeInfo? baseType)
    {
        var seen = new HashSet<string>();
        for (TypeInfo? current = baseType; current != null; current = GetSuperclass(current))
        {
            if (GetMethods(current) is { } methods)
                foreach (var kv in methods)
                    if (kv.Key.StartsWith("@@") && seen.Add(kv.Key))
                        yield return (kv.Key, kv.Value);
            if (GetFieldTypes(current) is { } fields)
                foreach (var kv in fields)
                    if (kv.Key.StartsWith("@@") && seen.Add(kv.Key))
                        yield return (kv.Key, kv.Value);
        }
    }

    /// <summary>
    /// Walks the base-class chain looking for the accessibility of a field, getter or method named
    /// <paramref name="name"/>. Returns its <see cref="AccessModifier"/>, or null if absent.
    /// </summary>
    private AccessModifier? FindMemberAccessInBaseChain(TypeInfo? baseType, string name)
    {
        TypeInfo? current = baseType;
        while (current != null)
        {
            var fieldAccess = GetFieldAccess(current);
            var methodAccess = GetMethodAccess(current);
            if (fieldAccess != null && fieldAccess.TryGetValue(name, out var fa)) return fa;
            if (methodAccess != null && methodAccess.TryGetValue(name, out var ma)) return ma;
            current = GetSuperclass(current);
        }
        return null;
    }

    /// <summary>
    /// Walks the base-class chain (handling both plain and generic-instantiated bases) looking for a
    /// field, getter or method named <paramref name="name"/>. Returns its type, or null if absent.
    /// </summary>
    private TypeInfo? FindMemberInBaseChain(TypeInfo? baseType, string name)
    {
        TypeInfo? current = baseType;
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

    /// <summary>Best-effort display name for a base type in inheritance diagnostics.</summary>
    private static string BaseTypeDisplayName(TypeInfo baseType) => baseType switch
    {
        TypeInfo.Class c => c.Name,
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass gc } => gc.Name,
        _ => baseType.ToString() ?? "base"
    };

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
                    throw new TypeCheckException($" Method '{methodName}' is marked as override but does not override any method in a base class.", tsCode: "TS4113");
                }
            }
        }

        // Check accessors marked with override
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                // Computed accessor names can't be validated against parent members.
                if (accessor.ComputedKey != null)
                {
                    continue;
                }

                if (accessor.IsOverride)
                {
                    string propertyName = accessor.Name.Lexeme;
                    bool isGetter = accessor.Kind.Type == Parsing.TokenType.GET;

                    if (isGetter)
                    {
                        if (!HasParentGetter(classType.Superclass, propertyName))
                        {
                            throw new TypeCheckException($" Getter '{propertyName}' is marked as override but does not override any getter in a base class.", tsCode: "TS4113");
                        }
                    }
                    else
                    {
                        if (!HasParentSetter(classType.Superclass, propertyName))
                        {
                            throw new TypeCheckException($" Setter '{propertyName}' is marked as override but does not override any setter in a base class.", tsCode: "TS4113");
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
