using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private ImmediateArrowArguments? _immediateArrowArguments;

    private sealed class ImmediateArrowArguments : IDisposable
    {
        private readonly TypeChecker _checker;
        private readonly ImmediateArrowArguments? _previous;
        internal bool Capturing { get; set; } = true;
        internal int SuspensionDepth { get; set; }
        internal Dictionary<Expr, TypeInfo> Types { get; } = new(ReferenceEqualityComparer.Instance);
        internal HashSet<Expr> Refresh { get; } = new(ReferenceEqualityComparer.Instance);
        internal HashSet<Expr.ArrowFunction> DeferredArrows { get; } = new(ReferenceEqualityComparer.Instance);

        internal ImmediateArrowArguments(TypeChecker checker, IReadOnlyList<Expr> arguments)
        {
            _checker = checker;
            _previous = checker._immediateArrowArguments;
            checker._immediateArrowArguments = this;
            foreach (var argument in arguments) Collect(argument);
        }

        private bool Collect(Expr expression)
        {
            bool deferred = expression switch
            {
                Expr.ArrowFunction arrow => DeferredArrows.Add(arrow),
                Expr.Grouping grouping => Collect(grouping.Expression),
                Expr.Spread spread => Collect(spread.Expression),
                Expr.ObjectLiteral literal => literal.Properties.Select(property => Collect(property.Value)).ToArray().Any(value => value),
                Expr.ArrayLiteral literal => literal.Elements.Select(Collect).ToArray().Any(value => value),
                _ => false
            };
            if (deferred) Refresh.Add(expression);
            return deferred;
        }

        public void Dispose() => _checker._immediateArrowArguments = _previous;
    }

    private ImmediateArrowArguments? BeginImmediateArrowArguments(Expr.Call call)
    {
        Expr callee = UngroupGenericTupleExpression(call.Callee);
        if (call.Arguments.Count == 0 || callee is not Expr.ArrowFunction { IsAsync: false, IsGenerator: false }) return null;
        var scope = new ImmediateArrowArguments(this, call.Arguments);
        try
        {
            // Eager argument expressions run before the body. Function values receive only
            // headers here; their deferred bodies retain the normal argument contextual pass.
            CheckGenericInferenceArguments(call.Arguments);
            scope.Capturing = false;
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private ImmediateArrowArguments? ActiveImmediateArgumentCache =>
        _immediateArrowArguments is { SuspensionDepth: 0 } arguments ? arguments : null;

    private static void RecordImmediateArgumentType(ImmediateArrowArguments? arguments, Expr expression, TypeInfo type)
    {
        if (arguments is null) return;
        arguments.Types[expression] = type;
        if (!arguments.Capturing) arguments.Refresh.Remove(expression);
    }

    private readonly struct ImmediateArgumentCacheSuspension : IDisposable
    {
        private readonly ImmediateArrowArguments? _arguments;
        internal ImmediateArgumentCacheSuspension(TypeChecker checker)
        {
            _arguments = checker._immediateArrowArguments;
            if (_arguments is not null) _arguments.SuspensionDepth++;
        }
        public void Dispose()
        {
            if (_arguments is not null) _arguments.SuspensionDepth--;
        }
    }
}
