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
/// One source occurrence of a semantic binding.
/// </summary>
public sealed record BindingOccurrence(
    SourceDocument Document,
    Token Name,
    bool IsDeclaration);

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
    private readonly Dictionary<Token, Dictionary<BindingNamespace, BindingSymbol>> _tokens =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, SourceDocument> _documents =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingSymbol, Dictionary<Token, SourceDocument>> _occurrences =
        new(ReferenceEqualityComparer.Instance);
    private int _nextId = 1;
    private int _generation;

    internal void Clear()
    {
        _tokens.Clear();
        _documents.Clear();
        _occurrences.Clear();
        _generation++;
    }

    internal BindingSymbol Declare(
        Token declaration,
        SourceDocument? document,
        BindingNamespace bindingNamespace,
        BindingSymbol? existing = null)
    {
        if (_tokens.TryGetValue(declaration, out var facets) &&
            facets.TryGetValue(bindingNamespace, out var alreadyDeclared))
            return alreadyDeclared;

        var symbol = existing is { Generation: var generation } && generation == _generation
            ? existing
            : new BindingSymbol(
                _nextId++,
                _generation,
                declaration.Lexeme,
                bindingNamespace);

        symbol.AddDeclaration(document, declaration);
        if (facets is null)
        {
            facets = [];
            _tokens[declaration] = facets;
        }
        facets[bindingNamespace] = symbol;
        if (document is not null && declaration.Start >= 0)
        {
            _documents[declaration] = document;
            RecordOccurrence(symbol, declaration, document);
        }
        return symbol;
    }

    internal void Bind(Token use, SourceDocument? document, BindingSymbol symbol)
    {
        if (use.Start < 0 || symbol.Generation != _generation)
            return;

        if (!_tokens.TryGetValue(use, out var facets))
        {
            facets = [];
            _tokens[use] = facets;
        }
        else if (facets.TryGetValue(symbol.Namespace, out var previous) &&
                 !ReferenceEquals(previous, symbol))
        {
            RemoveOccurrence(previous, use);
        }
        facets[symbol.Namespace] = symbol;
        if (document is not null)
        {
            _documents[use] = document;
            RecordOccurrence(symbol, use, document);
        }
    }

    private void RecordOccurrence(
        BindingSymbol symbol,
        Token token,
        SourceDocument document)
    {
        if (!_occurrences.TryGetValue(symbol, out var occurrences))
        {
            occurrences = new Dictionary<Token, SourceDocument>(
                ReferenceEqualityComparer.Instance);
            _occurrences[symbol] = occurrences;
        }
        occurrences[token] = document;
    }

    private void RemoveOccurrence(BindingSymbol symbol, Token token)
    {
        if (!_occurrences.TryGetValue(symbol, out var occurrences))
            return;

        occurrences.Remove(token);
        if (occurrences.Count == 0)
            _occurrences.Remove(symbol);
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

        foreach (var (token, facets) in _tokens)
        {
            if (!facets.TryGetValue(bindingNamespace, out var symbol) ||
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

    /// <summary>
    /// Returns all checker-bound occurrences for the semantic binding at a UTF-16 source offset.
    /// When the selected token has both value and type facets (for example a class declaration),
    /// occurrences from both identities are unioned.
    /// </summary>
    public IReadOnlyList<BindingOccurrence> FindReferences(
        SourceDocument document,
        int offset,
        bool includeDeclarations)
    {
        IReadOnlyList<BindingSymbol> symbols = FindSymbols(document, offset);
        return FindReferences(symbols, includeDeclarations);
    }

    /// <summary>
    /// Returns all checker-bound occurrences for explicit symbols from this index.
    /// </summary>
    public IReadOnlyList<BindingOccurrence> FindReferences(
        IReadOnlyList<BindingSymbol> symbols,
        bool includeDeclarations)
    {
        if (symbols.Count == 0)
            return [];

        var occurrences = new Dictionary<Token, OccurrenceBuilder>(
            ReferenceEqualityComparer.Instance);
        foreach (var symbol in symbols)
        {
            if (!_occurrences.TryGetValue(symbol, out var symbolOccurrences))
                continue;

            foreach (var (token, occurrenceDocument) in symbolOccurrences)
            {
                bool isDeclaration = symbol.Declarations.Any(declaration =>
                    ReferenceEquals(declaration.Document, occurrenceDocument) &&
                    ReferenceEquals(declaration.Name, token));

                if (occurrences.TryGetValue(token, out var existing))
                {
                    existing.IsDeclarationInEveryFacet &= isDeclaration;
                }
                else
                {
                    occurrences[token] = new OccurrenceBuilder(
                        occurrenceDocument,
                        isDeclaration);
                }
            }
        }

        return occurrences
            .Where(pair =>
                includeDeclarations || !pair.Value.IsDeclarationInEveryFacet)
            .Select(pair => new BindingOccurrence(
                pair.Value.Document,
                pair.Key,
                pair.Value.IsDeclarationInEveryFacet))
            .OrderBy(occurrence => occurrence.Document.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(occurrence => occurrence.Name.Start)
            .ToArray();
    }

    /// <summary>
    /// Returns the narrowest checker-bound value/type facets at a UTF-16 source offset.
    /// </summary>
    public IReadOnlyList<BindingSymbol> FindSymbols(
        SourceDocument document,
        int offset)
    {
        var symbols = new HashSet<BindingSymbol>(ReferenceEqualityComparer.Instance);
        int bestLength = int.MaxValue;

        foreach (var (token, facets) in _tokens)
        {
            if (!_documents.TryGetValue(token, out var tokenDocument) ||
                !ReferenceEquals(tokenDocument, document) ||
                !token.Span.Contains(offset))
            {
                continue;
            }

            if (token.Span.Length < bestLength)
            {
                symbols.Clear();
                bestLength = token.Span.Length;
            }
            if (token.Span.Length == bestLength)
            {
                foreach (var symbol in facets.Values)
                    symbols.Add(symbol);
            }
        }

        return symbols.ToArray();
    }

    private sealed class OccurrenceBuilder(
        SourceDocument document,
        bool isDeclarationInEveryFacet)
    {
        public SourceDocument Document { get; } = document;
        public bool IsDeclarationInEveryFacet { get; set; } =
            isDeclarationInEveryFacet;
    }
}
