using SharpTS.TypeSystem.Exceptions;
using SharpTS.Runtime.BuiltIns;
using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Named relation helpers extracted from the leading sections of
/// <see cref="TypeChecker.IsCompatibleCore"/> (#1140). Each mirrors the existing
/// <c>TryRelateDeferredMappedType</c> shape: it returns <c>true</c> with
/// <c>result</c> set when it decides the relation for its type category, and
/// <c>false</c> (leaving the decision to the remaining rules) otherwise. Splitting
/// these out turns the top of the single sequential cascade into self-describing
/// units without changing evaluation order or behaviour.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Type-predicate targets: a regular predicate <c>x is T</c> expects a boolean
    /// return; an assertion predicate <c>asserts x is T</c> and a bare
    /// <c>asserts x</c> expect void (the function throws on failure).
    /// </summary>
    private bool TryRelateTypePredicate(TypeInfo expected, TypeInfo actual, out bool result)
    {
        if (expected is TypeInfo.TypePredicate pred)
        {
            if (pred.IsAssertion)
            {
                // Assertion predicates return void (or throw)
                result = actual is TypeInfo.Void or TypeInfo.Never;
                return true;
            }
            else
            {
                // Regular type predicates return boolean
                result = actual is TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_BOOLEAN }
                    or TypeInfo.BooleanLiteral;
                return true;
            }
        }
        if (expected is TypeInfo.AssertsNonNull)
        {
            // AssertsNonNull returns void (or throws)
            result = actual is TypeInfo.Void or TypeInfo.Never;
            return true;
        }
        result = false;
        return false;
    }

    /// <summary>
    /// Type-parameter compatibility (TypeScript: type parameters are not assignable
    /// to one another unless directly or indirectly constrained to one another);
    /// an arbitrary concrete type is not assignable to a bare type parameter; and a
    /// source type parameter is assignable wherever its constraint is.
    /// </summary>
    private bool TryRelateTypeParameters(TypeInfo expected, TypeInfo actual, out bool result)
    {
        // Type-parameter compatibility (TypeScript: "type parameters are not assignable to one
        // another unless directly or indirectly constrained to one another").
        if (expected is TypeInfo.TypeParameter expectedTp && actual is TypeInfo.TypeParameter actualTp)
        {
            // The same parameter, or a source transitively constrained to the target (U extends … extends T).
            result = expectedTp.Name == actualTp.Name || TypeParameterConstrainedTo(actualTp, expectedTp.Name);
            return true;
        }

        // Expected is a bare type parameter and the source is some other type. An arbitrary concrete
        // type is NOT assignable to a type parameter — only `never`, or an intersection one of whose
        // constituents is (T & Function → T). (any / inferred and, under non-strict, null / undefined
        // are already accepted earlier in IsCompatibleCore; a source type parameter is handled by the
        // case above.) This is the strict TypeScript rule.
        if (expected is TypeInfo.TypeParameter)
        {
            if (actual is TypeInfo.Intersection actIntForTp)
                result = actIntForTp.FlattenedTypes.Any(t => IsCompatible(expected, t));
            else
                result = actual is TypeInfo.Never;
            return true;
        }

        // Source is a type parameter assigned to a non-parameter target: it is assignable wherever its
        // apparent (constraint) type is assignable. Also assignable into a union that contains it.
        if (actual is TypeInfo.TypeParameter actualTpOnly)
        {
            if (expected is TypeInfo.Any or TypeInfo.Unknown) { result = true; return true; }
            if (expected is TypeInfo.Union expUnionForTp &&
                expUnionForTp.FlattenedTypes.Any(t =>
                    t is TypeInfo.TypeParameter unionTp && unionTp.Name == actualTpOnly.Name))
            {
                result = true;
                return true;
            }
            var apparent = ApparentTypeOf(actualTpOnly);
            if (apparent == null)
            {
                // An unconstrained type parameter has no apparent (constraint) type. tsc still relates
                // it to a target that requires NO members — an all-optional / empty object type — its
                // "subtyping assumes transitivity for optional properties (99% case)" allowance. It is
                // NOT assignable to a primitive, `object`, or any target with a required member.
                result = expected switch
                {
                    TypeInfo.Record rec => HasNoRequiredMembers(rec),
                    TypeInfo.Interface itf => !itf.HasIndexSignature && !itf.IsCallable && !itf.IsConstructable
                        && itf.GetAllMembers().All(m => itf.GetAllOptionalMembers().Contains(m.Key)),
                    _ => false
                };
                return true;
            }
            result = IsCompatible(expected, apparent);
            return true;
        }
        result = false;
        return false;
    }

    /// <summary>
    /// The bottom/top/object trivial relations: <c>never</c> as source (assignable
    /// to anything) or target (accepts only never); <c>unknown</c> as target (top)
    /// or source; and the non-primitive <c>object</c> type in either position.
    /// </summary>
    private bool TryRelateNeverUnknownObject(TypeInfo expected, TypeInfo actual, out bool result)
    {
        // never as actual: assignable to anything (bottom type)
        if (actual is TypeInfo.Never) { result = true; return true; }

        // never as expected: nothing assignable to never except never
        if (expected is TypeInfo.Never) { result = actual is TypeInfo.Never; return true; }

        // unknown as expected: anything can be assigned TO unknown (top type)
        if (expected is TypeInfo.Unknown) { result = true; return true; }

        // unknown as actual: can only be assigned to unknown or any
        if (actual is TypeInfo.Unknown)
        {
            result = expected is TypeInfo.Unknown || expected is TypeInfo.Any;
            return true;
        }

        // object type: accepts non-primitive, non-null values
        if (expected is TypeInfo.Object)
        {
            if (actual is TypeInfo.Never) { result = true; return true; }  // never is bottom type
            if (actual is TypeInfo.Any) { result = true; return true; }    // any is assignable to anything
            if (actual is TypeInfo.Object) { result = true; return true; } // object to object
            if (IsPrimitiveType(actual)) { result = false; return true; }  // reject primitives
            if (actual is TypeInfo.Null or TypeInfo.Undefined) { result = false; return true; }
            // Accept: Record, Array, Instance, Class, Function, Map, Set, etc.
            result = true;
            return true;
        }

        // object as actual: assignable to object/any/unknown, and to any object
        // type that demands nothing of it — `{}` or an all-optional shape like
        // `{ t?: string }`. The bare non-primitive `object` satisfies such a
        // target because it has no required members to be missing (matches tsc).
        if (actual is TypeInfo.Object)
        {
            result = expected is TypeInfo.Object or TypeInfo.Any or TypeInfo.Unknown
                     || (expected is TypeInfo.Record rec && HasNoRequiredMembers(rec));
            return true;
        }
        result = false;
        return false;
    }

    /// <summary>
    /// True when an object type requires nothing of its source: every declared
    /// field is optional and there are no call / construct / index signatures.
    /// The bare non-primitive <c>object</c> type is assignable to such a target
    /// (<c>{}</c>, <c>{ t?: string }</c>), matching tsc.
    /// </summary>
    private static bool HasNoRequiredMembers(TypeInfo.Record rec)
    {
        if (rec.HasCallSignature || rec.HasConstructorSignature || rec.HasIndexSignature)
            return false;
        foreach (var name in rec.Fields.Keys)
            if (!rec.IsFieldOptional(name)) return false;
        return true;
    }

    /// <summary>
    /// Null / undefined as source under strictNullChecks (the non-strict case is
    /// handled at the very top of IsCompatibleCore): assignable only to a matching
    /// bare type or a union that includes null / undefined respectively.
    /// </summary>
    private bool TryRelateNullUndefinedStrict(TypeInfo expected, TypeInfo actual, out bool result)
    {
        // Null compatibility (strictNullChecks: on — the off case is handled early in IsCompatibleCore)
        if (actual is TypeInfo.Null)
        {
            if (expected is TypeInfo.Union u && u.ContainsNull) { result = true; return true; }
            if (expected is TypeInfo.Null) { result = true; return true; }
            result = false;
            return true;
        }

        // Undefined compatibility (strictNullChecks: on)
        if (actual is TypeInfo.Undefined)
        {
            if (expected is TypeInfo.Union u && u.ContainsUndefined) { result = true; return true; }
            if (expected is TypeInfo.Undefined) { result = true; return true; }
            result = false;
            return true;
        }
        result = false;
        return false;
    }

    /// <summary>
    /// Literal-type relations: literal-to-literal equality, literal-to-primitive
    /// widening, template-literal pattern matching, and intrinsic string types.
    /// Each arm decides only its specific shape and otherwise falls through.
    /// </summary>
    private bool TryRelateLiteralTypes(TypeInfo expected, TypeInfo actual, out bool result)
    {
        // Literal type compatibility - literal to literal (must have same value)
        if (expected is TypeInfo.StringLiteral sl1 && actual is TypeInfo.StringLiteral sl2)
            { result = sl1.Value == sl2.Value; return true; }
        if (expected is TypeInfo.NumberLiteral nl1 && actual is TypeInfo.NumberLiteral nl2)
            { result = nl1.Value == nl2.Value; return true; }
        if (expected is TypeInfo.BooleanLiteral bl1 && actual is TypeInfo.BooleanLiteral bl2)
            { result = bl1.Value == bl2.Value; return true; }
        if (expected is TypeInfo.BigIntLiteral bil1 && actual is TypeInfo.BigIntLiteral bil2)
            { result = bil1.Value == bil2.Value; return true; }

        // Literal to primitive widening
        if (expected is TypeInfo.String && actual is TypeInfo.StringLiteral)
            { result = true; return true; }
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } && actual is TypeInfo.NumberLiteral)
            { result = true; return true; }
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } && actual is TypeInfo.BooleanLiteral)
            { result = true; return true; }
        if (expected is TypeInfo.BigInt && actual is TypeInfo.BigIntLiteral)
            { result = true; return true; }

        // Template literal type compatibility

        // Template literal widens to string
        if (expected is TypeInfo.String && actual is TypeInfo.TemplateLiteralType)
            { result = true; return true; }

        // String literal matches template literal pattern
        if (expected is TypeInfo.TemplateLiteralType expectedTL && actual is TypeInfo.StringLiteral actualSL)
            { result = MatchesTemplateLiteralPattern(expectedTL, actualSL.Value); return true; }

        // Template literal to template literal: structural compatibility
        if (expected is TypeInfo.TemplateLiteralType expTL && actual is TypeInfo.TemplateLiteralType actTL)
            { result = TemplatePatternStructurallyCompatible(expTL, actTL); return true; }

        // Intrinsic string type: evaluate and check
        if (actual is TypeInfo.IntrinsicStringType ist)
        {
            var evaluated = EvaluateIntrinsicStringType(ist.Inner, ist.Operation);
            result = IsCompatible(expected, evaluated);
            return true;
        }
        result = false;
        return false;
    }
}
