using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Finds the deliberately narrow loop-capture shape whose display-class snapshot
/// may be stored as an unboxed <c>double</c>. The binding must be an explicitly
/// numeric, definitely initialized <c>for (let ...)</c> counter and the capturing
/// arrow must already belong to the proven non-escaping direct-call path in a fully
/// synchronous enclosing function, or be the inline callback of a proven stable
/// primitive Promise chain whose async loop counter never crosses a suspension.
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
        var promiseHandlers = new StablePromiseHandlerVisitor(typeMap);
        foreach (var statement in program)
            promiseHandlers.Visit(statement);

        var visitor = new LoopVisitor(
            typeMap, closures, directCallArrows, promiseHandlers.Arrows);
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
        HashSet<Expr.ArrowFunction> directCallArrows,
        HashSet<Expr.ArrowFunction> stablePromiseHandlers) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private readonly ClosureAnalyzer _closures = closures;
        private readonly HashSet<Expr.ArrowFunction> _directCallArrows = directCallArrows;
        private readonly HashSet<Expr.ArrowFunction> _stablePromiseHandlers = stablePromiseHandlers;
        private readonly Stack<object> _functionStack = new();
        private int _functionDepth;
        private int _suspendingFunctionDepth;

        protected override void VisitFunction(Stmt.Function statement)
        {
            _functionStack.Push(statement);
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
                _functionStack.Pop();
            }
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            _functionStack.Push(expression);
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
                _functionStack.Pop();
            }
        }

        protected override void VisitFor(Stmt.For statement)
        {
            if (_functionDepth > 0
                && TryGetStableNumericBinding(statement, _typeMap, out var declaration))
            {
                var collector = new CandidateCollector(
                    declaration.Name.Lexeme,
                    _typeMap,
                    _closures,
                    _directCallArrows,
                    _stablePromiseHandlers);
                collector.Visit(statement.Body);

                if (_suspendingFunctionDepth == 0)
                {
                    collector.MarkSynchronousCaptures();
                }
                else if (_functionStack.TryPeek(out var enclosing)
                    && enclosing is Stmt.Function
                    {
                        IsAsync: true,
                        IsGenerator: false
                    } function
                    && IsStableAsyncPromiseLoop(statement, declaration.Name.Lexeme)
                    && !collector.BindingWritten
                    && collector.StablePromiseCaptures.Count > 0)
                {
                    _typeMap.MarkStableNumericStateMachineLocal(declaration);
                    collector.MarkStablePromiseCaptures();
                    MarkStableBoundParameter(statement, function, declaration.Name.Lexeme);
                }
                else if (_functionStack.TryPeek(out enclosing)
                    && enclosing is Stmt.Function
                    {
                        IsAsync: false,
                        IsGenerator: true
                    } generator
                    && IsStableNumericGeneratorLoop(statement, declaration.Name.Lexeme))
                {
                    // A canonical numeric range generator necessarily carries its loop
                    // counter and bound across every yield.  Keeping those two proven-
                    // numeric bindings in double fields removes the otherwise mandatory
                    // box/re-coerce cycle on every MoveNext re-entry.  The generator's
                    // public object ABI remains unchanged; only its private suspension
                    // storage is specialized.
                    _typeMap.MarkStableNumericStateMachineLocal(declaration);
                    MarkStableBoundParameter(statement, generator, declaration.Name.Lexeme);
                }
            }

            base.VisitFor(statement);
        }

        private void MarkStableBoundParameter(
            Stmt.For loop,
            Stmt.Function function,
            string counterName)
        {
            if (loop.Condition is not Expr.Binary
                {
                    Operator.Type: TokenType.LESS,
                    Left: Expr.Variable left,
                    Right: Expr.Variable right
                }
                || left.Name.Lexeme != counterName)
            {
                return;
            }

            string boundName = right.Name.Lexeme;

            Stmt.Parameter? parameter = null;
            foreach (var candidate in function.Parameters)
            {
                if (candidate.Name.Lexeme != boundName)
                    continue;
                if (parameter is not null)
                    return;
                parameter = candidate;
            }
            if (parameter is not
                {
                    Type: "number",
                    IsOptional: false,
                    IsRest: false,
                    DefaultValue: null
                }
                || _typeMap.IsUndefinedReachableNumericParam(parameter)
                || _closures.GetCapturedLocals(function).Contains(boundName))
            {
                return;
            }

            var writes = new BindingWriteVisitor(boundName);
            if (function.Body is not null)
                foreach (var statement in function.Body)
                    writes.Visit(statement);
            if (!writes.Written)
                _typeMap.MarkStableNumericStateMachineParameter(parameter);
        }

        private static bool IsStableAsyncPromiseLoop(Stmt.For loop, string name)
        {
            if (loop.Condition is not Expr.Binary
                {
                    Operator.Type: TokenType.LESS,
                    Left: Expr.Variable conditionCounter
                }
                || conditionCounter.Name.Lexeme != name
                || loop.Increment is not Expr.PostfixIncrement
                {
                    Operand: Expr.Variable incrementCounter,
                    Operator.Type: TokenType.PLUS_PLUS
                }
                || incrementCounter.Name.Lexeme != name)
            {
                return false;
            }

            var safety = new AsyncLoopSafetyVisitor(name);
            if (loop.Condition is not null)
                safety.Visit(loop.Condition);
            safety.Visit(loop.Body);
            return safety.Safe;
        }

        private static bool IsStableNumericGeneratorLoop(Stmt.For loop, string name)
        {
            if (loop.Condition is not Expr.Binary
                {
                    Operator.Type: TokenType.LESS,
                    Left: Expr.Variable conditionCounter,
                    Right: Expr.Variable
                }
                || conditionCounter.Name.Lexeme != name
                || loop.Increment is not Expr.PostfixIncrement
                {
                    Operand: Expr.Variable incrementCounter,
                    Operator.Type: TokenType.PLUS_PLUS
                }
                || incrementCounter.Name.Lexeme != name)
            {
                return false;
            }

            // Start deliberately with the exact range shape that exposed the
            // regression.  A wider generator body can mutate, capture, delegate,
            // or otherwise expose the counter across suspension; those shapes keep
            // object storage until they receive their own proof.
            Stmt body = loop.Body;
            if (body is Stmt.Block { Statements: [Stmt.Expression expression] })
                body = new Stmt.Expression(expression.Expr);

            return body is Stmt.Expression
            {
                Expr: Expr.Yield
                {
                    IsDelegating: false,
                    Value: Expr.Variable yielded
                }
            }
            && yielded.Name.Lexeme == name;
        }
    }

    private sealed class CandidateCollector(
        string bindingName,
        TypeMap typeMap,
        ClosureAnalyzer closures,
        HashSet<Expr.ArrowFunction> directCallArrows,
        HashSet<Expr.ArrowFunction> stablePromiseHandlers) : AstVisitorBase
    {
        private readonly string _bindingName = bindingName;
        private readonly TypeMap _typeMap = typeMap;
        private readonly ClosureAnalyzer _closures = closures;
        private readonly HashSet<Expr.ArrowFunction> _directCallArrows = directCallArrows;
        private readonly HashSet<Expr.ArrowFunction> _stablePromiseHandlers = stablePromiseHandlers;
        private readonly HashSet<Expr.ArrowFunction> _directCaptures =
            new(ReferenceEqualityComparer.Instance);
        public HashSet<Expr.ArrowFunction> StablePromiseCaptures { get; } =
            new(ReferenceEqualityComparer.Instance);
        public bool BindingWritten => _bindingWritten;

        protected override void VisitArrowFunction(Expr.ArrowFunction arrow)
        {
            if (_closures.GetCaptures(arrow).Contains(_bindingName)
                && !_closures.GetClosureCellFields(arrow).Contains(_bindingName))
            {
                if (_directCallArrows.Contains(arrow))
                    _directCaptures.Add(arrow);
                if (_stablePromiseHandlers.Contains(arrow))
                    StablePromiseCaptures.Add(arrow);
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

        protected override void VisitSwitch(Stmt.Switch statement)
        {
            Visit(statement.Subject);

            // A switch owns one lexical scope shared by every case. A declaration in
            // any arm therefore shadows the loop binding throughout the case block.
            if (statement.Cases.Any(@case => DeclaresBinding(@case.Body))
                || statement.DefaultBody is not null
                && DeclaresBinding(statement.DefaultBody))
            {
                return;
            }

            foreach (var @case in statement.Cases)
            {
                Visit(@case.Value);
                foreach (var child in @case.Body)
                    Visit(child);
            }
            if (statement.DefaultBody is not null)
                foreach (var child in statement.DefaultBody)
                    Visit(child);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            Visit(statement.Iterable);
            if (statement.Variable.Lexeme == _bindingName)
            {
                // ForOf does not retain whether the target came from a declaration
                // or an existing-binding assignment. Conservatively reject both:
                // one shadows this binding and the other writes it.
                _bindingWritten = true;
                return;
            }
            Visit(statement.Body);
        }

        protected override void VisitForIn(Stmt.ForIn statement)
        {
            Visit(statement.Object);
            if (statement.Variable.Lexeme == _bindingName)
            {
                // The declaration form can still be `var`, which reuses a
                // function-scoped binding. Reject the name collision conservatively.
                _bindingWritten = true;
                return;
            }
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

        public void MarkSynchronousCaptures()
        {
            if (_bindingWritten)
                return;
            foreach (var arrow in _directCaptures)
                _typeMap.MarkStableNumericCaptureField(arrow, _bindingName);
            foreach (var arrow in StablePromiseCaptures)
                _typeMap.MarkStableNumericCaptureField(arrow, _bindingName);
        }

        public void MarkStablePromiseCaptures()
        {
            if (_bindingWritten)
                return;
            foreach (var arrow in StablePromiseCaptures)
                _typeMap.MarkStableNumericCaptureField(arrow, _bindingName);
        }

        private bool _bindingWritten;
    }

    private sealed class StablePromiseHandlerVisitor(TypeMap typeMap) : AstVisitorBase
    {
        public HashSet<Expr.ArrowFunction> Arrows { get; } =
            new(ReferenceEqualityComparer.Instance);

        protected override void VisitCall(Expr.Call expression)
        {
            if (expression.Callee is Expr.Get method
                && typeMap.IsStablePrimitivePromiseThen(method)
                && expression.Arguments is [Expr.ArrowFunction handler])
            {
                Arrows.Add(handler);
            }
            base.VisitCall(expression);
        }
    }

    private sealed class AsyncLoopSafetyVisitor(string bindingName) : AstVisitorBase
    {
        public bool Safe { get; private set; } = true;

        protected override void VisitAwait(Expr.Await expression) => Safe = false;
        protected override void VisitYield(Expr.Yield expression) => Safe = false;

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (expression.Name.Lexeme == bindingName)
                Safe = false;
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            if (expression.Name.Lexeme == bindingName)
                Safe = false;
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            if (expression.Name.Lexeme == bindingName)
                Safe = false;
            base.VisitLogicalAssign(expression);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            if (expression.Operand is Expr.Variable variable
                && variable.Name.Lexeme == bindingName)
                Safe = false;
            base.VisitPrefixIncrement(expression);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            if (expression.Operand is Expr.Variable variable
                && variable.Name.Lexeme == bindingName)
                Safe = false;
            base.VisitPostfixIncrement(expression);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            Visit(statement.Iterable);
            if (statement.Variable.Lexeme == bindingName)
            {
                Safe = false;
                return;
            }
            Visit(statement.Body);
        }

        protected override void VisitForIn(Stmt.ForIn statement)
        {
            Visit(statement.Object);
            if (statement.Variable.Lexeme == bindingName)
            {
                Safe = false;
                return;
            }
            Visit(statement.Body);
        }
    }

    private sealed class BindingWriteVisitor(string bindingName) : AstVisitorBase
    {
        public bool Written { get; private set; }

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (expression.Name.Lexeme == bindingName)
                Written = true;
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            if (expression.Name.Lexeme == bindingName)
                Written = true;
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            if (expression.Name.Lexeme == bindingName)
                Written = true;
            base.VisitLogicalAssign(expression);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            if (expression.Operand is Expr.Variable variable
                && variable.Name.Lexeme == bindingName)
                Written = true;
            base.VisitPrefixIncrement(expression);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            if (expression.Operand is Expr.Variable variable
                && variable.Name.Lexeme == bindingName)
                Written = true;
            base.VisitPostfixIncrement(expression);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            Visit(statement.Iterable);
            if (statement.Variable.Lexeme == bindingName)
            {
                // The AST cannot distinguish `for (name of ...)` from a declaration,
                // so both forms conservatively disqualify a typed parameter field.
                Written = true;
                return;
            }
            Visit(statement.Body);
        }

        protected override void VisitForIn(Stmt.ForIn statement)
        {
            Visit(statement.Object);
            if (statement.Variable.Lexeme == bindingName)
            {
                Written = true;
                return;
            }
            Visit(statement.Body);
        }
    }

    private sealed class DirectEvalVisitor : AstVisitorBase
    {
        public bool ContainsDirectEval { get; private set; }

        protected override void VisitCall(Expr.Call expression)
        {
            if (!expression.Optional
                && Unwrap(expression.Callee) is Expr.Variable { Name.Lexeme: "eval" })
            {
                ContainsDirectEval = true;
            }
            base.VisitCall(expression);
        }
    }
}
