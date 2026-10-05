using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

internal sealed record ClassExpressionFactory(MethodBuilder Method, Type Template, string Name, int Length,
    MethodBuilder Initializer, IReadOnlyList<Expr> Keys);
