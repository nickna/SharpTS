using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// The namespace in which a TypeScript binding is resolved.
/// </summary>
public enum BindingNamespace
{
    Value,
    Type,
    Label,
}

/// <summary>
/// One source declaration participating in a semantic binding.
/// </summary>
public sealed record BindingDeclaration(SourceDocument Document, Token Name);

/// <summary>
/// Stable identity for one checker-resolved symbol.
/// </summary>
/// <remarks>
/// A symbol may have more than one declaration (for example, function overloads or legal
/// <c>var</c> redeclarations). Uses point at the identity rather than directly at a span so later
/// references and rename support can share the same resolution result.
/// </remarks>
public sealed class BindingSymbol
{
    private readonly List<BindingDeclaration> _declarations = [];

    internal BindingSymbol(
        int id,
        int generation,
        string name,
        BindingNamespace bindingNamespace)
    {
        Id = id;
        Generation = generation;
        Name = name;
        Namespace = bindingNamespace;
    }

    public int Id { get; }
    internal int Generation { get; }
    public string Name { get; }
    public BindingNamespace Namespace { get; }
    public IReadOnlyList<BindingDeclaration> Declarations => _declarations;

    internal void AddDeclaration(SourceDocument? document, Token name)
    {
        if (document is null || name.Start < 0)
            return;

        if (_declarations.Any(d =>
                ReferenceEquals(d.Document, document) && ReferenceEquals(d.Name, name)))
            return;

        _declarations.Add(new BindingDeclaration(document, name));
    }
}

/// <summary>
/// Checker-produced map from source tokens to semantic binding identities.
/// </summary>
/// <remarks>
/// This is deliberately populated at the same points where <see cref="TypeEnvironment"/> defines
/// and resolves names. It therefore follows the checker's real hoisting and shadowing behavior
/// instead of maintaining a second, editor-only implementation of TypeScript scopes.
/// </remarks>
public sealed class BindingIndex
{
    private readonly Dictionary<Token, BindingSymbol> _tokens =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, SourceDocument> _documents =
        new(ReferenceEqualityComparer.Instance);
    private int _nextId = 1;
    private int _generation;

    internal void Clear()
    {
        _tokens.Clear();
        _documents.Clear();
        _generation++;
    }

    internal BindingSymbol Declare(
        Token declaration,
        SourceDocument? document,
        BindingNamespace bindingNamespace,
        BindingSymbol? existing = null)
    {
        if (_tokens.TryGetValue(declaration, out var alreadyDeclared))
            return alreadyDeclared;

        var symbol = existing is { Generation: var generation } && generation == _generation
            ? existing
            : new BindingSymbol(
                _nextId++,
                _generation,
                declaration.Lexeme,
                bindingNamespace);

        symbol.AddDeclaration(document, declaration);
        _tokens[declaration] = symbol;
        if (document is not null && declaration.Start >= 0)
            _documents[declaration] = document;
        return symbol;
    }

    internal void Bind(Token use, SourceDocument? document, BindingSymbol symbol)
    {
        if (use.Start < 0 || symbol.Generation != _generation)
            return;

        _tokens[use] = symbol;
        if (document is not null)
            _documents[use] = document;
    }

    /// <summary>
    /// Returns the declarations for the binding at a UTF-16 source offset.
    /// </summary>
    public IReadOnlyList<BindingDeclaration> FindDefinitions(
        SourceDocument document,
        int offset,
        BindingNamespace bindingNamespace = BindingNamespace.Value)
    {
        BindingSymbol? best = null;
        int bestLength = int.MaxValue;

        foreach (var (token, symbol) in _tokens)
        {
            if (symbol.Namespace != bindingNamespace ||
                !_documents.TryGetValue(token, out var tokenDocument) ||
                !ReferenceEquals(tokenDocument, document) ||
                !token.Span.Contains(offset))
            {
                continue;
            }

            if (token.Span.Length < bestLength)
            {
                best = symbol;
                bestLength = token.Span.Length;
            }
        }

        return best?.Declarations ?? [];
    }
}
