using System.Collections.Frozen;
using System.Collections.Immutable;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public enum MemberFacet { Instance, Static, PrivateInstance, PrivateStatic }
public enum SourceMemberKind { Field, Method, Accessor, AutoAccessor, ParameterProperty }

[Flags]
public enum MemberOperation { None = 0, Declaration = 1, Read = 2, Write = 4, Call = 8, Presence = 16 }

public sealed record SourceMemberDeclaration(SourceDocument Document, Token Name,
    SourceMemberKind Kind, object? Owner = null)
{
    public SourceSpan Span => Name.Span;
}

public sealed record SourceMemberGroup(string Name, MemberFacet Facet, SourceMemberKind Kind,
    IReadOnlyList<SourceMemberDeclaration> Declarations);

/// <summary>Source identity uses its first observed nominal ID; later nominal IDs are aliases.</summary>
public sealed record SourceClassInfo(int DeclarationId, SourceDocument Document, object Owner,
    bool IsNestedPrivateEnvironment = false, bool ContainsNestedClass = false);

/// <summary>Canonical identity for one source-class member in one checker generation.</summary>
public sealed class SourceMemberSymbol
{
    private readonly List<SourceMemberDeclaration> _declarations = [];

    internal SourceMemberSymbol(int id, long generation, int declaringClassId, string name,
        MemberFacet facet, SourceMemberKind kind)
    {
        Id = id; Generation = generation; DeclaringClassId = declaringClassId;
        Name = name; Facet = facet; Kind = kind;
    }

    public int Id { get; }
    public long Generation { get; }
    public int DeclaringClassId { get; }
    public string Name { get; }
    public MemberFacet Facet { get; }
    public SourceMemberKind Kind { get; }
    public IReadOnlyList<SourceMemberDeclaration> Declarations => _declarations;
    public bool CanRename => false;

    internal void AddDeclaration(SourceMemberDeclaration declaration)
    {
        if (!_declarations.Any(existing => ReferenceEquals(existing.Document, declaration.Document) &&
            ReferenceEquals(existing.Name, declaration.Name))) _declarations.Add(declaration);
    }
}

/// <summary>
/// Known candidates and whether every required lookup was supported. A finite ambiguous set is
/// retained explicitly; neither ambiguity nor a partly unsupported lookup selects an identity.
/// </summary>
public sealed class MemberResolution
{
    private MemberResolution(IEnumerable<SourceMemberSymbol> candidates, bool isComplete)
    {
        Candidates = candidates.Distinct<SourceMemberSymbol>(ReferenceEqualityComparer.Instance)
            .OrderBy(symbol => symbol.Generation).ThenBy(symbol => symbol.Id).ToImmutableArray();
        IsComplete = isComplete && Candidates.Count != 0;
    }

    public static MemberResolution Unavailable { get; } = new([], false);
    public IReadOnlyList<SourceMemberSymbol> Candidates { get; }
    public bool IsComplete { get; }
    public bool IsResolved => IsComplete && Candidates.Count == 1;
    public bool IsAmbiguous => Candidates.Count > 1;
    public static MemberResolution For(SourceMemberSymbol symbol) => new([symbol], true);

    public static MemberResolution CombineRequired(IEnumerable<MemberResolution> selections)
    {
        var values = selections.ToArray();
        if (values.Length == 0) return Unavailable;
        return new(values.SelectMany(value => value.Candidates), values.All(value => value.IsComplete));
    }
}

public sealed record MemberOccurrence(SourceDocument Document, Token Name, MemberOperation Operations,
    bool IsDeclaration, MemberResolution Resolution,
    IReadOnlyDictionary<MemberOperation, MemberResolution> Selections, object? Owner = null);

/// <summary>
/// Opt-in checker-owned member registry. Registration records source ownership; expression hooks
/// report the member actually selected by ordinary checking. This index performs no type lookup.
/// </summary>
public sealed class MemberIndex
{
    private static long _nextGeneration;
    internal static MemberIndex Empty { get; } = new(enabled: false);
    private readonly Dictionary<MemberKey, SourceMemberSymbol> _symbols = [];
    private readonly Dictionary<int, SourceClassInfo> _classes = [];
    private readonly Dictionary<int, int> _classAliases = [];
    private readonly Dictionary<SourceDocument, Dictionary<object, int>> _sourceOwners =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, OccurrenceBuilder> _occurrences = new(ReferenceEqualityComparer.Instance);
    private int _nextId = 1;

    public MemberIndex() : this(enabled: true) { }
    private MemberIndex(bool enabled)
    {
        IsEnabled = enabled;
        Generation = enabled ? Interlocked.Increment(ref _nextGeneration) : 0;
    }

    public bool IsEnabled { get; }
    public long Generation { get; private set; }
    public IReadOnlyList<SourceMemberSymbol> Symbols => _symbols.Values.OrderBy(symbol => symbol.Id).ToArray();
    public int Count => _symbols.Count + _occurrences.Count;

    public void Clear()
    {
        if (!IsEnabled) return;
        _symbols.Clear(); _classes.Clear(); _classAliases.Clear(); _sourceOwners.Clear();
        _occurrences.Clear(); _nextId = 1;
        Generation = Interlocked.Increment(ref _nextGeneration);
    }

    public void RegisterSourceClass(int declarationId, SourceDocument document, object owner,
        IReadOnlyList<SourceMemberGroup> groups, bool isNestedPrivateEnvironment = false,
        bool containsNestedClass = false)
    {
        if (!IsEnabled || declarationId == 0) return;
        int canonicalId;
        if (_classAliases.TryGetValue(declarationId, out canonicalId))
        {
            var registered = _classes[canonicalId];
            // An actual nominal ID cannot authorize a different source owner. The owner proof
            // deliberately uses reference identity, even for structurally equal AST records.
            if (!ReferenceEquals(registered.Document, document) || !ReferenceEquals(registered.Owner, owner)) return;
        }
        else
        {
            if (!_sourceOwners.TryGetValue(document, out var owners))
                _sourceOwners.Add(document, owners = new(ReferenceEqualityComparer.Instance));
            if (!owners.TryGetValue(owner, out canonicalId)) owners.Add(owner, canonicalId = declarationId);
            _classAliases.Add(declarationId, canonicalId);
        }
        if (_classes.TryGetValue(canonicalId, out var previous))
            _classes[canonicalId] = previous with
            {
                IsNestedPrivateEnvironment = previous.IsNestedPrivateEnvironment || isNestedPrivateEnvironment,
                ContainsNestedClass = previous.ContainsNestedClass || containsNestedClass,
            };
        else _classes.Add(canonicalId, new(canonicalId, document, owner,
            isNestedPrivateEnvironment, containsNestedClass));

        foreach (SourceMemberGroup group in groups)
        {
            var declarations = group.Declarations.Where(declaration => Valid(declaration.Document, declaration.Name)).ToArray();
            if (declarations.Length == 0) continue;
            var key = new MemberKey(canonicalId, group.Facet, group.Name);
            if (!_symbols.TryGetValue(key, out var symbol))
                _symbols.Add(key, symbol = new(_nextId++, Generation, canonicalId, group.Name, group.Facet, group.Kind));
            foreach (SourceMemberDeclaration declaration in declarations)
            {
                symbol.AddDeclaration(declaration);
                Bind(declaration.Document, declaration.Name, MemberResolution.For(symbol),
                    MemberOperation.Declaration, isDeclaration: true, declaration.Owner);
            }
        }
    }

    public MemberResolution ResolveSelected(int declarationId, MemberFacet facet, string name) =>
        IsEnabled && _classAliases.TryGetValue(declarationId, out int canonicalId) &&
            _symbols.TryGetValue(new(canonicalId, facet, name), out var symbol)
            ? MemberResolution.For(symbol) : MemberResolution.Unavailable;

    /// <summary>Actual nominal IDs alias only through the same captured source owner.</summary>
    public bool IsSameSourceClass(int leftId, int rightId) => IsEnabled &&
        _classAliases.TryGetValue(leftId, out int left) &&
        _classAliases.TryGetValue(rightId, out int right) && left == right;

    public void Bind(SourceDocument? document, Token name, MemberResolution resolution,
        MemberOperation operations, bool isDeclaration = false, object? owner = null)
    {
        if (!IsEnabled || document is null || !Valid(document, name) || operations == MemberOperation.None) return;
        if (resolution.Candidates.Any(symbol => symbol.Generation != Generation)) return;
        if (!_occurrences.TryGetValue(name, out var occurrence))
            _occurrences.Add(name, occurrence = new(document, name, owner));
        else if (!ReferenceEquals(occurrence.Document, document)) return;
        occurrence.IsDeclaration |= isDeclaration;
        foreach (MemberOperation operation in AtomicOperations(operations))
        {
            if (!occurrence.OwnerFacts.TryGetValue(operation, out var facts))
                occurrence.OwnerFacts.Add(operation, facts = new(ReferenceEqualityComparer.Instance));
            // The checker can revisit one AST during preparatory/authoritative passes. Its final
            // fact replaces its previous fact, like the TypeMap. Different owners still combine;
            // a caller omitting ownership cannot claim an earlier candidate has been superseded.
            facts[owner ?? resolution] = resolution;
            occurrence.Selections[operation] = MemberResolution.CombineRequired(facts.Values);
        }
    }

    public MemberResolution GetResolution(Token name) => _occurrences.TryGetValue(name, out var occurrence)
        ? MemberResolution.CombineRequired(occurrence.Selections.Values) : MemberResolution.Unavailable;

    /// <summary>Clears an owner's previous attempt without erasing independently proven facts.</summary>
    public void RemoveOperation(SourceDocument? document, Token name,
        MemberOperation operations, object owner)
    {
        if (!IsEnabled || document is null || !_occurrences.TryGetValue(name, out var occurrence) ||
            !ReferenceEquals(occurrence.Document, document)) return;
        foreach (MemberOperation operation in AtomicOperations(operations))
        {
            if (!occurrence.OwnerFacts.TryGetValue(operation, out var facts) || !facts.Remove(owner)) continue;
            if (facts.Count == 0)
            {
                occurrence.OwnerFacts.Remove(operation);
                occurrence.Selections.Remove(operation);
            }
            else occurrence.Selections[operation] = MemberResolution.CombineRequired(facts.Values);
        }
        if (occurrence.Selections.Count == 0) _occurrences.Remove(name);
    }

    public MemberResolution GetResolution(SourceDocument? document, Token name,
        MemberOperation operations = MemberOperation.Read)
    {
        if (document is null || !_occurrences.TryGetValue(name, out var occurrence) ||
            !ReferenceEquals(occurrence.Document, document)) return MemberResolution.Unavailable;
        return MemberResolution.CombineRequired(AtomicOperations(operations).Select(operation =>
            occurrence.Selections.GetValueOrDefault(operation, MemberResolution.Unavailable)));
    }

    public FrozenMemberIndex Freeze()
    {
        if (!IsEnabled) return FrozenMemberIndex.Empty;
        var symbols = _symbols.Values.ToDictionary(symbol => symbol,
            symbol => new FrozenSourceMemberSymbol(symbol), (IEqualityComparer<SourceMemberSymbol>)ReferenceEqualityComparer.Instance);
        FrozenMemberResolution FreezeResolution(MemberResolution resolution) =>
            new(resolution.Candidates.Select(symbol => symbols[symbol]), resolution.IsComplete);
        var occurrences = _occurrences.Values.Select(occurrence =>
        {
            var selections = occurrence.Selections.ToFrozenDictionary(pair => pair.Key,
                pair => FreezeResolution(pair.Value));
            return new FrozenMemberOccurrence(occurrence.Document, occurrence.Name,
                occurrence.Selections.Keys.Aggregate(MemberOperation.None, (all, operation) => all | operation),
                occurrence.IsDeclaration, FreezeResolution(MemberResolution.CombineRequired(occurrence.Selections.Values)), selections, occurrence.Owner);
        });
        return new(Generation, symbols.Values, _classes.Values, occurrences, _classAliases);
    }

    private static bool Valid(SourceDocument document, Token name) =>
        name.Start >= 0 && name.End > name.Start && name.End <= document.Text.Length;

    internal static IEnumerable<MemberOperation> AtomicOperations(MemberOperation operations)
    {
        foreach (MemberOperation operation in new[] { MemberOperation.Declaration, MemberOperation.Read,
            MemberOperation.Write, MemberOperation.Call, MemberOperation.Presence })
            if ((operations & operation) != 0) yield return operation;
    }

    private readonly record struct MemberKey(int ClassId, MemberFacet Facet, string Name);
    private sealed class OccurrenceBuilder(SourceDocument document, Token name, object? owner)
    {
        public SourceDocument Document { get; } = document;
        public Token Name { get; } = name;
        public object? Owner { get; } = owner;
        public bool IsDeclaration { get; set; }
        public Dictionary<MemberOperation, MemberResolution> Selections { get; } = [];
        public Dictionary<MemberOperation, Dictionary<object, MemberResolution>> OwnerFacts { get; } = [];
    }
}

public sealed class FrozenSourceMemberSymbol
{
    internal FrozenSourceMemberSymbol(SourceMemberSymbol symbol)
    {
        Id = symbol.Id; Generation = symbol.Generation; DeclaringClassId = symbol.DeclaringClassId;
        Name = symbol.Name; Facet = symbol.Facet; Kind = symbol.Kind;
        Declarations = symbol.Declarations.ToImmutableArray();
    }
    public int Id { get; }
    public long Generation { get; }
    public int DeclaringClassId { get; }
    public string Name { get; }
    public MemberFacet Facet { get; }
    public SourceMemberKind Kind { get; }
    public IReadOnlyList<SourceMemberDeclaration> Declarations { get; }
    public bool CanRename => false;
}

public sealed class FrozenMemberResolution
{
    internal FrozenMemberResolution(IEnumerable<FrozenSourceMemberSymbol> candidates, bool isComplete)
    {
        Candidates = candidates.Distinct<FrozenSourceMemberSymbol>(ReferenceEqualityComparer.Instance)
            .OrderBy(symbol => symbol.Generation).ThenBy(symbol => symbol.Id).ToImmutableArray();
        IsComplete = isComplete && Candidates.Count != 0;
    }
    public static FrozenMemberResolution Unavailable { get; } = new([], false);
    public IReadOnlyList<FrozenSourceMemberSymbol> Candidates { get; }
    public bool IsComplete { get; }
    public bool IsResolved => IsComplete && Candidates.Count == 1;
    public bool IsAmbiguous => Candidates.Count > 1;
}

public sealed record FrozenMemberOccurrence(SourceDocument Document, Token Name, MemberOperation Operations,
    bool IsDeclaration, FrozenMemberResolution Resolution,
    IReadOnlyDictionary<MemberOperation, FrozenMemberResolution> Selections, object? Owner = null);

/// <summary>Query-only generation copy with no checker, resolver or callback references.</summary>
public sealed class FrozenMemberIndex
{
    private readonly FrozenDictionary<Token, FrozenMemberOccurrence> _tokens;
    private readonly FrozenDictionary<int, SourceClassInfo> _classes;
    private readonly FrozenDictionary<int, int> _classAliases;
    public static FrozenMemberIndex Empty { get; } = new(0, [], [], []);

    internal FrozenMemberIndex(long generation, IEnumerable<FrozenSourceMemberSymbol> symbols,
        IEnumerable<SourceClassInfo> classes, IEnumerable<FrozenMemberOccurrence> occurrences,
        IEnumerable<KeyValuePair<int, int>>? classAliases = null)
    {
        Generation = generation;
        Symbols = symbols.OrderBy(symbol => symbol.Id).ToImmutableArray();
        Classes = classes.OrderBy(@class => @class.DeclarationId).ToImmutableArray();
        Occurrences = occurrences.OrderBy(occurrence => occurrence.Document.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(occurrence => occurrence.Name.Start).ThenBy(occurrence => occurrence.Name.End).ToImmutableArray();
        _tokens = Occurrences.ToFrozenDictionary(occurrence => occurrence.Name,
            (IEqualityComparer<Token>)ReferenceEqualityComparer.Instance);
        _classes = Classes.ToFrozenDictionary(@class => @class.DeclarationId);
        _classAliases = (classAliases ?? Classes.Select(@class => KeyValuePair.Create(@class.DeclarationId, @class.DeclarationId)))
            .ToFrozenDictionary();
    }

    public long Generation { get; }
    public IReadOnlyList<FrozenSourceMemberSymbol> Symbols { get; }
    public IReadOnlyList<SourceClassInfo> Classes { get; }
    public IReadOnlyList<FrozenMemberOccurrence> Occurrences { get; }
    public int Count => Symbols.Count + Occurrences.Count;
    public long EstimatedBytes => Symbols.Sum(symbol => 96L + symbol.Name.Length * 2L + symbol.Declarations.Count * 64L) +
        Classes.Count * 80L + _classAliases.Count * 16L + Occurrences.Sum(occurrence => 128L + occurrence.Resolution.Candidates.Count * 8L +
            occurrence.Selections.Sum(selection => 48L + selection.Value.Candidates.Count * 8L));

    public SourceClassInfo? GetClassInfo(int declarationId) => _classAliases.TryGetValue(declarationId, out int canonicalId)
        ? _classes.GetValueOrDefault(canonicalId) : null;
    public bool IsSameSourceClass(int leftId, int rightId) =>
        _classAliases.TryGetValue(leftId, out int left) &&
        _classAliases.TryGetValue(rightId, out int right) && left == right;
    public FrozenMemberResolution GetResolution(Token name) =>
        _tokens.TryGetValue(name, out var occurrence) ? occurrence.Resolution : FrozenMemberResolution.Unavailable;

    public FrozenMemberResolution FindResolution(SourceDocument document, int offset)
    {
        var matches = Occurrences.Where(occurrence => ReferenceEquals(document, occurrence.Document) && occurrence.Name.Span.Contains(offset)).ToArray();
        if (matches.Length == 0) return FrozenMemberResolution.Unavailable;
        int length = matches.Min(occurrence => occurrence.Name.Span.Length);
        var narrowest = matches.Where(occurrence => occurrence.Name.Span.Length == length).ToArray();
        return new(narrowest.SelectMany(occurrence => occurrence.Resolution.Candidates),
            narrowest.All(occurrence => occurrence.Resolution.IsComplete));
    }

    public IReadOnlyList<FrozenSourceMemberSymbol> FindSymbols(SourceDocument document, int offset) =>
        FindResolution(document, offset).Candidates;

    public IReadOnlyList<SourceMemberDeclaration> FindDefinitions(SourceDocument document, int offset)
    {
        var resolution = FindResolution(document, offset);
        return resolution.IsResolved ? resolution.Candidates.SelectMany(symbol => symbol.Declarations)
            .OrderBy(declaration => declaration.Document.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(declaration => declaration.Name.Start).ToImmutableArray() : [];
    }

    /// <summary>Only unambiguous, fully supported occurrences count as known references.</summary>
    public IReadOnlyList<FrozenMemberOccurrence> GetOccurrences(FrozenSourceMemberSymbol symbol,
        bool includeDeclarations = true) => Symbols.Any(candidate => ReferenceEquals(candidate, symbol))
        ? Occurrences.Where(occurrence => occurrence.Resolution.IsResolved &&
            ReferenceEquals(occurrence.Resolution.Candidates[0], symbol) && (includeDeclarations || !occurrence.IsDeclaration)).ToImmutableArray()
        : ImmutableArray<FrozenMemberOccurrence>.Empty;

    public bool TryFindOccurrence(string documentPath, int offset, out FrozenMemberOccurrence? occurrence)
    {
        occurrence = Occurrences.Where(candidate => string.Equals(candidate.Document.Path, documentPath, StringComparison.OrdinalIgnoreCase) &&
            candidate.Name.Span.Contains(offset)).OrderBy(candidate => candidate.Name.Span.Length).FirstOrDefault();
        return occurrence is not null;
    }
}
