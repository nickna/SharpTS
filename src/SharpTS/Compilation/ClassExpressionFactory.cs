using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal sealed record ClassExpressionFactory(MethodBuilder Method, Type Template, string Name, int Length);
