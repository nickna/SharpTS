using System.Reflection;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>Per-emission function capabilities, derived before runtime declaration.</summary>
internal readonly record struct FunctionEmissionPolicy(bool BindNumericRest4)
{
    internal MethodInfo? NativeNumericRest4Binder { get; init; }
    internal MethodInfo? NativeGenericNormalizer { get; init; }
    internal MethodInfo? NativeConstructedGenericResolver { get; init; }
    internal NativeConstructedBodyLookup? NativeConstructedBodies { get; init; }
    internal MethodInfo? NativeUnionConverter { get; init; }
    internal bool RejectReflectiveCallableFallback { get; init; }

    internal MethodInfo? NativeReceiverBinder { get; init; }
    internal static FunctionEmissionPolicy Full => new(true);

    internal static FunctionEmissionPolicy ForPreparedSource(IEnumerable<Stmt> statements, bool native)
    {
        if (!native) return Full;
        var probe = new PotentialProducerProbe();
        foreach (var statement in statements) probe.Visit(statement);
        return new(probe.MayProduce) { RejectReflectiveCallableFallback = true };
    }

    internal void RequireNumericRest4Producer()
    {
        if (!BindNumericRest4)
            throw new InvalidOperationException("A numeric-rest capability producer escaped the prepared-source proof.");
    }

    // Over-approximate the later stable-function analysis: even ineligible
    // rest functions keep binding. Source-execution syntax keeps it as well,
    // without relying on a user-supplied RuntimeFeatureSet override.
    private sealed class PotentialProducerProbe : AstVisitorBase
    {
        internal bool MayProduce { get; private set; }
        protected override void VisitFunction(Stmt.Function statement)
        {
            MayProduce |= statement.Parameters.Any(parameter => parameter.IsRest);
            base.VisitFunction(statement);
        }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            MayProduce |= expression.Parameters.Any(parameter => parameter.IsRest);
            base.VisitArrowFunction(expression);
        }
        protected override void VisitVariable(Expr.Variable expression) =>
            MayProduce |= IsSourceExecutionName(expression.Name.Lexeme);
        protected override void VisitGet(Expr.Get expression)
        {
            MayProduce |= IsSourceExecutionName(expression.Name.Lexeme);
            base.VisitGet(expression);
        }
        private static bool IsSourceExecutionName(string name) => name is
            "eval" or "Function" or "AsyncFunction" or "GeneratorFunction" or "AsyncGeneratorFunction" or "globalThis" or "global";
    }
}
