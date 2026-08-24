using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Finds the deliberately narrow loop-capture shape whose display-class snapshot
/// may be stored as an unboxed <c>double</c>. The binding must be an explicitly
/// numeric, definitely initialized <c>for (let ...)</c> counter and the capturing
/// arrow must already belong to the proven non-escaping direct-call path in a fully
/// synchronous enclosing function.
/// </summary>
/// <remarks>
/// Captures that need live binding semantics are excluded through the per-iteration
/// cell analysis. Direct eval and lexically shadowed names also opt out. This changes
/// only the representation of a fresh value-snapshot field; all general callable and
/// ESM-visible paths retain object storage.
/// </remarks>
internal static class StableNumericLoopCaptureAnalyzer
{
    public static void Analyze(
        List<Stmt> program,
        TypeMap? typeMap,
        ClosureAnalyzer? closures,
        IReadOnlyDictionary<string, Expr.ArrowFunction> directCallBindings)
    {
        if (typeMap is null || closures is null)
            return;

        var evalVisitor = new DirectEvalVisitor();
        foreach (var statement in program)
            evalVisitor.Visit(statement);
        if (evalVisitor.ContainsDirectEval)
            return;

        var directCallArrows = new HashSet<Expr.ArrowFunction>(
            directCallBindings.Values,
            ReferenceEqualityComparer.Instance);
        var visitor = new LoopVisitor(typeMap, closures, directCallArrows);
        foreach (var statement in program)
            visitor.Visit(statement);
    }

    private static bool TryGetStableNumericBinding(
        Stmt.For loop,
        TypeMap typeMap,
        out Stmt.Var declaration)
    {
        declaration = null!;
        if (loop.Initializer is not Stmt.Var
            {
                IsVar: false,
                TypeAnnotation: "number",
                Initializer: { } initializer
            } candidate
            || !IsNumericLiteral(initializer)
            || typeMap.Get(initializer) is not (TypeInfo.Primitive
                { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral)
            || typeMap.IsUndefinedReachableNumericLocal(candidate)
            || typeMap.IsUndefinedReachableNumericLocal(initializer))
        {
            return false;
        }

        declaration = candidate;
        return true;
    }

    private static bool IsNumericLiteral(Expr expression)
    {
        expression = Unwrap(expression);
        return expression is Expr.Literal { Value: double }
            or Expr.Unary
            {
                Operator.Type: TokenType.MINUS,
                Right: Expr.Literal { Value: double }
            };
    }

    private static Expr Unwrap(Expr expression)
    {
        while (true)
        {
            switch (expression)
            {
                case Expr.Grouping grouping:
                    expression = grouping.Expression;
                    continue;
                case Expr.TypeAssertion assertion:
                    expression = assertion.Expression;
                    continue;
                case Expr.Satisfies satisfies:
                    expression = satisfies.Expression;
                    continue;
                case Expr.NonNullAssertion nonNull:
                    expression = nonNull.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private sealed class LoopVisitor(
        TypeMap typeMap,
        ClosureAnalyzer closures,
        HashSet<Expr.ArrowFunction> directCallArrows) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private readonly ClosureAnalyzer _closures = closures;
        private readonly HashSet<Expr.ArrowFunction> _directCallArrows = directCallArrows;
        private int _functionDepth;
        private int _suspendingFunctionDepth;

        protected override void VisitFunction(Stmt.Function statement)
        {
            _functionDepth++;
            if (statement.IsAsync || statement.IsGenerator)
                _suspendingFunctionDepth++;
            try
            {
                base.VisitFunction(statement);
            }
            finally
            {
                if (statement.IsAsync || statement.IsGenerator)
                    _suspendingFunctionDepth--;
                _functionDepth--;
            }
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            _functionDepth++;
            if (expression.IsAsync || expression.IsGenerator)
                _suspendingFunctionDepth++;
            try
            {
                base.VisitArrowFunction(expression);
            }
            finally
            {
                if (expression.IsAsync || expression.IsGenerator)
                    _suspendingFunctionDepth--;
                _functionDepth--;
            }
        }

        protected override void VisitFor(Stmt.For statement)
        {
            if (_functionDepth > 0
                && _suspendingFunctionDepth == 0
                && TryGetStableNumericBinding(statement, _typeMap, out var declaration))
            {
                var collector = new CandidateCollector(
                    declaration.Name.Lexeme,
                    _typeMap,
                    _closures,
                    _directCallArrows);
                collector.Visit(statement.Body);
            }

            base.VisitFor(statement);
        }
    }

    private sealed class CandidateCollector(
        string bindingName,
        TypeMap typeMap,
        ClosureAnalyzer closures,
        HashSet<Expr.ArrowFunction> directCallArrows) : AstVisitorBase
    {
        private readonly string _bindingName = bindingName;
        private readonly TypeMap _typeMap = typeMap;
        private readonly ClosureAnalyzer _closures = closures;
        private readonly HashSet<Expr.ArrowFunction> _directCallArrows = directCallArrows;

        protected override void VisitArrowFunction(Expr.ArrowFunction arrow)
        {
            if (_directCallArrows.Contains(arrow)
                && _closures.GetCaptures(arrow).Contains(_bindingName)
                && !_closures.GetClosureCellFields(arrow).Contains(_bindingName))
            {
                _typeMap.MarkStableNumericCaptureField(arrow, _bindingName);
            }

            // A nested arrow has a separate creation scope and capture source.
            // Its own loops are considered by LoopVisitor's ordinary traversal.
        }

        protected override void VisitFunction(Stmt.Function statement)
        {
            // Function declarations execute in their own lexical environment.
        }

        protected override void VisitClass(Stmt.Class statement)
        {
            // Class methods and field initializers are not created as loop-body arrows.
        }

        protected override void VisitBlock(Stmt.Block statement)
        {
            if (!DeclaresBinding(statement.Statements))
                base.VisitBlock(statement);
        }

        protected override void VisitSequence(Stmt.Sequence statement)
        {
            if (!DeclaresBinding(statement.Statements))
                base.VisitSequence(statement);
        }

        protected override void VisitFor(Stmt.For statement)
        {
            if (DeclaresBinding(statement.Initializer))
                return;
            base.VisitFor(statement);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            Visit(statement.Iterable);
            if (statement.Variable.Lexeme != _bindingName)
                Visit(statement.Body);
        }

        protected override void VisitForIn(Stmt.ForIn statement)
        {
            Visit(statement.Object);
            if (statement.Variable.Lexeme != _bindingName)
                Visit(statement.Body);
        }

        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            foreach (var child in statement.TryBlock)
                Visit(child);
            if (statement.CatchBlock is not null
                && statement.CatchParam?.Lexeme != _bindingName)
            {
                foreach (var child in statement.CatchBlock)
                    Visit(child);
            }
            if (statement.FinallyBlock is not null)
            {
                foreach (var child in statement.FinallyBlock)
                    Visit(child);
            }
        }

        private bool DeclaresBinding(IEnumerable<Stmt> statements) =>
            statements.Any(DeclaresBinding);

        private bool DeclaresBinding(Stmt? statement) => statement switch
        {
            Stmt.Var variable => variable.Name.Lexeme == _bindingName,
            Stmt.Const constant => constant.Name.Lexeme == _bindingName,
            Stmt.Function function => function.Name.Lexeme == _bindingName,
            Stmt.Class @class => @class.Name.Lexeme == _bindingName,
            Stmt.Sequence sequence => DeclaresBinding(sequence.Statements),
            _ => false
        };
    }

    private sealed class DirectEvalVisitor : AstVisitorBase
    {
        public bool ContainsDirectEval { get; private set; }

        protected override void VisitCall(Expr.Call expression)
        {
            if (!expression.Optional
                && expression.Callee is Expr.Variable { Name.Lexeme: "eval" })
            {
                ContainsDirectEval = true;
            }
            base.VisitCall(expression);
        }
    }
}
