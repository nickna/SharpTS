using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>Native source identities, allocated only by actual source declaration/template producers.</summary>
internal sealed class NativeSourceTypeNames
{
    internal const string DeclarationDomain = "<SharpTS.Source>.Declared.";
    internal const string ExpressionDomain = "<SharpTS.Source>.Expression.";
    private readonly Dictionary<Stmt.Class, (string Logical, string Emitted)> _declarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ClassExpr, (string Template, string Emitted)> _expressions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, string> _classAliases = new(StringComparer.Ordinal);

    internal string Declare(Stmt.Class source, string logicalName, bool exposeClassAlias)
    {
        if (!ILCompiler.EmitsSourceClassBody(source))
            throw new InvalidOperationException("Only an actual source class body can allocate a native source identity.");
        if (_declarations.TryGetValue(source, out var existing))
        {
            if (existing.Logical != logicalName) throw new InvalidOperationException("Source class identity changed after allocation.");
            return existing.Emitted;
        }
        // The ordinal keeps generated block/namespace names distinct from valid
        // source identifiers which happen to spell an existing generated name.
        string emitted = DeclarationDomain + "D" + _declarations.Count + "." + logicalName;
        _declarations.Add(source, (logicalName, emitted));
        if (exposeClassAlias) _classAliases.Add(logicalName, emitted);
        return emitted;
    }

    internal string DeclareExpression(Expr.ClassExpr source, string templateName)
    {
        if (_expressions.TryGetValue(source, out var existing))
        {
            if (existing.Template != templateName) throw new InvalidOperationException("Source template identity changed after allocation.");
            return existing.Emitted;
        }
        string emitted = ExpressionDomain + templateName;
        _expressions.Add(source, (templateName, emitted));
        return emitted;
    }

    // This is class-role resolution only; namespace storage and CLR aliases never
    // register here. No prefix/name spelling alone authorizes a source identity.
    internal string ResolveClassCandidate(string logicalName) => _classAliases.GetValueOrDefault(logicalName) ?? logicalName;
}
