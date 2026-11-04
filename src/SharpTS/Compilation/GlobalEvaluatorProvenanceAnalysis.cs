using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

internal static class GlobalEvaluatorProvenanceAnalysis
{
    internal static bool RequiresRuntime(List<Stmt> statements, TypeMap? types)
    {
        if (types is null) return false;
        bool strict = DirectivePrologue.HasUseStrict(statements) ||
            statements.Any(statement => statement is Stmt.Import or Stmt.Export);
        var collector = new Collector(types, strict);
        foreach (var statement in statements) collector.Visit(statement);
        return collector.RequiresRuntime();
    }

    private sealed class Collector(TypeMap types, bool strictRoot) : AstVisitorBase
    {
        private readonly HashSet<BindingSymbol> _receiverFunctions = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<BindingSymbol> _globals = new(ReferenceEqualityComparer.Instance);
        private readonly List<(BindingSymbol Binding, Expr Value)> _initializers = [];
        private readonly List<(Expr Receiver, Expr? Index, string? Name)> _reads = [];
        private bool _strict = strictRoot;

        internal bool RequiresRuntime()
        {
            // Binding identities keep unrelated shadows and ordinary any-valued objects
            // outside this provenance. Monotone propagation also covers alias chains.
            bool changed;
            do
            {
                changed = false;
                foreach (var (binding, value) in _initializers)
                {
                    if (IsReceiverFunction(value)) changed |= _receiverFunctions.Add(binding);
                    if (IsGlobal(value)) changed |= _globals.Add(binding);
                }
            } while (changed);
            return _reads.Any(read => IsGlobal(read.Receiver) &&
                (read.Name == "eval" || read.Index is not null && ConstantKey(read.Index) is null or "eval"));
        }

        private bool IsReceiverFunction(Expr value) => Unwrap(value) is Expr.Variable variable &&
            types.GetValueBinding(variable.Name) is { } binding && _receiverFunctions.Contains(binding);

        private bool IsGlobal(Expr value) => Unwrap(value) switch
        {
            Expr.Variable variable => types.GetValueBinding(variable.Name) is { } binding
                ? _globals.Contains(binding) : variable.Name.Lexeme is "globalThis" or "global",
            Expr.Call call => IsReceiverFunction(call.Callee),
            Expr.Ternary conditional => IsGlobal(conditional.ThenBranch) || IsGlobal(conditional.ElseBranch),
            _ => false,
        };

        private static Expr Unwrap(Expr value) => value switch
        {
            Expr.Grouping group => Unwrap(group.Expression),
            Expr.TypeAssertion assertion => Unwrap(assertion.Expression),
            Expr.Satisfies satisfies => Unwrap(satisfies.Expression),
            Expr.NonNullAssertion nonNull => Unwrap(nonNull.Expression),
            Expr.Comma comma => Unwrap(comma.Right),
            _ => value,
        };

        private static string? ConstantKey(Expr value) => Unwrap(value) switch
        {
            Expr.Literal { Value: string text } => text,
            Expr.Literal { Value: double number } => Runtime.PropertyKeyConverter.ToPropertyKeyString(number),
            Expr.Binary { Operator.Type: TokenType.PLUS } binary when
                ConstantKey(binary.Left) is { } left && ConstantKey(binary.Right) is { } right => left + right,
            _ => null,
        };

        protected override void VisitFunction(Stmt.Function statement)
        {
            bool previous = _strict;
            _strict |= statement.Body is not null && DirectivePrologue.HasUseStrict(statement.Body);
            if (!_strict && !statement.IsAsync && !statement.IsGenerator && statement.Body is { } body &&
                types.GetValueBinding(statement.Name) is { } binding)
            {
                var returns = new ReceiverReturnVisitor();
                foreach (var child in body) returns.Visit(child);
                if (returns.ReturnsReceiver) _receiverFunctions.Add(binding);
            }
            try { base.VisitFunction(statement); }
            finally { _strict = previous; }
        }

        protected override void VisitClass(Stmt.Class statement)
        {
            bool previous = _strict;
            _strict = true;
            try { base.VisitClass(statement); }
            finally { _strict = previous; }
        }

        protected override void VisitVar(Stmt.Var statement)
        {
            if (statement.Initializer is { } value) Add(statement.Name, value);
            base.VisitVar(statement);
        }
        protected override void VisitConst(Stmt.Const statement)
        {
            Add(statement.Name, statement.Initializer);
            base.VisitConst(statement);
        }
        protected override void VisitAssign(Expr.Assign expression)
        {
            Add(expression.Name, expression.Value);
            base.VisitAssign(expression);
        }
        private void Add(Token name, Expr value)
        {
            if (types.GetValueBinding(name) is { } binding) _initializers.Add((binding, value));
        }
        protected override void VisitGet(Expr.Get expression)
        {
            _reads.Add((expression.Object, null, expression.Name.Lexeme));
            base.VisitGet(expression);
        }
        protected override void VisitGetIndex(Expr.GetIndex expression)
        {
            _reads.Add((expression.Object, expression.Index, null));
            base.VisitGetIndex(expression);
        }
    }

    private sealed class ReceiverReturnVisitor : AstVisitorBase
    {
        internal bool ReturnsReceiver { get; private set; }
        protected override void VisitReturn(Stmt.Return statement)
        {
            if (statement.Value is Expr.This) ReturnsReceiver = true;
            base.VisitReturn(statement);
        }
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
    }
}
