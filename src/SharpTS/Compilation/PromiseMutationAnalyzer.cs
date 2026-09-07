using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Detects observable Promise mutation that invalidates primitive Promise optimizations.
/// The compiler shares one result across the all and then eligibility passes.
/// </summary>
internal static class PromiseMutationAnalyzer
{
    public static bool HasObservableMutation(IReadOnlyList<Stmt> program, TypeMap typeMap)
    {
        var visitor = new PromiseMutationVisitor(typeMap);
        foreach (var statement in program)
            visitor.Visit(statement);
        return visitor.HasObservableMutation;
    }

    private sealed class PromiseMutationVisitor(TypeMap typeMap) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        public bool HasObservableMutation { get; private set; }

        protected override void VisitCall(Expr.Call expression)
        {
            if (expression.Callee is Expr.Variable { Name.Lexeme: "eval" })
                HasObservableMutation = true;

            if (expression.Arguments.Count > 0
                && expression.Callee is Expr.Get
                {
                    Object: Expr.Variable { Name.Lexeme: "Object" or "Reflect" },
                    Name.Lexeme: "assign" or "defineProperty" or "defineProperties"
                        or "set" or "deleteProperty" or "setPrototypeOf"
                }
                && IsPromiseTarget(expression.Arguments[0]))
            {
                HasObservableMutation = true;
            }
            base.VisitCall(expression);
        }

        protected override void VisitGet(Expr.Get expression)
        {
            if (IsPromisePrototype(expression))
                HasObservableMutation = true;
            base.VisitGet(expression);
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (expression.Name.Lexeme == "Promise")
                HasObservableMutation = true;
            base.VisitAssign(expression);
        }

        protected override void VisitSet(Expr.Set expression)
        {
            if (IsPromiseTarget(expression.Object))
                HasObservableMutation = true;
            base.VisitSet(expression);
        }

        protected override void VisitSetIndex(Expr.SetIndex expression)
        {
            if (IsPromiseTarget(expression.Object))
                HasObservableMutation = true;
            base.VisitSetIndex(expression);
        }

        protected override void VisitCompoundSet(Expr.CompoundSet expression)
        {
            if (IsPromiseTarget(expression.Object))
                HasObservableMutation = true;
            base.VisitCompoundSet(expression);
        }

        protected override void VisitCompoundSetIndex(Expr.CompoundSetIndex expression)
        {
            if (IsPromiseTarget(expression.Object))
                HasObservableMutation = true;
            base.VisitCompoundSetIndex(expression);
        }

        protected override void VisitLogicalSet(Expr.LogicalSet expression)
        {
            if (IsPromiseTarget(expression.Object))
                HasObservableMutation = true;
            base.VisitLogicalSet(expression);
        }

        protected override void VisitLogicalSetIndex(Expr.LogicalSetIndex expression)
        {
            if (IsPromiseTarget(expression.Object))
                HasObservableMutation = true;
            base.VisitLogicalSetIndex(expression);
        }

        protected override void VisitDelete(Expr.Delete expression)
        {
            Expr operand = ExpressionUnwrapper.Unwrap(expression.Operand);
            if (operand is Expr.Get property && IsPromiseTarget(property.Object)
                || operand is Expr.GetIndex index && IsPromiseTarget(index.Object))
            {
                HasObservableMutation = true;
            }
            base.VisitDelete(expression);
        }

        private bool IsPromiseTarget(Expr expression)
        {
            expression = ExpressionUnwrapper.Unwrap(expression);
            return expression is Expr.Variable { Name.Lexeme: "Promise" }
                || IsPromisePrototype(expression)
                || _typeMap.Get(expression) is TypeInfo.Promise;
        }

        private static bool IsPromisePrototype(Expr expression) => ExpressionUnwrapper.Unwrap(expression) is Expr.Get
        {
            Optional: false,
            Object: Expr.Variable { Name.Lexeme: "Promise" },
            Name.Lexeme: "prototype"
        };
    }
}
