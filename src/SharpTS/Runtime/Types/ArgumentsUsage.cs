using System.Runtime.CompilerServices;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Per-declaration cache answering "does this function's body observe the JS
/// <c>arguments</c> binding?" so call paths can skip materializing the
/// <c>arguments</c> array (a List + SharpTSArray + per-arg boxing on EVERY
/// call) for the overwhelming majority of functions that never touch it.
/// Conservative on direct eval: a body referencing <c>eval</c> keeps the
/// binding, since interpreter eval runs against the live scope chain and can
/// observe <c>arguments</c> without the identifier appearing in the AST.
/// Keyed by declaration node reference, so closures re-created from the same
/// declaration (function factories) scan once.
/// </summary>
internal static class ArgumentsUsage
{
    private static readonly ConditionalWeakTable<object, object> _cache = new();
    private static readonly object BoxedTrue = true;
    private static readonly object BoxedFalse = false;

    public static bool UsesArguments(Stmt.Function declaration)
    {
        if (_cache.TryGetValue(declaration, out var cached))
            return (bool)cached;
        bool result = declaration.Body != null && Scan(declaration.Body, null);
        _cache.AddOrUpdate(declaration, result ? BoxedTrue : BoxedFalse);
        return result;
    }

    public static bool UsesArguments(Expr.ArrowFunction declaration)
    {
        if (_cache.TryGetValue(declaration, out var cached))
            return (bool)cached;
        bool result = Scan(declaration.BlockBody, declaration.ExpressionBody);
        _cache.AddOrUpdate(declaration, result ? BoxedTrue : BoxedFalse);
        return result;
    }

    private static bool Scan(List<Stmt>? blockBody, Expr? expressionBody)
    {
        var scanner = new ArgumentsRefScanner(treatEvalReferenceAsUse: true);
        if (expressionBody != null)
        {
            scanner.Visit(expressionBody);
            return scanner.Found;
        }
        if (blockBody == null)
            return false;
        foreach (var stmt in blockBody)
        {
            scanner.Visit(stmt);
            if (scanner.Found) return true;
        }
        return false;
    }
}
