using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Proves the narrow end-to-end primitive Promise.all shape used by the
/// unboxed result fast path. Both the promise array and the fulfilled-value
/// array are function-local, single-use bindings: the former is populated
/// only by intrinsic Promise.resolve(number), and the latter is observed only
/// through numeric index reads and length.
/// </summary>
internal static class StablePrimitivePromiseAllAnalyzer
{
    public static void Analyze(
        List<Stmt> program,
        TypeMap? typeMap,
        ClosureAnalyzer? closures)
    {
        if (typeMap is null)
            return;

        var mutations = new PromiseMutationVisitor(typeMap);
        foreach (var statement in program)
            mutations.Visit(statement);
        if (mutations.HasObservableMutation)
            return;

        var visitor = new BindingVisitor(typeMap);
        foreach (var statement in program)
            visitor.Visit(statement);

        foreach (var (resultKey, candidate) in visitor.Results)
        {
            var inputKey = candidate.InputKey;
            if (visitor.Disqualified.Contains(resultKey)
                || visitor.Disqualified.Contains(inputKey)
                || visitor.DeclarationCounts.GetValueOrDefault(resultKey) != 1
                || visitor.DeclarationCounts.GetValueOrDefault(inputKey) != 1
                || visitor.TerminalCounts.GetValueOrDefault(inputKey) != 1
                || visitor.InputSeeds.GetValueOrDefault(inputKey, []).Any(seed =>
                    !visitor.IsProvablyPrimitiveNumber(seed, inputKey.Scope))
                || closures?.IsVariableCaptured(resultKey.Name) == true
                || closures?.IsVariableCaptured(inputKey.Name) == true)
            {
                continue;
            }

            typeMap.MarkStablePrimitivePromiseAllIterable(candidate.Iterable);
            foreach (var use in visitor.ResultUses.GetValueOrDefault(resultKey, []))
                typeMap.MarkStablePrimitivePromiseAllResultUse(use);
        }
    }

    private static bool IsNumber(TypeInfo? type) => type is
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral;

    private static bool TryGetIntrinsicNumberResolve(
        Expr expression,
        TypeMap typeMap,
        out Expr value)
    {
        value = null!;
        expression = Unwrap(expression);
        if (expression is not Expr.Call
        {
            Optional: false,
            Callee: Expr.Get
            {
                Optional: false,
                Object: Expr.Variable { Name.Lexeme: "Promise" },
                Name.Lexeme: "resolve"
            },
            Arguments: [var resolvedValue]
        } || !IsNumber(typeMap.Get(resolvedValue)))
        {
            return false;
        }

        value = resolvedValue;
        return true;
    }

    private static bool TryGetIntrinsicAll(
        Expr? expression,
        out Expr.Variable iterable)
    {
        iterable = null!;
        if (expression is null)
            return false;

        expression = Unwrap(expression);
        if (expression is not Expr.Await awaitExpression)
            return false;

        Expr awaited = Unwrap(awaitExpression.Expression);
        if (awaited is not Expr.Call
            {
                Optional: false,
                Callee: Expr.Get
                {
                    Optional: false,
                    Object: Expr.Variable { Name.Lexeme: "Promise" },
                    Name.Lexeme: "all"
                },
                Arguments: [Expr.Variable source]
            })
        {
            return false;
        }

        iterable = source;
        return true;
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

    private sealed class BindingVisitor(TypeMap typeMap) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private int _scope;
        private int _nextScope;
        private readonly Stack<int> _enclosingScopes = [];

        public Dictionary<(int Scope, string Name), int> DeclarationCounts { get; } = [];
        public Dictionary<(int Scope, string Name), int> TerminalCounts { get; } = [];
        public HashSet<(int Scope, string Name)> Inputs { get; } = [];
        public Dictionary<(int Scope, string Name), ResultCandidate> Results { get; } = [];
        public Dictionary<(int Scope, string Name), List<Expr.Variable>> ResultUses { get; } = [];
        public Dictionary<(int Scope, string Name), List<Expr>> InputSeeds { get; } = [];
        public HashSet<(int Scope, string Name)> StableNumericLocals { get; } = [];
        public Dictionary<(int Scope, string Name), int> StableNumericDeclarationCounts { get; } = [];
        public HashSet<(int Scope, string Name)> MutatedNumericLocals { get; } = [];
        public HashSet<(int Scope, string Name)> PotentiallyCapturedLocals { get; } = [];
        public HashSet<(int Scope, string Name)> Disqualified { get; } = [];

        protected override void VisitFunction(Stmt.Function statement) =>
            InScope(() => base.VisitFunction(statement));

        protected override void VisitArrowFunction(Expr.ArrowFunction expression) =>
            InScope(() => base.VisitArrowFunction(expression));

        private void InScope(Action visit)
        {
            int saved = _scope;
            if (saved != 0)
                _enclosingScopes.Push(saved);
            _scope = ++_nextScope;
            try
            {
                visit();
            }
            finally
            {
                _scope = saved;
                if (saved != 0)
                    _enclosingScopes.Pop();
            }
        }

        protected override void VisitVar(Stmt.Var statement) =>
            HandleDeclaration(statement.Name, statement.TypeAnnotation, statement.Initializer);

        protected override void VisitConst(Stmt.Const statement) =>
            HandleDeclaration(statement.Name, statement.TypeAnnotation, statement.Initializer);

        private void HandleDeclaration(Token name, string? annotation, Expr? initializer)
        {
            var key = (_scope, name.Lexeme);
            DeclarationCounts[key] = DeclarationCounts.GetValueOrDefault(key) + 1;

            if (annotation == "number"
                && initializer is Expr.Literal { Value: double })
            {
                StableNumericLocals.Add(key);
                StableNumericDeclarationCounts[key] =
                    StableNumericDeclarationCounts.GetValueOrDefault(key) + 1;
            }

            if (_scope != 0
                && annotation?.Replace(" ", "", StringComparison.Ordinal) == "Promise<number>[]"
                && initializer is Expr.ArrayLiteral { Elements.Count: 0 })
            {
                Inputs.Add(key);
            }

            if (_scope != 0
                && annotation?.Replace(" ", "", StringComparison.Ordinal) == "number[]"
            && TryGetIntrinsicAll(initializer, out var iterable))
            {
                var inputKey = (_scope, iterable.Name.Lexeme);
                Results[key] = new ResultCandidate(inputKey, iterable);
                TerminalCounts[inputKey] = TerminalCounts.GetValueOrDefault(inputKey) + 1;
                return;
            }

            if (initializer is not null)
                Visit(initializer);
        }

        protected override void VisitCall(Expr.Call expression)
        {
            if (expression.Callee is Expr.Get
                {
                    Optional: false,
                    Object: Expr.Variable receiver,
                    Name.Lexeme: "push"
                })
            {
                var key = (_scope, receiver.Name.Lexeme);
                if (Inputs.Contains(key)
                    && expression.Arguments is [var argument]
                    && TryGetIntrinsicNumberResolve(argument, _typeMap, out var value))
                {
                    if (!InputSeeds.TryGetValue(key, out var seeds))
                        InputSeeds[key] = seeds = [];
                    seeds.Add(value);
                    Visit(argument);
                    return;
                }
            }

            base.VisitCall(expression);
        }

        protected override void VisitGet(Expr.Get expression)
        {
            if (!expression.Optional
                && expression.Name.Lexeme == "length"
                && expression.Object is Expr.Variable receiver
                && Results.ContainsKey((_scope, receiver.Name.Lexeme)))
            {
                RecordResultUse(receiver);
                return;
            }

            base.VisitGet(expression);
        }

        protected override void VisitGetIndex(Expr.GetIndex expression)
        {
            if (!expression.Optional
                && expression.Object is Expr.Variable receiver
                && Results.ContainsKey((_scope, receiver.Name.Lexeme))
                && IsNumber(_typeMap.Get(expression.Index)))
            {
                RecordResultUse(receiver);
                Visit(expression.Index);
                return;
            }

            base.VisitGetIndex(expression);
        }

        private void RecordResultUse(Expr.Variable variable)
        {
            var key = (_scope, variable.Name.Lexeme);
            if (!ResultUses.TryGetValue(key, out var uses))
                ResultUses[key] = uses = [];
            uses.Add(variable);
        }

        protected override void VisitVariable(Expr.Variable expression)
        {
            MarkPotentialCapture(expression.Name.Lexeme);
            var key = (_scope, expression.Name.Lexeme);
            if (Inputs.Contains(key) || Results.ContainsKey(key))
                Disqualified.Add(key);
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            MarkPotentialCapture(expression.Name.Lexeme);
            MutatedNumericLocals.Add((_scope, expression.Name.Lexeme));
            Disqualified.Add((_scope, expression.Name.Lexeme));
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            MarkPotentialCapture(expression.Name.Lexeme);
            MutatedNumericLocals.Add((_scope, expression.Name.Lexeme));
            Disqualified.Add((_scope, expression.Name.Lexeme));
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            MarkPotentialCapture(expression.Name.Lexeme);
            MutatedNumericLocals.Add((_scope, expression.Name.Lexeme));
            Disqualified.Add((_scope, expression.Name.Lexeme));
            base.VisitLogicalAssign(expression);
        }

        public bool IsProvablyPrimitiveNumber(
            Expr expression,
            int scope)
        {
            expression = Unwrap(expression);
            if (expression is Expr.Literal { Value: double })
                return true;

            if (expression is not Expr.Variable variable)
                return false;

            var key = (scope, variable.Name.Lexeme);
            return StableNumericLocals.Contains(key)
                && !MutatedNumericLocals.Contains(key)
                && StableNumericDeclarationCounts.GetValueOrDefault(key)
                    == DeclarationCounts.GetValueOrDefault(key)
                && !PotentiallyCapturedLocals.Contains(key);
        }

        private void MarkPotentialCapture(string name)
        {
            foreach (int scope in _enclosingScopes)
                PotentiallyCapturedLocals.Add((scope, name));
        }

        public sealed record ResultCandidate(
            (int Scope, string Name) InputKey,
            Expr.Variable Iterable);
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
            Expr operand = Unwrap(expression.Operand);
            if (operand is Expr.Get property && IsPromiseTarget(property.Object)
                || operand is Expr.GetIndex index && IsPromiseTarget(index.Object))
            {
                HasObservableMutation = true;
            }
            base.VisitDelete(expression);
        }

        private bool IsPromiseTarget(Expr expression)
        {
            expression = Unwrap(expression);
            return expression is Expr.Variable { Name.Lexeme: "Promise" }
                || IsPromisePrototype(expression)
                || _typeMap.Get(expression) is TypeInfo.Promise;
        }

        private static bool IsPromisePrototype(Expr expression) => Unwrap(expression) is Expr.Get
        {
            Optional: false,
            Object: Expr.Variable { Name.Lexeme: "Promise" },
            Name.Lexeme: "prototype"
        };
    }
}
