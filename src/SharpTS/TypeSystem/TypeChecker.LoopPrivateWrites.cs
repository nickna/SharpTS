using SharpTS.Parsing;
using SharpTS.TypeSystem.Narrowing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private bool LoopWritesPreserveNestedPrivateGuard(NarrowingPath path,
        Dictionary<NarrowingPath, List<Expr?>> assignments)
    {
        // Only refine the nested private receivers newly tracked by loop analysis.
        // Keep the existing invalidation policy for other loop assignments.
        if (!HasPrivateReceiver(path) || GetNarrowing(path) is not { } narrowed)
            return false;

        foreach (var (assignedPath, writes) in assignments)
        {
            if (!path.IsAffectedByAssignmentTo(assignedPath)) continue;
            // Replacing a receiver or writing a descendant can change the guarded
            // value independently of these exact writes.
            if (!path.Equals(assignedPath)) return false;
            foreach (var write in writes)
                if (GuaranteedLoopWriteType(write, narrowed) is not { } result ||
                    !IsCompatible(narrowed, result))
                    return false;
        }
        return true;
    }

    private static bool HasPrivateReceiver(NarrowingPath path) => path switch
    {
        NarrowingPath.PrivateAccess member => ContainsPrivateAccess(member.Base),
        NarrowingPath.PropertyAccess member => ContainsPrivateAccess(member.Base),
        NarrowingPath.ElementAccess element => ContainsPrivateAccess(element.Base),
        _ => false
    };

    private static bool ContainsPrivateAccess(NarrowingPath path) => path switch
    {
        NarrowingPath.PrivateAccess => true,
        NarrowingPath.PropertyAccess member => ContainsPrivateAccess(member.Base),
        NarrowingPath.ElementAccess element => ContainsPrivateAccess(element.Base),
        _ => false
    };

    private TypeInfo? GuaranteedLoopWriteType(Expr? write, TypeInfo narrowed)
    {
        if (write is Expr.PrefixIncrement or Expr.PostfixIncrement)
            return narrowed switch
            {
                TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => TypeInfo.Primitive.Number,
                TypeInfo.BigInt => TypeInfo.BigInt.Shared,
                _ => null
            };

        Expr? value = write switch
        {
            Expr.Set assignment => assignment.Value,
            Expr.SetPrivate assignment => assignment.Value,
            Expr.SetIndex assignment => assignment.Value,
            Expr.LogicalSet assignment => assignment.Value,
            Expr.LogicalSetPrivate assignment => assignment.Value,
            Expr.LogicalSetIndex assignment => assignment.Value,
            _ => null
        };
        while (value is Expr.Grouping grouping) value = grouping.Expression;
        // Do not check a body expression before its declarations and flow are
        // established. Literal writes provide a context-independent proof.
        return value is Expr.Literal literal ? GetLiteralType(literal.Value) : null;
    }
}
