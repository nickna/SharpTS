using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    internal TypeInfo VisitAssertedAssignment(Expr.AssertedAssignment expression)
    {
        Expr reference = AssignmentReference(expression.TargetSyntax);
        var (value, op) = AssignmentValue(expression.LoweredAssignment);
        bool assertsType = HasTypeAssignmentView(expression.TargetSyntax);
        TypeInfo targetType;
        TypeInfo result;
        if (!assertsType)
        {
            // A bare non-null assertion keeps the ordinary reference permissions.
            // Its target is a write, so an uninitialized variable is not read here.
            if (reference is Expr.Variable variable)
            {
                BindValueUse(variable.Name);
                if (Bindings.GetSymbol(variable.Name) is { } symbol && _typeMap.IsConstBinding(symbol))
                    throw new TypeCheckException($"Cannot assign to '{variable.Name.Lexeme}' because it is a constant.",
                        variable.Name.Line, tsCode: "TS2588");
            }
            result = CheckExpr(expression.LoweredAssignment);
            targetType = ExpandNonNullable(AssignmentSlotType(reference));
            TypeInfo assigned = _typeMap.Get(value) ?? result;
            if (!IsCompatible(targetType, assigned))
                throw new TypeCheckException($"Type '{assigned}' is not assignable to type '{targetType}'.",
                    TryGetExprLine(value), tsCode: "TS2322");
        }
        else
        {
            // Check the written assertions, including their overlap/constraint and
            // source-member access rules. They never execute in either backend.
            targetType = CheckExpr(expression.TargetSyntax);
            TypeInfo right = CheckExprWithContext(value, op is null ? targetType : null);
            result = op?.Type switch
            {
                null => right,
                TokenType.OR_OR_EQUAL => CreateUnion(NarrowLogicalTruthy(targetType), right),
                TokenType.AND_AND_EQUAL => CreateUnion(NarrowLogicalFalsy(targetType), right),
                TokenType.QUESTION_QUESTION_EQUAL => CreateUnion(ExpandNonNullable(targetType), right),
                TokenType.PLUS_EQUAL => CheckPlusOperator(targetType, right, op.Line),
                TokenType.AMPERSAND_EQUAL or TokenType.PIPE_EQUAL or TokenType.CARET_EQUAL or
                    TokenType.LESS_LESS_EQUAL or TokenType.GREATER_GREATER_EQUAL => CheckBitwiseBinary(targetType, right, op.Line),
                TokenType.GREATER_GREATER_GREATER_EQUAL => CheckUnsignedShiftBinary(targetType, right, op.Line),
                _ => CheckArithmeticBinary(targetType, right, op.Line),
            };
            TypeInfo stored = op?.Type is TokenType.OR_OR_EQUAL or TokenType.AND_AND_EQUAL or TokenType.QUESTION_QUESTION_EQUAL
                ? right : result;
            if (!IsCompatible(targetType, stored))
                throw new TypeCheckException($"Type '{stored}' is not assignable to type '{targetType}'.",
                    TryGetExprLine(value), tsCode: "TS2322");

            // Only this explicit operation uses the assertion's type. Nested RHS
            // writes go through their normal checker; no ambient override leaks.
            _typeMap.Set(expression.LoweredAssignment, result);
            var path = ExtractNarrowingPath(reference);
            if (path is not null) InvalidateNarrowingsFor(path);
            if (reference is Expr.Variable written)
            {
                BindValueUse(written.Name);
                if (GetDeclaredType(written.Name.Lexeme) is { } declared)
                {
                    WidenEnclosingNarrowing(written.Name.Lexeme, declared);
                    _environment.Define(written.Name.Lexeme, declared);
                }
                MarkDefinitelyAssigned(written.Name);
            }
        }
        if (reference is Expr.Variable slot && Bindings.GetSymbol(slot.Name) is { } binding)
            _typeMap.MarkAssertedAssignmentStorage(binding);
        return result;
    }

    private TypeInfo AssignmentSlotType(Expr reference) => reference switch
    {
        Expr.Variable variable => GetDeclaredType(variable.Name.Lexeme) ?? LookupVariable(variable.Name),
        Expr.Get get when _typeMap.Get(get.Object) is { } receiver => CheckGetOnType(receiver, get.Name),
        Expr.GetPrivate get when FindLexicalPrivateOwner(get.Name.Lexeme) is { } owner =>
            owner.PrivateFieldTypes.GetValueOrDefault(get.Name.Lexeme)
                ?? owner.StaticPrivateFieldTypes.GetValueOrDefault(get.Name.Lexeme) ?? TypeInfo.Any.Shared,
        _ => CheckExpr(reference),
    };

    private static Expr AssignmentReference(Expr expression) => expression switch
    {
        Expr.Grouping grouping => AssignmentReference(grouping.Expression),
        Expr.TypeAssertion assertion => AssignmentReference(assertion.Expression),
        Expr.NonNullAssertion assertion => AssignmentReference(assertion.Expression),
        Expr.Satisfies assertion => AssignmentReference(assertion.Expression),
        _ => expression,
    };

    private static bool HasTypeAssignmentView(Expr expression) => expression switch
    {
        Expr.Grouping grouping => HasTypeAssignmentView(grouping.Expression),
        Expr.NonNullAssertion assertion => HasTypeAssignmentView(assertion.Expression),
        Expr.TypeAssertion or Expr.Satisfies => true,
        _ => false,
    };

    private static (Expr Value, Token? Operator) AssignmentValue(Expr assignment) => assignment switch
    {
        Expr.Assign assign => (assign.Value, null),
        Expr.Set assign => (assign.Value, null),
        Expr.SetIndex assign => (assign.Value, null),
        Expr.SetPrivate assign => (assign.Value, null),
        Expr.SetSuper assign => (assign.Value, null),
        Expr.CompoundAssign assign => (assign.Value, assign.Operator),
        Expr.CompoundSet assign => (assign.Value, assign.Operator),
        Expr.CompoundSetIndex assign => (assign.Value, assign.Operator),
        Expr.CompoundSetPrivate assign => (assign.Value, assign.Operator),
        Expr.LogicalAssign assign => (assign.Value, assign.Operator),
        Expr.LogicalSet assign => (assign.Value, assign.Operator),
        Expr.LogicalSetIndex assign => (assign.Value, assign.Operator),
        Expr.LogicalSetPrivate assign => (assign.Value, assign.Operator),
        _ => throw new InvalidOperationException("An asserted assignment must contain a reference write."),
    };
}
