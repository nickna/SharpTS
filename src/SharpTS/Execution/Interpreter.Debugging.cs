using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime;

namespace SharpTS.Execution;

public partial class Interpreter
{
    internal void NotifyDebuggerCaughtException(object? exception) =>
        DebugController?.OnException(
            this, exception, unhandled: false, DebugController.BreakOnCaughtException);

    internal void NotifyDebuggerUnhandledException(object? exception) =>
        DebugController?.OnException(
            this, exception, unhandled: true, DebugController.BreakOnUncaughtException);

    internal void NotifyDebuggerUnhandledRejection(object? exception) =>
        DebugController?.OnException(
            this, exception, unhandled: true, DebugController.BreakOnUnhandledRejection);

    internal object? EvaluateDebuggerExpression(
        string source,
        RuntimeEnvironment environment,
        bool allowPropertyAccess,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(environment);

        var parser = new Parser(new Lexer(source).ScanTokens());
        var parseResult = parser.Parse();
        if (!parseResult.IsSuccess || parseResult.Statements.Count != 1)
        {
            string detail = parseResult.Diagnostics.FirstOrDefault()?.ToString()
                ?? "Expected one expression.";
            throw new InvalidOperationException($"Debugger evaluation syntax error: {detail}");
        }
        if (parseResult.Statements[0] is not Stmt.Expression expression)
            throw new InvalidOperationException("Debugger evaluation expects one expression.");

        var validator = new ReadOnlyEvaluationValidator(allowPropertyAccess);
        validator.Visit(expression.Expr);

        RuntimeEnvironment previousEnvironment = _environment;
        CancellationToken previousTimeout = _vmTimeoutToken;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            _environment = environment;
            _vmTimeoutToken = timeout.Token;
            timeout.Token.ThrowIfCancellationRequested();
            return EvaluateRV(expression.Expr).ToObject();
        }
        finally
        {
            _vmTimeoutToken = previousTimeout;
            _environment = previousEnvironment;
        }
    }

    private sealed class ReadOnlyEvaluationValidator(bool allowPropertyAccess) : AstVisitorBase
    {
        protected override void VisitAssign(Expr.Assign expr) => Reject("assignment");
        protected override void VisitDestructuringAssign(Expr.DestructuringAssign expr) => Reject("assignment");
        protected override void VisitSet(Expr.Set expr) => Reject("assignment");
        protected override void VisitSetIndex(Expr.SetIndex expr) => Reject("assignment");
        protected override void VisitSetPrivate(Expr.SetPrivate expr) => Reject("assignment");
        protected override void VisitCompoundAssign(Expr.CompoundAssign expr) => Reject("assignment");
        protected override void VisitCompoundSet(Expr.CompoundSet expr) => Reject("assignment");
        protected override void VisitCompoundSetIndex(Expr.CompoundSetIndex expr) => Reject("assignment");
        protected override void VisitLogicalAssign(Expr.LogicalAssign expr) => Reject("assignment");
        protected override void VisitLogicalSet(Expr.LogicalSet expr) => Reject("assignment");
        protected override void VisitLogicalSetIndex(Expr.LogicalSetIndex expr) => Reject("assignment");
        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expr) => Reject("update");
        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expr) => Reject("update");
        protected override void VisitDelete(Expr.Delete expr) => Reject("delete");
        protected override void VisitCall(Expr.Call expr) => Reject("function call");
        protected override void VisitCallPrivate(Expr.CallPrivate expr) => Reject("function call");
        protected override void VisitNew(Expr.New expr) => Reject("construction");
        protected override void VisitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral expr) => Reject("tagged template call");
        protected override void VisitDynamicImport(Expr.DynamicImport expr) => Reject("dynamic import");
        protected override void VisitAwait(Expr.Await expr) => Reject("await");
        protected override void VisitYield(Expr.Yield expr) => Reject("yield");
        protected override void VisitArrowFunction(Expr.ArrowFunction expr) => Reject("function creation");
        protected override void VisitClassExpr(Expr.ClassExpr expr) => Reject("class creation");

        protected override void VisitGet(Expr.Get expr)
        {
            if (!allowPropertyAccess)
                Reject("property access in hover evaluation");
            base.VisitGet(expr);
        }

        protected override void VisitGetIndex(Expr.GetIndex expr)
        {
            if (!allowPropertyAccess)
                Reject("index access in hover evaluation");
            base.VisitGetIndex(expr);
        }

        protected override void VisitGetPrivate(Expr.GetPrivate expr)
        {
            if (!allowPropertyAccess)
                Reject("private property access in hover evaluation");
            base.VisitGetPrivate(expr);
        }

        private static void Reject(string operation) =>
            throw new InvalidOperationException(
                $"Debugger evaluation is read-only; {operation} is not allowed.");
    }
}
