using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Proves the narrow ordinary-object iterator shape used by the direct for-of path.
/// The binding may only feed for-of, the iterator method must return its receiver,
/// and every next return must be the same compact numeric result record.
/// </summary>
internal static class StableCustomIteratorAnalyzer
{
    public static void Analyze(
        List<Stmt> program,
        TypeMap? typeMap,
        ClosureAnalyzer? closures,
        RuntimeFeatureSet? features)
    {
        if (typeMap is null || features is null ||
            features.UsesDynamicPropertyDescriptors ||
            features.UsesClassPrototypeMutation)
            return;

        var visitor = new CandidateVisitor(typeMap, features);
        foreach (var statement in program)
            visitor.Visit(statement);
        if (visitor.ContainsDirectEval)
            return;

        foreach (var (key, candidate) in visitor.Candidates)
        {
            if (visitor.Disqualified.Contains(key) ||
                visitor.DeclarationCounts.GetValueOrDefault(key) != 1 ||
                closures?.IsVariableCaptured(key.Name) == true ||
                !visitor.Loops.TryGetValue(key, out var loops))
                continue;

            foreach (var loop in loops)
            {
                if (!loop.IsAsync && loop.Variable.Lexeme != key.Name)
                {
                    features.CompactObjectRecordStableIteratorShapes.Add(
                        candidate.Info.ResultFingerprint);
                    // The pre-runtime pass has no closure information and is
                    // intentionally feature-only. Persist eligibility hints only
                    // after the closure pass re-proves non-capture/non-escape.
                    if (closures is not null)
                    {
                        typeMap.MarkStableCustomIterator(loop, candidate.Info);
                        MarkStableNumericAccumulator(loop, candidate.Owner, typeMap);
                    }
                }
            }

        }
    }

    private static void MarkStableNumericAccumulator(
        Stmt.ForOf loop, object? owner, TypeMap typeMap)
    {
        if (owner is not Stmt.Function { IsAsync: false, Body: { } body } ||
            loop.Body is not Stmt.Expression
            {
                Expr: Expr.Assign assignment and
                {
                    Name: var accumulatorName,
                    Value: Expr.Binary
                    {
                        Operator.Type: TokenType.PLUS,
                        Left: Expr.Variable left,
                        Right: Expr.Variable right
                    }
                }
            } ||
            left.Name.Lexeme != accumulatorName.Lexeme ||
            right.Name.Lexeme != loop.Variable.Lexeme)
            return;

        int loopIndex = body.FindIndex(statement => ReferenceEquals(statement, loop));
        if (loopIndex < 0)
            return;

        var writes = new StableNumericFunctionCaptureAnalyzer.NumericWriteVisitor(
            accumulatorName.Lexeme, typeMap, assignment);
        foreach (var statement in body)
            writes.Visit(statement);
        if (!writes.Valid)
            return;

        for (int index = 0; index < loopIndex; index++)
        {
            Token? declarationName = body[index] switch
            {
                Stmt.Var declaration when declaration.Name.Lexeme == accumulatorName.Lexeme &&
                    declaration.TypeAnnotation == "number" &&
                    declaration.Initializer is not null &&
                    StableNumericFunctionCaptureAnalyzer.IsNumber(typeMap.Get(declaration.Initializer)) => declaration.Name,
                Stmt.Const declaration when declaration.Name.Lexeme == accumulatorName.Lexeme &&
                    declaration.TypeAnnotation == "number" &&
                    StableNumericFunctionCaptureAnalyzer.IsNumber(typeMap.Get(declaration.Initializer)) => declaration.Name,
                _ => null
            };
            if (declarationName is not null)
            {
                typeMap.MarkStableCustomIteratorNumericAccumulator(declarationName);
                return;
            }
        }
    }

    private sealed class CandidateVisitor(
        TypeMap typeMap, RuntimeFeatureSet features) : AstVisitorBase
    {
        private int _scope;
        private int _nextScope;

        public Dictionary<(int Scope, string Name), Candidate> Candidates { get; } = [];
        public HashSet<(int Scope, string Name)> Disqualified { get; } = [];
        public Dictionary<(int Scope, string Name), int> DeclarationCounts { get; } = [];
        public Dictionary<(int Scope, string Name), List<Stmt.ForOf>> Loops { get; } = [];
        public bool ContainsDirectEval { get; private set; }

        private object? _currentCallable;

        protected override void VisitFunction(Stmt.Function statement) =>
            InCallableScope(statement, () => base.VisitFunction(statement));

        protected override void VisitArrowFunction(Expr.ArrowFunction expression) =>
            InCallableScope(expression, () => base.VisitArrowFunction(expression));

        private void InCallableScope(object callable, Action visit)
        {
            int saved = _scope;
            object? savedCallable = _currentCallable;
            _scope = ++_nextScope;
            _currentCallable = callable;
            visit();
            _scope = saved;
            _currentCallable = savedCallable;
        }

        protected override void VisitVar(Stmt.Var statement) =>
            HandleDeclaration(statement.Name, statement.Initializer);

        protected override void VisitConst(Stmt.Const statement) =>
            HandleDeclaration(statement.Name, statement.Initializer);

        private void HandleDeclaration(Token name, Expr? initializer)
        {
            var key = (_scope, name.Lexeme);
            DeclarationCounts[key] = DeclarationCounts.GetValueOrDefault(key) + 1;
            if (initializer is Expr.ObjectLiteral literal &&
                TryAnalyzeLiteral(literal, typeMap, features, out var info))
                Candidates[key] = new Candidate(info, _currentCallable);
            if (initializer is not null)
                Visit(initializer);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            if (statement.Iterable is Expr.Variable receiver)
            {
                var key = (_scope, receiver.Name.Lexeme);
                if (!Loops.TryGetValue(key, out var loops))
                    Loops[key] = loops = [];
                loops.Add(statement);
                Visit(statement.Body);
                return;
            }
            base.VisitForOf(statement);
        }

        protected override void VisitCall(Expr.Call expression)
        {
            if (!expression.Optional &&
                expression.Callee is Expr.Variable { Name.Lexeme: "eval" })
                ContainsDirectEval = true;
            base.VisitCall(expression);
        }

        protected override void VisitVariable(Expr.Variable expression) =>
            Disqualified.Add((_scope, expression.Name.Lexeme));

        protected override void VisitAssign(Expr.Assign expression)
        {
            Disqualified.Add((_scope, expression.Name.Lexeme));
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            Disqualified.Add((_scope, expression.Name.Lexeme));
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            Disqualified.Add((_scope, expression.Name.Lexeme));
            base.VisitLogicalAssign(expression);
        }
    }

    private sealed record Candidate(StableCustomIteratorInfo Info, object? Owner);

    private static bool TryAnalyzeLiteral(
        Expr.ObjectLiteral literal,
        TypeMap typeMap,
        RuntimeFeatureSet features,
        out StableCustomIteratorInfo info)
    {
        info = null!;
        if (literal.Properties.Any(property => property.IsSpread ||
            property.Kind is Expr.ObjectPropertyKind.Getter or Expr.ObjectPropertyKind.Setter))
            return false;

        Expr.ArrowFunction? iterator = null;
        Expr.ArrowFunction? next = null;
        foreach (var property in literal.Properties)
        {
            if (property.Value is not Expr.ArrowFunction method)
                continue;
            if (property.Key is Expr.IdentifierKey { Name.Lexeme: "next" })
                next = method;
            else if (property.Key is Expr.ComputedKey { Expression: var key } &&
                IsSymbolIterator(key))
                iterator = method;
        }

        if (iterator is null || next is null ||
            iterator.IsAsync || iterator.IsGenerator || iterator.Parameters.Count != 0 ||
            !iterator.HasOwnThis ||
            next.IsAsync || next.IsGenerator || next.Parameters.Count != 0 ||
            !ReturnsOnlyThis(iterator) ||
            !TryGetResultShape(next, typeMap, out var fingerprint,
                out int valueIndex, out int doneIndex) ||
            !features.CanAssumeCompactObjectRecordIsUnmaterialized(fingerprint))
            return false;

        info = new StableCustomIteratorInfo(
            iterator, next, fingerprint, valueIndex, doneIndex);
        return true;
    }

    private static bool IsSymbolIterator(Expr expression) => expression switch
    {
        Expr.Get
        {
            Object: Expr.Variable { Name.Lexeme: "Symbol" },
            Name.Lexeme: "iterator"
        } => true,
        Expr.Grouping grouping => IsSymbolIterator(grouping.Expression),
        Expr.TypeAssertion assertion => IsSymbolIterator(assertion.Expression),
        Expr.Satisfies satisfies => IsSymbolIterator(satisfies.Expression),
        Expr.NonNullAssertion nonNull => IsSymbolIterator(nonNull.Expression),
        _ => false
    };

    private static bool ReturnsOnlyThis(Expr.ArrowFunction method)
    {
        if (method.ExpressionBody is Expr.This)
            return true;
        return method.BlockBody is [Stmt.Return { Value: Expr.This }];
    }

    private static bool TryGetResultShape(
        Expr.ArrowFunction next,
        TypeMap typeMap,
        out string fingerprint,
        out int valueIndex,
        out int doneIndex)
    {
        var returns = new ResultReturnVisitor();
        if (next.ExpressionBody is not null)
            returns.Add(next.ExpressionBody);
        else if (next.BlockBody is not null)
        {
            // A typed value-result ABI has no `undefined` representation. Require
            // an unconditional trailing result return so falling off the method
            // cannot change from a protocol TypeError into a default struct value.
            if (next.BlockBody.Count == 0 ||
                next.BlockBody[^1] is not Stmt.Return { Value: Expr.ObjectLiteral })
            {
                fingerprint = "";
                valueIndex = -1;
                doneIndex = -1;
                return false;
            }
            foreach (var statement in next.BlockBody)
                returns.Visit(statement);
        }

        fingerprint = "";
        valueIndex = -1;
        doneIndex = -1;
        if (!returns.Valid || returns.Values.Count == 0)
            return false;

        foreach (var value in returns.Values)
        {
            if (value is not Expr.ObjectLiteral literal ||
                !JsonSerializationShapeAnalyzer.TryAnalyze(
                    typeMap.Get(literal), out var analyzed) ||
                analyzed is not JsonSerializationShape.Record shape ||
                shape.Fields.Count != 2)
                return false;

            string current = JsonSerializationShapeAnalyzer.Fingerprint(shape);
            if (fingerprint.Length == 0)
            {
                fingerprint = current;
                for (int index = 0; index < shape.Fields.Count; index++)
                {
                    if (shape.Fields[index].Key == "value" &&
                        shape.Fields[index].Value is JsonSerializationShape.Number)
                        valueIndex = index;
                    if (shape.Fields[index].Key == "done" &&
                        shape.Fields[index].Value is JsonSerializationShape.Boolean)
                        doneIndex = index;
                }
            }
            else if (current != fingerprint)
            {
                return false;
            }
        }

        // The specialized return ABI evaluates value then done. Restrict the proof
        // to that source order so object-literal evaluation order is unchanged.
        return valueIndex == 0 && doneIndex == 1;
    }

    private sealed class ResultReturnVisitor : AstVisitorBase
    {
        public List<Expr> Values { get; } = [];
        public bool Valid { get; private set; } = true;

        public void Add(Expr value) => Values.Add(value);

        protected override void VisitReturn(Stmt.Return statement)
        {
            if (statement.Value is null)
                Valid = false;
            else
                Values.Add(statement.Value);
        }

        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
    }
}
