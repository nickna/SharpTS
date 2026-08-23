using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Proves the deliberately narrow Promise.then shape used by the primitive-result
/// continuation fast path. A qualifying binding starts at intrinsic Promise.resolve,
/// never escapes or aliases, and is only advanced by assigning the result of its own
/// eligible <c>then</c> call. Any observable Promise/prototype mutation or direct eval
/// disables the optimization for the whole compilation.
/// </summary>
internal static class StablePrimitivePromiseThenAnalyzer
{
    public static void Analyze(
        List<Stmt> program,
        TypeMap? typeMap,
        ClosureAnalyzer? closures)
    {
        if (typeMap is null)
            return;

        var mutationVisitor = new PromiseMutationVisitor(typeMap);
        foreach (var statement in program)
            mutationVisitor.Visit(statement);
        if (mutationVisitor.HasObservableMutation)
            return;

        var visitor = new BindingVisitor(typeMap);
        foreach (var statement in program)
            visitor.Visit(statement);

        foreach (var (key, calls) in visitor.Calls)
        {
            if (!visitor.Candidates.Contains(key)
                || visitor.Disqualified.Contains(key)
                || visitor.DeclarationCounts.GetValueOrDefault(key) != 1
                || visitor.TerminalCounts.GetValueOrDefault(key) != 1
                || closures?.IsVariableCaptured(key.Name) == true)
            {
                continue;
            }

            foreach (var method in calls)
                typeMap.MarkStablePrimitivePromiseThen(method);
        }

        foreach (var method in visitor.DirectSeedCalls)
            typeMap.MarkStablePrimitivePromiseThen(method);
    }

    private static bool TryGetEligibleThen(
        Expr expression,
        TypeMap typeMap,
        out Expr.Call call,
        out Expr.Get method)
    {
        call = null!;
        method = null!;
        if (expression is not Expr.Call
            {
                Optional: false,
                Callee: Expr.Get
                {
                    Optional: false,
                    Name.Lexeme: "then"
                } get,
                Arguments: [Expr.ArrowFunction
                {
                    IsAsync: false,
                    IsGenerator: false,
                    HasOwnThis: false,
                    Parameters: [
                        {
                            IsRest: false,
                            IsOptional: false,
                            DefaultValue: null
                        }]
                } handler]
            } candidate
            || typeMap.Get(get.Object) is not TypeInfo.Promise receiver
            || !IsNumber(receiver.ValueType)
            || typeMap.Get(handler) is not TypeInfo.Function function
            || function.ParamTypes is not [var parameterType]
            || !IsNumber(parameterType)
            || !IsNumber(function.ReturnType))
        {
            return false;
        }

        call = candidate;
        method = get;
        return true;
    }

    private static bool IsNumber(TypeInfo type) => type is
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral;

    private static bool IsIntrinsicResolveSeed(Expr expression)
    {
        expression = Unwrap(expression);
        return expression is Expr.Call
        {
            Optional: false,
            Callee: Expr.Get
            {
                Optional: false,
                Object: Expr.Variable { Name.Lexeme: "Promise" },
                Name.Lexeme: "resolve"
            }
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

    private sealed class BindingVisitor(TypeMap typeMap) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private int _scope;
        private int _nextScope;
        private Expr.Assign? _linearAssignmentStatement;

        public HashSet<(int Scope, string Name)> Candidates { get; } = [];
        public HashSet<(int Scope, string Name)> Disqualified { get; } = [];
        public Dictionary<(int Scope, string Name), int> DeclarationCounts { get; } = [];
        public Dictionary<(int Scope, string Name), int> TerminalCounts { get; } = [];
        public Dictionary<(int Scope, string Name), List<Expr.Get>> Calls { get; } = [];
        public HashSet<Expr.Get> DirectSeedCalls { get; } = new(ReferenceEqualityComparer.Instance);
        private HashSet<(int Scope, string Name)> Terminated { get; } = [];

        protected override void VisitFunction(Stmt.Function statement) =>
            InScope(() => base.VisitFunction(statement));

        protected override void VisitArrowFunction(Expr.ArrowFunction expression) =>
            InScope(() => base.VisitArrowFunction(expression));

        private void InScope(Action visit)
        {
            int saved = _scope;
            _scope = ++_nextScope;
            visit();
            _scope = saved;
        }

        protected override void VisitVar(Stmt.Var statement) =>
            HandleDeclaration(statement.Name, statement.Initializer);

        protected override void VisitConst(Stmt.Const statement) =>
            HandleDeclaration(statement.Name, statement.Initializer);

        private void HandleDeclaration(Token name, Expr? initializer)
        {
            var key = (_scope, name.Lexeme);
            DeclarationCounts[key] = DeclarationCounts.GetValueOrDefault(key) + 1;
            // Top-level bindings can be observed through ESM exports (including
            // under an imported alias), so this deliberately remains a local-
            // binding optimization. Function-local bindings cannot become live
            // module cells unless another use below proves that they escape.
            if (_scope != 0
                && initializer is not null
                && IsIntrinsicResolveSeed(initializer))
                Candidates.Add(key);
            if (initializer is not null)
                Visit(initializer);
        }

        protected override void VisitExpression(Stmt.Expression statement)
        {
            var saved = _linearAssignmentStatement;
            _linearAssignmentStatement = Unwrap(statement.Expr) as Expr.Assign;
            try
            {
                Visit(statement.Expr);
            }
            finally
            {
                _linearAssignmentStatement = saved;
            }
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            var key = (_scope, expression.Name.Lexeme);
            // The assignment's resulting value is itself observable when it is
            // nested in another expression (for example, consume(chain = ...)).
            // Only a discarded expression statement proves that no intermediate
            // Promise identity escapes the linear binding.
            if (ReferenceEquals(expression, _linearAssignmentStatement)
                && TryGetEligibleThen(expression.Value, _typeMap, out var call, out var method)
                && method.Object is Expr.Variable receiver
                && receiver.Name.Lexeme == expression.Name.Lexeme)
            {
                if (Terminated.Contains(key))
                    Disqualified.Add(key);
                RecordCall(key, method);
                VisitEligibleArguments(call);
                return;
            }

            Disqualified.Add(key);
            base.VisitAssign(expression);
        }

        protected override void VisitCall(Expr.Call expression)
        {
            if (TryGetEligibleThen(expression, _typeMap, out var call, out var method))
            {
                if (method.Object is Expr.Variable receiver)
                {
                    // Only `chain = chain.then(handler)` is a linear append.
                    // A bare or sibling call observes a distinct intermediate
                    // Promise and therefore cannot share the fused carrier.
                    Disqualified.Add((_scope, receiver.Name.Lexeme));
                    VisitEligibleArguments(call);
                    return;
                }

                if (IsIntrinsicResolveSeed(method.Object))
                {
                    DirectSeedCalls.Add(method);
                    Visit(method.Object);
                    VisitEligibleArguments(call);
                    return;
                }
            }

            base.VisitCall(expression);
        }

        private void RecordCall((int Scope, string Name) key, Expr.Get method)
        {
            if (!Calls.TryGetValue(key, out var calls))
                Calls[key] = calls = [];
            calls.Add(method);
        }

        private void VisitEligibleArguments(Expr.Call call)
        {
            foreach (var argument in call.Arguments)
                Visit(argument);
        }

        protected override void VisitAwait(Expr.Await expression)
        {
            if (Unwrap(expression.Expression) is Expr.Variable variable)
            {
                RecordTerminal((_scope, variable.Name.Lexeme));
                return;
            }
            base.VisitAwait(expression);
        }

        protected override void VisitReturn(Stmt.Return statement)
        {
            if (statement.Value is not null
                && Unwrap(statement.Value) is Expr.Variable variable)
            {
                RecordTerminal((_scope, variable.Name.Lexeme));
                return;
            }
            base.VisitReturn(statement);
        }

        private void RecordTerminal((int Scope, string Name) key)
        {
            TerminalCounts[key] = TerminalCounts.GetValueOrDefault(key) + 1;
            Terminated.Add(key);
        }

        protected override void VisitVariable(Expr.Variable expression) =>
            Disqualified.Add((_scope, expression.Name.Lexeme));

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

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            if (expression.Operand is Expr.Variable variable)
                Disqualified.Add((_scope, variable.Name.Lexeme));
            base.VisitPrefixIncrement(expression);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            if (expression.Operand is Expr.Variable variable)
                Disqualified.Add((_scope, variable.Name.Lexeme));
            base.VisitPostfixIncrement(expression);
        }
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
