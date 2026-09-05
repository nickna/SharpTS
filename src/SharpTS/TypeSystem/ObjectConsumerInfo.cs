using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>A bounded numeric-only consumer whose parameter cannot escape or alias another value.</summary>
public sealed record ObjectConsumerInfo(
    string ParameterName, IReadOnlyList<Expr.Set> Writes, Expr Result,
    IReadOnlySet<string> NumericFields);
