using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Proves the deliberately narrow local lifetime in which an exact
/// <c>Map&lt;number, number&gt;</c> may use <c>Dictionary&lt;double, double&gt;</c>
/// storage. Any bare receiver observation, escape, alias, iteration, dynamic
/// member access, widening call, reassignment, capture, direct eval, or
/// observable use of the intrinsic <c>Map</c> constructor disables promotion.
/// </summary>
internal static class NumericMapLocalPromotionAnalyzer
{
    public static void Analyze(List<Stmt> program, TypeMap? typeMap, ClosureAnalyzer? closures)
    {
        if (typeMap == null)
            return;

        var visitor = new Visitor(typeMap);
        foreach (var statement in program)
            visitor.Visit(statement);

        if (visitor.ContainsDirectEval || visitor.IntrinsicMapIsObservable)
            return;

        foreach (var (key, nameToken) in visitor.Candidates)
        {
            if (key.Scope == 0
                || visitor.Disqualified.Contains(key)
                || visitor.DeclarationCounts.GetValueOrDefault(key) != 1
                || closures?.IsVariableCaptured(key.Name) == true)
            {
                continue;
            }

            typeMap.MarkPromotableNumericMapLocal(nameToken);
        }
    }

    private static bool IsNumber(TypeInfo? type) =>
        type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
            or TypeInfo.NumberLiteral;

    private static bool IsExactNumericMap(TypeInfo? type) =>
        type is TypeInfo.Map map
        && IsNumber(map.KeyType)
        && IsNumber(map.ValueType);

    private sealed class Visitor(TypeMap typeMap) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private Expr.Call? _discardedCall;
        private int _scope;
        private int _nextScope;

        public Dictionary<(int Scope, string Name), Token> Candidates { get; } = [];
        public Dictionary<(int Scope, string Name), int> DeclarationCounts { get; } = [];
        public HashSet<(int Scope, string Name)> Disqualified { get; } = [];
        public bool ContainsDirectEval { get; private set; }
        public bool IntrinsicMapIsObservable { get; private set; }

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

            if (initializer is Expr.New
                {
                    Callee: Expr.Variable { Name.Lexeme: "Map" },
                    Arguments.Count: 0
                }
                && IsExactNumericMap(_typeMap.Get(initializer)))
            {
                Candidates.TryAdd(key, name);
            }

            if (initializer != null)
                Visit(initializer);
        }

        protected override void VisitNew(Expr.New expression)
        {
            // Constructing the intrinsic does not expose it. Skip only the
            // callee token; entry arguments still participate in escape proof.
            if (expression.Callee is Expr.Variable { Name.Lexeme: "Map" })
            {
                foreach (var argument in expression.Arguments)
                    Visit(argument);
                return;
            }

            base.VisitNew(expression);
        }

        protected override void VisitExpression(Stmt.Expression statement)
        {
            Expr.Call? saved = _discardedCall;
            _discardedCall = statement.Expr as Expr.Call;
            try
            {
                Visit(statement.Expr);
            }
            finally
            {
                _discardedCall = saved;
            }
        }

        protected override void VisitCall(Expr.Call expression)
        {
            if (!expression.Optional
                && expression.Callee is Expr.Variable { Name.Lexeme: "eval" })
            {
                ContainsDirectEval = true;
            }

            if (!expression.Optional
                && expression.Callee is Expr.Get
                {
                    Object: Expr.Variable receiver,
                    Optional: false
                } method
                && IsExactNumericMap(_typeMap.Get(receiver))
                && IsPermittedCall(expression, method.Name.Lexeme))
            {
                foreach (var argument in expression.Arguments)
                    Visit(argument);
                return;
            }

            base.VisitCall(expression);
        }

        private bool IsPermittedCall(Expr.Call call, string methodName) => methodName switch
        {
            "set" => ReferenceEquals(call, _discardedCall)
                && call.Arguments is [var key, var value]
                && IsNumber(_typeMap.Get(key))
                && IsNumber(_typeMap.Get(value)),
            "get" or "has" or "delete" => call.Arguments is [var key]
                && IsNumber(_typeMap.Get(key)),
            "clear" => call.Arguments.Count == 0,
            _ => false
        };

        protected override void VisitGet(Expr.Get expression)
        {
            if (!expression.Optional
                && expression.Name.Lexeme == "size"
                && expression.Object is Expr.Variable receiver
                && IsExactNumericMap(_typeMap.Get(receiver)))
            {
                return;
            }

            base.VisitGet(expression);
        }

        protected override void VisitVariable(Expr.Variable expression)
        {
            if (expression.Name.Lexeme == "Map")
                IntrinsicMapIsObservable = true;
            Disqualified.Add((_scope, expression.Name.Lexeme));
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (expression.Name.Lexeme == "Map")
                IntrinsicMapIsObservable = true;
            Disqualified.Add((_scope, expression.Name.Lexeme));
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            if (expression.Name.Lexeme == "Map")
                IntrinsicMapIsObservable = true;
            Disqualified.Add((_scope, expression.Name.Lexeme));
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            if (expression.Name.Lexeme == "Map")
                IntrinsicMapIsObservable = true;
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
}
