using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Finds the deliberately narrow Map-entry loop shape that can skip JavaScript
/// entry-array materialization. The receiver must be a fresh numeric Map local
/// that never escapes; the loop entry itself may only be observed through
/// literal <c>[0]</c> and <c>[1]</c> reads.
/// </summary>
internal static class StableMapIterationAnalyzer
{
    public static void Analyze(List<Stmt> program, TypeMap? typeMap, ClosureAnalyzer? closures)
    {
        if (typeMap == null) return;

        var visitor = new ReceiverVisitor(typeMap);
        foreach (var statement in program)
            visitor.Visit(statement);

        if (visitor.ContainsDirectEval)
            return;

        foreach (var (key, loops) in visitor.Loops)
        {
            if (!visitor.Candidates.Contains(key)
                || visitor.Disqualified.Contains(key)
                || visitor.DeclarationCounts.GetValueOrDefault(key) != 1
                || closures?.IsVariableCaptured(key.Name) == true)
            {
                continue;
            }

            foreach (var loop in loops)
            {
                if (loop.IsAsync
                    || loop.Variable.Lexeme == key.Name
                    || typeMap.Get(loop.Iterable) is not TypeInfo.Map map
                    || !IsNumber(map.KeyType)
                    || !IsNumber(map.ValueType)
                    || !EntryUseVisitor.IsSafe(loop.Body, loop.Variable.Lexeme))
                {
                    continue;
                }

                typeMap.MarkStableNumericMapIteration(loop);
            }
        }
    }

    private static bool IsNumber(TypeInfo? type) =>
        type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral;

    private sealed class ReceiverVisitor(TypeMap typeMap) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private readonly Stack<(int Scope, string Name)> _activeMapIterations = new();
        private Expr.Call? _discardedCall;
        private int _scope;
        private int _nextScope;

        public HashSet<(int Scope, string Name)> Candidates { get; } = [];
        public HashSet<(int Scope, string Name)> Disqualified { get; } = [];
        public Dictionary<(int Scope, string Name), int> DeclarationCounts { get; } = [];
        public Dictionary<(int Scope, string Name), List<Stmt.ForOf>> Loops { get; } = [];
        public bool ContainsDirectEval { get; private set; }

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

        protected override void VisitVar(Stmt.Var statement)
        {
            HandleDeclaration(statement.Name, statement.Initializer);
        }

        protected override void VisitConst(Stmt.Const statement)
        {
            HandleDeclaration(statement.Name, statement.Initializer);
        }

        private void HandleDeclaration(Token name, Expr? initializer)
        {
            var key = (_scope, name.Lexeme);
            DeclarationCounts[key] = DeclarationCounts.GetValueOrDefault(key) + 1;

            if (initializer is Expr.New
                {
                    Callee: Expr.Variable { Name.Lexeme: "Map" },
                    Arguments.Count: 0
                }
                && _typeMap.Get(initializer) is TypeInfo.Map map
                && IsNumber(map.KeyType)
                && IsNumber(map.ValueType))
            {
                Candidates.Add(key);
            }

            if (initializer != null)
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

                // This receiver occurrence is the intended non-escaping use.
                _activeMapIterations.Push(key);
                try
                {
                    Visit(statement.Body);
                }
                finally
                {
                    _activeMapIterations.Pop();
                }
                return;
            }

            base.VisitForOf(statement);
        }

        protected override void VisitCall(Expr.Call expression)
        {
            if (!expression.Optional
                && expression.Callee is Expr.Variable { Name.Lexeme: "eval" })
            {
                ContainsDirectEval = true;
            }

            // A typed set on the exact local preserves the numeric representation.
            // Skip the receiver variable so the catch-all VisitVariable does not
            // classify this permitted use as an escape.
            if (!expression.Optional
                && expression.Callee is Expr.Get
                {
                    Object: Expr.Variable receiver,
                    Name.Lexeme: "set",
                    Optional: false
                }
                && expression.Arguments is [var key, var value]
                && IsNumber(_typeMap.Get(key))
                && IsNumber(_typeMap.Get(value))
                && ReferenceEquals(expression, _discardedCall)
                && !_activeMapIterations.Contains((_scope, receiver.Name.Lexeme)))
            {
                Visit(key);
                Visit(value);
                return;
            }

            base.VisitCall(expression);
        }

        protected override void VisitExpression(Stmt.Expression statement)
        {
            var saved = _discardedCall;
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

        protected override void VisitGet(Expr.Get expression)
        {
            if (!expression.Optional
                && expression.Name.Lexeme == "size"
                && expression.Object is Expr.Variable)
            {
                // Reading size neither aliases nor mutates the receiver.
                return;
            }

            base.VisitGet(expression);
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

    private sealed class EntryUseVisitor(string entryName) : AstVisitorBase
    {
        private readonly string _entryName = entryName;
        public bool Safe { get; private set; } = true;

        public static bool IsSafe(Stmt body, string entryName)
        {
            var visitor = new EntryUseVisitor(entryName);
            visitor.Visit(body);
            return visitor.Safe;
        }

        protected override void VisitGetIndex(Expr.GetIndex expression)
        {
            if (expression.Object is Expr.Variable variable
                && variable.Name.Lexeme == _entryName)
            {
                if (expression.Optional
                    || expression.Index is not Expr.Literal { Value: double index }
                    || index is not (0d or 1d))
                {
                    Safe = false;
                }
                return;
            }

            base.VisitGetIndex(expression);
        }

        protected override void VisitVar(Stmt.Var statement)
        {
            if (statement.Name.Lexeme == _entryName)
                Safe = false;
            base.VisitVar(statement);
        }

        protected override void VisitConst(Stmt.Const statement)
        {
            if (statement.Name.Lexeme == _entryName)
                Safe = false;
            base.VisitConst(statement);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            if (statement.Variable.Lexeme == _entryName)
                Safe = false;
            base.VisitForOf(statement);
        }

        protected override void VisitForIn(Stmt.ForIn statement)
        {
            if (statement.Variable.Lexeme == _entryName)
                Safe = false;
            base.VisitForIn(statement);
        }

        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            if (statement.CatchParam?.Lexeme == _entryName)
                Safe = false;
            base.VisitTryCatch(statement);
        }

        protected override void VisitFunction(Stmt.Function statement)
        {
            if (statement.Name.Lexeme == _entryName)
                Safe = false;
            base.VisitFunction(statement);
        }

        protected override void VisitClass(Stmt.Class statement)
        {
            if (statement.Name.Lexeme == _entryName)
                Safe = false;
            base.VisitClass(statement);
        }

        protected override void VisitUsing(Stmt.Using statement)
        {
            if (statement.Bindings.Any(binding => binding.Name?.Lexeme == _entryName))
                Safe = false;
            base.VisitUsing(statement);
        }

        protected override void VisitVariable(Expr.Variable expression)
        {
            if (expression.Name.Lexeme == _entryName)
                Safe = false;
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (expression.Name.Lexeme == _entryName)
                Safe = false;
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            if (expression.Name.Lexeme == _entryName)
                Safe = false;
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            if (expression.Name.Lexeme == _entryName)
                Safe = false;
            base.VisitLogicalAssign(expression);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            if (IsEntryIndex(expression.Operand))
                Safe = false;
            base.VisitPrefixIncrement(expression);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            if (IsEntryIndex(expression.Operand))
                Safe = false;
            base.VisitPostfixIncrement(expression);
        }

        protected override void VisitDelete(Expr.Delete expression)
        {
            if (IsEntryIndex(expression.Operand))
                Safe = false;
            base.VisitDelete(expression);
        }

        private bool IsEntryIndex(Expr expression) =>
            expression is Expr.GetIndex
            {
                Object: Expr.Variable variable
            } && variable.Name.Lexeme == _entryName;
    }
}
