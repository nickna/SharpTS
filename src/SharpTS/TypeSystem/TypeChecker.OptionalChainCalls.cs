using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // An invocation may skip a nullable receiver without invoking an undefined method.
    // This receipt belongs to the actual Get resolution; it is not a receiver/native fact.
    private readonly Dictionary<Expr.Get, TypeInfo> _optionalChainMemberTargets =
        new(ReferenceEqualityComparer.Instance);

    private bool TryGetOptionalMemberCallType(Expr callee, out TypeInfo memberType)
    {
        bool asserted = false;
        while (callee is Expr.NonNullAssertion assertion)
        {
            asserted = true;
            callee = assertion.Expression;
        }
        // A grouping ends the chain. Do not unwrap it as a callable Reference.
        if (callee is Expr.Get { Optional: true } get && _optionalChainMemberTargets.TryGetValue(get, out memberType!))
        {
            if (asserted && memberType is TypeInfo.Union union)
            {
                var present = union.FlattenedTypes
                    .Where(type => type is not (TypeInfo.Null or TypeInfo.Undefined)).ToList();
                memberType = present.Count == 0 ? TypeInfo.Never.Shared
                    : present.Count == 1 ? present[0] : new TypeInfo.Union(present);
            }
            return true;
        }
        memberType = null!;
        return false;
    }

    private bool OptionalCallCanShortCircuit(Expr.Call call)
    {
        if (TryGetOptionalMemberCallType(call.Callee, out _)) return true;
        return call.Optional && (_typeMap.Get(call.Callee) switch
        {
            TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Null or TypeInfo.Undefined => true,
            TypeInfo.Union union => union.ContainsNull || union.ContainsUndefined,
            _ => false
        });
    }
}
