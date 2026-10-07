using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>Opt-in build facts. Publication projects types into bounded immutable values.</summary>
public sealed class EditorSemanticIndex
{
    private static long _nextGeneration;
    internal static EditorSemanticIndex Empty { get; } = new(false);
    private readonly Dictionary<OwnerKey, OccurrenceDraft> _occurrences = new(OwnerComparer.Instance);
    private readonly Dictionary<OwnerKey, OccurrenceDraft> _typeUses = new(OwnerComparer.Instance);
    private readonly Dictionary<OwnerKey, List<DeclarationDraft>> _declarations = new(OwnerComparer.Instance);
    private readonly Dictionary<OwnerKey, ReceiverDraft> _receivers = new(OwnerComparer.Instance);
    private readonly Dictionary<OwnerKey, InvocationDraft> _invocations = new(OwnerComparer.Instance);
    private readonly Dictionary<ScopeKey, ScopeDraft> _scopes = new(ScopeComparer.Instance);
    private readonly Dictionary<int, ScopeDraft> _scopesById = [];
    private readonly Dictionary<OwnerKey, List<SourceSpan>> _enteredBodies = new(OwnerComparer.Instance);
    private readonly Dictionary<TypeInfo, int> _signatureIds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeInfo, EditorCallableSurface> _callableSurfaces = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<OwnerKey, Dictionary<EditorAnnotationSlot, bool>> _annotationProofs = new(OwnerComparer.Instance);
    private int _nextScope = 1;

    public EditorSemanticIndex() : this(true) { }
    private EditorSemanticIndex(bool enabled) { IsEnabled = enabled; Generation = enabled ? Interlocked.Increment(ref _nextGeneration) : 0; }
    public bool IsEnabled { get; }
    public long Generation { get; private set; }

    public void Clear()
    {
        if (!IsEnabled) return;
        _occurrences.Clear(); _typeUses.Clear(); _declarations.Clear(); _receivers.Clear(); _invocations.Clear();
        _callableSurfaces.Clear();
        _annotationProofs.Clear();
        _scopes.Clear(); _scopesById.Clear(); _enteredBodies.Clear(); _signatureIds.Clear(); _nextScope = 1;
        Generation = Interlocked.Increment(ref _nextGeneration);
    }

    /// <summary>Discards one document's earlier pass without disturbing other captured inputs.</summary>
    public void ClearDocument(SourceDocument document)
    {
        if (!IsEnabled) return;
        foreach (var key in _occurrences.Keys.Where(key => ReferenceEquals(key.Document, document)).ToArray()) _occurrences.Remove(key);
        foreach (var key in _typeUses.Keys.Where(key => ReferenceEquals(key.Document, document)).ToArray()) _typeUses.Remove(key);
        foreach (var key in _declarations.Keys.Where(key => ReferenceEquals(key.Document, document)).ToArray()) _declarations.Remove(key);
        foreach (var key in _receivers.Keys.Where(key => ReferenceEquals(key.Document, document)).ToArray()) _receivers.Remove(key);
        foreach (var key in _invocations.Keys.Where(key => ReferenceEquals(key.Document, document)).ToArray()) _invocations.Remove(key);
        foreach (var key in _enteredBodies.Keys.Where(key => ReferenceEquals(key.Document, document)).ToArray()) _enteredBodies.Remove(key);
        foreach (var key in _scopes.Keys.Where(key => ReferenceEquals(key.Owner.Document, document)).ToArray())
        {
            _scopesById.Remove(_scopes[key].Id);
            _scopes.Remove(key);
        }
    }

    public void BeginExpression(SourceDocument? document, Expr owner)
    {
        if (!IsEnabled || document is null) return;
        var key = new OwnerKey(document, owner);
        _occurrences[key] = new(null, EditorFactAvailability.Unavailable);
        _receivers.Remove(key);
    }

    public void CompleteExpression(SourceDocument? document, Expr owner, TypeInfo? type, BindingSymbol? binding = null)
    {
        if (!IsEnabled || document is null) return;
        _occurrences[new(document, owner)] = new(type, type is null or TypeInfo.Inferred
            ? EditorFactAvailability.Unavailable : document.EditorSyntax?.GetRecords(owner).Any(record =>
                record.Origin == EditorSyntaxOrigin.Recovered) == true ? EditorFactAvailability.Recovered : EditorFactAvailability.Available, binding);
    }

    public void FailExpression(SourceDocument? document, Expr owner, bool preserveReceiver = false)
    {
        if (!IsEnabled || document is null) return;
        var key = new OwnerKey(document, owner);
        _occurrences[key] = new(null, EditorFactAvailability.Unavailable);
        if (!preserveReceiver) _receivers.Remove(key);
    }

    public void BeginTypeUse(SourceDocument? document, TypeNode owner)
    {
        if (IsEnabled && document is not null) _typeUses[new(document, owner)] = new(null, EditorFactAvailability.Unavailable);
    }

    public void CompleteTypeUse(SourceDocument? document, TypeNode owner, TypeInfo? type,
        EditorFactAvailability availability = EditorFactAvailability.Available)
    {
        if (IsEnabled && document is not null) _typeUses[new(document, owner)] = new(type,
            type is null or TypeInfo.Inferred ? EditorFactAvailability.Unavailable : availability);
    }

    public void RegisterCallableSurface(TypeInfo actual, IReadOnlyList<TypeInfo> signatures,
        IReadOnlyList<TypeInfo.TypeParameter>? typeParameters = null)
    {
        if (!IsEnabled) return;
        _callableSurfaces[actual] = new(signatures.Take(33).ToImmutableArray(), typeParameters?.Take(33).ToImmutableArray());
    }

    // Like public signature identity, eligibility can be learned while constructing the
    // exact type in an earlier pass. Only final source facts consume it at publication.
    public void RecordAnnotationProof(SourceDocument? document, object owner, EditorAnnotationSlot slot, bool isProven)
    {
        if (!IsEnabled || document is null) return;
        var key = new OwnerKey(document, owner);
        if (!_annotationProofs.TryGetValue(key, out var slots)) _annotationProofs.Add(key, slots = []);
        slots[slot] = isProven;
    }

    public void RecordDeclaration(SourceDocument? document, object owner, Token name, BindingSymbol? symbol,
        BindingNamespace facet, TypeInfo? type, EditorFactAvailability availability = EditorFactAvailability.Available)
    {
        if (!IsEnabled) return;
        var key = new OwnerKey(document, owner);
        if (!_declarations.TryGetValue(key, out var declarations)) _declarations.Add(key, declarations = []);
        declarations.RemoveAll(declaration => ReferenceEquals(declaration.Name, name) && declaration.Facet == facet);
        declarations.Add(new(name, symbol, facet, type, availability));
    }

    public int RegisterScope(SourceDocument? document, object owner, SourceSpan? span,
        int? parentScopeId = null, EditorScopeKind kind = EditorScopeKind.Block)
    {
        if (!IsEnabled) return 0;
        var key = new ScopeKey(new(document, owner), kind, span);
        if (!_scopes.TryGetValue(key, out var scope))
        {
            scope = new(_nextScope++, key.Owner, span, parentScopeId, kind);
            _scopes.Add(key, scope); _scopesById.Add(scope.Id, scope);
        }
        else { scope.Span = span; if (parentScopeId != scope.Id) scope.Parent = parentScopeId; scope.Kind = kind; }
        if (kind == EditorScopeKind.Block && span is { } entered) MarkBodyEntered(document, owner, entered);
        return scope.Id;
    }

    public void MarkBodyEntered(SourceDocument? document, object owner, SourceSpan span)
    {
        if (!IsEnabled || document is null || span.IsHidden || span.IsEmpty || span.End > document.Text.Length) return;
        var key = new OwnerKey(document, owner);
        if (!_enteredBodies.TryGetValue(key, out var spans)) _enteredBodies.Add(key, spans = []);
        if (!spans.Contains(span)) spans.Add(span);
    }

    public void ClearBodyEntered(SourceDocument? document, object owner)
    {
        if (IsEnabled && document is not null) _enteredBodies.Remove(new(document, owner));
    }

    public void BindLocal(int scopeId, string spelling, BindingNamespace facet, BindingSymbol? symbol,
        TypeInfo? type, object? declarationOwner = null, int availableFrom = 0,
        EditorFactAvailability availability = EditorFactAvailability.Available)
    {
        if (!IsEnabled || !_scopesById.TryGetValue(scopeId, out var scope)) return;
        scope.Bindings[(spelling, facet)] = new(spelling, facet, symbol, type, declarationOwner, availableFrom, availability);
    }

    public void SetReceiverMembers(SourceDocument? document, Expr receiver,
        IReadOnlyList<EditorReceiverCandidate> candidates, bool isComplete = true, bool isTruncated = false,
        TypeInfo? receiverType = null)
    {
        if (!IsEnabled || document is null) return;
        var copied = candidates.Take(256).Select(candidate => candidate with
        {
            Substitutions = candidate.Substitutions?.ToFrozenDictionary(StringComparer.Ordinal),
        }).ToImmutableArray();
        bool truncated = isTruncated || candidates.Count > 256;
        _receivers[new(document, receiver)] = new(copied, isComplete && !truncated, truncated, receiverType);
    }

    public void BeginInvocation(SourceDocument? document, Expr owner, EditorInvocationKind kind,
        bool isRecovered = false, bool hasHoles = false)
    {
        if (!IsEnabled || document is null) return;
        _invocations[new(document, owner)] = new(kind, [], null, null, null,
            EditorInvocationStatus.Unavailable, false, isRecovered, hasHoles);
    }

    public void RecordInvocation(SourceDocument? document, Expr owner, EditorInvocationKind kind,
        IReadOnlyList<EditorInvocationCandidate> candidates, int? selectedOrdinal = null,
        TypeInfo? selectedSignature = null, EditorInvocationStatus status = EditorInvocationStatus.CandidatesOnly,
        TypeInfo? resultType = null, bool isComplete = true, bool isRecovered = false, bool hasHoles = false)
    {
        if (!IsEnabled || document is null) return;
        var copied = CopyCandidates(candidates);
        bool uniqueOrdinals = copied.Count == Math.Min(candidates.Count, 32);
        bool selected = status == EditorInvocationStatus.Selected && selectedOrdinal is { } ordinal &&
            isComplete && candidates.Count <= 32 && uniqueOrdinals && copied.Any(candidate => candidate.Ordinal == ordinal) &&
            selectedSignature is not null && !isRecovered && !hasHoles;
        _invocations[new(document, owner)] = new(kind, copied, selected ? selectedOrdinal : null,
            selected ? selectedSignature : null, selected ? resultType : null,
            selected ? EditorInvocationStatus.Selected : copied.Count > 0 ? EditorInvocationStatus.CandidatesOnly : EditorInvocationStatus.Unavailable,
            isComplete && uniqueOrdinals && candidates.Count <= 32, isRecovered, hasHoles);
    }

    public void RecordInvocationCandidates(SourceDocument? document, Expr owner,
        IReadOnlyList<EditorInvocationCandidate> candidates, bool isComplete = true)
    {
        if (!IsEnabled || document is null || !_invocations.TryGetValue(new(document, owner), out var invocation)) return;
        RecordInvocation(document, owner, invocation.Kind, candidates, isComplete: isComplete,
            isRecovered: invocation.Recovered, hasHoles: invocation.Holes);
    }

    public void RecordInvocationInstantiation(SourceDocument? document, Expr owner, int ordinal, TypeInfo? instantiatedSignature)
    {
        if (!IsEnabled || document is null || !_invocations.TryGetValue(new(document, owner), out var invocation)) return;
        var candidates = invocation.Candidates.Select(candidate => candidate.Ordinal == ordinal
            ? candidate with { InstantiatedSignature = instantiatedSignature } : candidate).ToImmutableArray();
        _invocations[new(document, owner)] = invocation with { Candidates = candidates };
    }

    public void SelectInvocation(SourceDocument? document, Expr owner, int selectedOrdinal,
        TypeInfo selectedSignature, TypeInfo? resultType = null)
    {
        if (!IsEnabled || document is null || !_invocations.TryGetValue(new(document, owner), out var invocation)) return;
        RecordInvocation(document, owner, invocation.Kind, invocation.Candidates, selectedOrdinal, selectedSignature,
            EditorInvocationStatus.Selected, resultType, invocation.Complete, invocation.Recovered, invocation.Holes);
    }

    public void FailInvocation(SourceDocument? document, Expr owner)
    {
        if (!IsEnabled || document is null || !_invocations.TryGetValue(new(document, owner), out var invocation)) return;
        _invocations[new(document, owner)] = invocation with { SelectedOrdinal = null, SelectedSignature = null,
            Result = null, Status = invocation.Candidates.Count > 0 ? EditorInvocationStatus.CandidatesOnly : EditorInvocationStatus.Unavailable };
    }

    private IReadOnlyList<EditorInvocationCandidate> CopyCandidates(IReadOnlyList<EditorInvocationCandidate> candidates)
    {
        var copied = candidates.Take(32).GroupBy(candidate => candidate.Ordinal).Select(group => group.First())
            // The extra element preserves the renderer's explicit truncation marker while
            // bounding copies of shared generic-overload type parameter lists.
            .Select(candidate => candidate with { TypeParameters = candidate.TypeParameters?.Take(33).ToImmutableArray() }).ToImmutableArray();
        foreach (var candidate in copied)
            if (!_signatureIds.ContainsKey(candidate.OriginalSignature)) _signatureIds.Add(candidate.OriginalSignature, _signatureIds.Count + 1);
        return copied;
    }

    public FrozenEditorSemanticIndex Freeze(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return FrozenEditorSemanticIndex.Empty;
        var ownerProof = new Dictionary<OwnerKey, bool>(OwnerComparer.Instance);
        bool UnprovenOwner(OwnerKey key)
        {
            if (ownerProof.TryGetValue(key, out bool unproven)) return unproven;
            cancellationToken.ThrowIfCancellationRequested();
            unproven = _annotationProofs.TryGetValue(key, out var slots) && slots.Values.Any(proven => !proven);
            IEnumerable<object> signatureParts = key.Owner switch
            {
                Stmt.Function function => function.Parameters.Cast<object>().Concat(function.TypeParams ?? []),
                Expr.ArrowFunction function => function.Parameters.Cast<object>().Concat(function.TypeParams ?? []),
                Stmt.Accessor { SetterParam: { } parameter } => [parameter],
                Stmt.Var { TypeAnnotation: null, Initializer: { } initializer } => [initializer],
                Stmt.Const { TypeAnnotation: null } constant => [constant.Initializer],
                Stmt.Field { TypeAnnotation: null, Initializer: { } initializer } => [initializer],
                Expr.Grouping grouping => [grouping.Expression],
                Expr.NonNullAssertion assertion => [assertion.Expression],
                _ => [],
            };
            if (!unproven)
                foreach (var part in signatureParts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (UnprovenOwner(new(key.Document, part))) { unproven = true; break; }
                }
            ownerProof[key] = unproven;
            return unproven;
        }
        var unprovenBindings = new HashSet<BindingSymbol>(ReferenceEqualityComparer.Instance);
        foreach (var (key, drafts) in _declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UnprovenOwner(key))
                foreach (var draft in drafts)
                    if (draft.Symbol is { } symbol) unprovenBindings.Add(symbol);
        }
        var bindings = new Dictionary<BindingSymbol, EditorBindingIdentity>(ReferenceEqualityComparer.Instance);
        EditorBindingIdentity? Binding(BindingSymbol? symbol)
        {
            if (symbol is null) return null;
            if (!bindings.TryGetValue(symbol, out var copied)) bindings.Add(symbol, copied = new(symbol.Id,
                symbol.Generation, symbol.Name, symbol.Namespace, symbol.Declarations.ToImmutableArray(), symbol.RenameEligibility));
            return copied;
        }
        var presentations = new Dictionary<TypeInfo, EditorTypePresentation>(ReferenceEqualityComparer.Instance);
        EditorTypePresentation Type(TypeInfo? type, BindingNamespace facet = BindingNamespace.Type)
        {
            if (type is null) return new("unavailable", false, false);
            if (facet == BindingNamespace.Value) return EditorTypeRenderer.Render(type, EditorTypeRenderContext.Value, callableSurfaces: _callableSurfaces);
            if (!presentations.TryGetValue(type, out var presentation)) presentations.Add(type, presentation = EditorTypeRenderer.Render(type, callableSurfaces: _callableSurfaces));
            return presentation;
        }
        static EditorFactAvailability Availability(EditorFactAvailability requested, EditorTypePresentation type) =>
            type.IsAvailable ? requested : EditorFactAvailability.Unavailable;
        var occurrences = new List<EditorOccurrenceFact>();
        foreach (var (key, draft) in _occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = Type(draft.Type, BindingNamespace.Value);
            occurrences.Add(new(key.Document!, (Expr)key.Owner, SourceRange(key.Document!, key.Owner), type,
                Availability(UnprovenOwner(key) || draft.Binding is { } binding && unprovenBindings.Contains(binding)
                    ? EditorFactAvailability.Unavailable : draft.Availability, type)));
        }
        var typeUses = new List<EditorTypeUseFact>();
        foreach (var (key, draft) in _typeUses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = Type(draft.Type);
            typeUses.Add(new(key.Document!, (TypeNode)key.Owner, TypeSourceRange(key.Document!, key.Owner), type, Availability(draft.Availability, type)));
        }
        var declarations = new List<EditorDeclarationFact>();
        foreach (var (key, drafts) in _declarations)
            foreach (var draft in drafts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var type = Type(draft.Type, draft.Facet);
                declarations.Add(new(new(key.Document, key.Owner, draft.Name), draft.Name.Lexeme, draft.Facet,
                    Binding(draft.Symbol), type, Availability(UnprovenOwner(key) || draft.Symbol is { } symbol && unprovenBindings.Contains(symbol)
                        ? EditorFactAvailability.Unavailable : draft.Availability, type)));
            }
        var scopes = new List<EditorSourceScope>();
        foreach (var draft in _scopes.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locals = draft.Bindings.Values.Select(local =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var type = Type(local.Type, local.Facet);
                return new EditorVisibleBinding(local.Spelling, local.Facet, Binding(local.Symbol), type,
                    local.Owner is TypeEnvironment ? null : local.Owner, local.AvailableFrom,
                    Availability(local.Owner is { } owner && UnprovenOwner(new(draft.Key.Document, owner)) ||
                        local.Symbol is { } symbol && unprovenBindings.Contains(symbol) ? EditorFactAvailability.Unavailable : local.Availability, type));
            }).ToImmutableArray();
            scopes.Add(new(draft.Id, draft.Key.Document, draft.Key.Owner is TypeEnvironment ? new object() : draft.Key.Owner,
                draft.Span, draft.Parent, draft.Kind, locals));
        }
        var receivers = new Dictionary<OwnerKey, EditorReceiverSet>(OwnerComparer.Instance);
        foreach (var (key, draft) in _receivers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            receivers.Add(key, new(draft.Candidates.Select(candidate =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool unproven = UnprovenOwner(key) || candidate.Source?.Declarations.Any(declaration => declaration.Owner is { } owner &&
                    UnprovenOwner(new(declaration.Document, owner))) == true;
                return new EditorReceiverMember(candidate.Name,
                EditorTypeRenderer.Render(unproven ? null : candidate.Type, EditorTypeRenderContext.Value, substitutions: candidate.Substitutions,
                    callableSurfaces: _callableSurfaces), candidate.Kind,
                candidate.Access, candidate.Facet, candidate.IsReadonly, candidate.IsOptional,
                candidate.Source is { } source ? new(source.Id, source.Generation, source.DeclaringClassId, source.Declarations.ToImmutableArray()) : null,
                candidate.NamespaceFacet);
            })
                .ToImmutableArray(), draft.Complete, draft.Truncated, draft.ReceiverType is null ? null : Type(draft.ReceiverType)));
        }
        var invocations = new List<EditorInvocationFact>();
        foreach (var (key, draft) in _invocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = draft.Candidates.Select(candidate =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new EditorInvocationSignature(candidate.Ordinal,
                _signatureIds[candidate.OriginalSignature], EditorTypeRenderer.RenderSignature(candidate.OriginalSignature,
                    draft.Kind == EditorInvocationKind.New, candidate.ConstructedType, candidate.TypeParameters,
                    callableSurfaces: _callableSurfaces),
                candidate.InstantiatedSignature is null ? null : EditorTypeRenderer.RenderSignature(candidate.InstantiatedSignature,
                    draft.Kind == EditorInvocationKind.New, callableSurfaces: _callableSurfaces), candidate.Origin,
                candidate.ConstructedType is null ? null : Type(candidate.ConstructedType), candidate.IsImplicit);
            }).ToImmutableArray();
            invocations.Add(new(key.Document!, (Expr)key.Owner, draft.Kind, candidates, draft.SelectedOrdinal,
                draft.SelectedSignature is null ? null : EditorTypeRenderer.RenderSignature(draft.SelectedSignature,
                    draft.Kind == EditorInvocationKind.New, draft.Kind == EditorInvocationKind.New ? draft.Result : null,
                    callableSurfaces: _callableSurfaces), draft.Result is null ? null : Type(draft.Result),
                draft.Status, draft.Complete, draft.Recovered, draft.Holes));
        }
        var bodies = _enteredBodies.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<SourceSpan>)pair.Value.ToImmutableArray(), OwnerComparer.Instance);
        return new(Generation, occurrences, declarations, scopes, receivers, invocations, bodies, cancellationToken, typeUses);
    }

    internal static SourceSpan? SourceRange(SourceDocument document, object owner) => document.EditorSyntax?
        .GetRecords(owner).Where(record => record.IsAuthoritative && record.Role == EditorSyntaxRole.Whole)
        .OrderBy(record => record.Span.Length).Select(record => (SourceSpan?)record.Span).FirstOrDefault();

    private static SourceSpan? TypeSourceRange(SourceDocument document, object owner) => document.EditorSyntax?
        .GetRecords(owner).Where(record => record.IsAuthoritative && record.Kind == EditorSyntaxKind.Type)
        .OrderBy(record => record.Span.Length).Select(record => (SourceSpan?)record.Span).FirstOrDefault();

    internal readonly record struct OwnerKey(SourceDocument? Document, object Owner);
    private readonly record struct ScopeKey(OwnerKey Owner, EditorScopeKind Kind, SourceSpan? Span);
    private sealed class ScopeComparer : IEqualityComparer<ScopeKey>
    {
        public static ScopeComparer Instance { get; } = new();
        public bool Equals(ScopeKey left, ScopeKey right) => OwnerComparer.Instance.Equals(left.Owner, right.Owner) && left.Kind == right.Kind && left.Span == right.Span;
        public int GetHashCode(ScopeKey key) => HashCode.Combine(OwnerComparer.Instance.GetHashCode(key.Owner), key.Kind, key.Span);
    }
    internal sealed class OwnerComparer : IEqualityComparer<OwnerKey>
    {
        public static OwnerComparer Instance { get; } = new();
        public bool Equals(OwnerKey left, OwnerKey right) => ReferenceEquals(left.Document, right.Document) && ReferenceEquals(left.Owner, right.Owner);
        public int GetHashCode(OwnerKey key) => HashCode.Combine(key.Document is null ? 0 : RuntimeHelpers.GetHashCode(key.Document), RuntimeHelpers.GetHashCode(key.Owner));
    }
    private sealed record OccurrenceDraft(TypeInfo? Type, EditorFactAvailability Availability, BindingSymbol? Binding = null);
    private sealed record DeclarationDraft(Token Name, BindingSymbol? Symbol, BindingNamespace Facet, TypeInfo? Type, EditorFactAvailability Availability);
    private sealed record LocalDraft(string Spelling, BindingNamespace Facet, BindingSymbol? Symbol, TypeInfo? Type, object? Owner, int AvailableFrom, EditorFactAvailability Availability);
    private sealed record ReceiverDraft(IReadOnlyList<EditorReceiverCandidate> Candidates, bool Complete, bool Truncated, TypeInfo? ReceiverType);
    private sealed record InvocationDraft(EditorInvocationKind Kind, IReadOnlyList<EditorInvocationCandidate> Candidates,
        int? SelectedOrdinal, TypeInfo? SelectedSignature, TypeInfo? Result, EditorInvocationStatus Status, bool Complete, bool Recovered, bool Holes);
    private sealed class ScopeDraft(int id, OwnerKey key, SourceSpan? span, int? parent, EditorScopeKind kind)
    {
        public int Id { get; } = id;
        public OwnerKey Key { get; } = key;
        public SourceSpan? Span { get; set; } = span;
        public int? Parent { get; set; } = parent;
        public EditorScopeKind Kind { get; set; } = kind;
        public Dictionary<(string, BindingNamespace), LocalDraft> Bindings { get; } = [];
    }
}

/// <summary>Completed query values; no raw type, checker, resolver or environment is retained.</summary>
public sealed class FrozenEditorSemanticIndex
{
    private readonly FrozenDictionary<EditorSemanticIndex.OwnerKey, EditorOccurrenceFact> _occurrences;
    private readonly FrozenDictionary<EditorSemanticIndex.OwnerKey, EditorTypeUseFact> _typeUses;
    private readonly FrozenDictionary<EditorSemanticIndex.OwnerKey, EditorReceiverSet> _receivers;
    private readonly FrozenDictionary<EditorSemanticIndex.OwnerKey, EditorInvocationFact> _invocations;
    private readonly FrozenDictionary<int, EditorSourceScope> _scopes;
    private readonly FrozenDictionary<EditorSemanticIndex.OwnerKey, IReadOnlyList<SourceSpan>> _enteredBodies;
    private readonly FrozenDictionary<EditorSemanticIndex.OwnerKey, IReadOnlyList<EditorSourceScope>> _ownerScopes;
    private readonly FrozenDictionary<SourceDocument, IReadOnlyList<EditorSyntaxRecord>> _lexicalContexts;
    private readonly FrozenSet<int> _unvisitedLocalScopes;
    public static FrozenEditorSemanticIndex Empty { get; } = new(0, [], [], [],
        new Dictionary<EditorSemanticIndex.OwnerKey, EditorReceiverSet>(), []);

    internal FrozenEditorSemanticIndex(long generation, IEnumerable<EditorOccurrenceFact> occurrences,
        IEnumerable<EditorDeclarationFact> declarations, IEnumerable<EditorSourceScope> scopes,
        IReadOnlyDictionary<EditorSemanticIndex.OwnerKey, EditorReceiverSet> receivers,
        IEnumerable<EditorInvocationFact> invocations,
        IReadOnlyDictionary<EditorSemanticIndex.OwnerKey, IReadOnlyList<SourceSpan>>? enteredBodies = null,
        CancellationToken cancellationToken = default, IEnumerable<EditorTypeUseFact>? typeUses = null)
    {
        Generation = generation;
        Occurrences = occurrences.ToImmutableArray(); Declarations = declarations.ToImmutableArray();
        TypeUses = typeUses?.ToImmutableArray() ?? [];
        Scopes = scopes.ToImmutableArray(); Invocations = invocations.ToImmutableArray();
        _occurrences = Occurrences.ToFrozenDictionary(fact => new EditorSemanticIndex.OwnerKey(fact.Document, fact.Owner), EditorSemanticIndex.OwnerComparer.Instance);
        _typeUses = TypeUses.ToFrozenDictionary(fact => new EditorSemanticIndex.OwnerKey(fact.Document, fact.Owner), EditorSemanticIndex.OwnerComparer.Instance);
        _receivers = receivers.ToFrozenDictionary(EditorSemanticIndex.OwnerComparer.Instance);
        _invocations = Invocations.ToFrozenDictionary(fact => new EditorSemanticIndex.OwnerKey(fact.Document, fact.Owner), EditorSemanticIndex.OwnerComparer.Instance);
        _scopes = Scopes.ToFrozenDictionary(scope => scope.Id);
        _enteredBodies = (enteredBodies ?? new Dictionary<EditorSemanticIndex.OwnerKey, IReadOnlyList<SourceSpan>>())
            .ToFrozenDictionary(EditorSemanticIndex.OwnerComparer.Instance);
        _ownerScopes = Scopes.GroupBy(scope => new EditorSemanticIndex.OwnerKey(scope.Document, scope.Owner),
                EditorSemanticIndex.OwnerComparer.Instance)
            .ToFrozenDictionary(group => group.Key, group => (IReadOnlyList<EditorSourceScope>)group.ToImmutableArray(),
                EditorSemanticIndex.OwnerComparer.Instance);
        _lexicalContexts = Scopes.Select(scope => scope.Document).OfType<SourceDocument>()
            .Distinct<SourceDocument>(ReferenceEqualityComparer.Instance)
            .ToFrozenDictionary(document => document, BuildLexicalContexts,
                (IEqualityComparer<SourceDocument>)ReferenceEqualityComparer.Instance);
        _unvisitedLocalScopes = FindUnvisitedLocalScopes(cancellationToken).ToFrozenSet();
    }
    public long Generation { get; }
    public IReadOnlyList<EditorOccurrenceFact> Occurrences { get; }
    public IReadOnlyList<EditorTypeUseFact> TypeUses { get; }
    public IReadOnlyList<EditorDeclarationFact> Declarations { get; }
    public IReadOnlyList<EditorSourceScope> Scopes { get; }
    public IReadOnlyList<EditorInvocationFact> Invocations { get; }
    public int Count => Occurrences.Count + TypeUses.Count + Declarations.Count + Scopes.Count + Invocations.Count +
        _receivers.Values.Sum(set => set.Members.Count) + _enteredBodies.Values.Sum(spans => spans.Count);
    public long EstimatedBytes => Occurrences.Sum(fact => 96L + fact.Type.Text.Length * 2L) +
        TypeUses.Sum(fact => 96L + fact.Type.Text.Length * 2L) +
        Declarations.Sum(fact => 144L + (fact.LocalName.Length + fact.Type.Text.Length) * 2L) +
        Scopes.Sum(scope => 96L + scope.Bindings.Sum(local => 128L + (local.LocalName.Length + local.Type.Text.Length) * 2L)) +
        _receivers.Values.Sum(set => 64L + (set.ReceiverType?.Text.Length ?? 0) * 2L + set.Members.Sum(member => 128L + (member.Name.Length + member.Type.Text.Length) * 2L)) +
        Invocations.Sum(fact => 128L + fact.Candidates.Sum(candidate => 192L + candidate.Declared.Label.Length * 2L + (candidate.Instantiated?.Label.Length ?? 0) * 2L)) +
        _enteredBodies.Values.Sum(spans => 56L + spans.Count * 8L) +
        _ownerScopes.Values.Sum(scopes => 56L + scopes.Count * 8L) +
        _lexicalContexts.Values.Sum(contexts => 56L + contexts.Count * 8L) + _unvisitedLocalScopes.Count * 16L;

    public EditorOccurrenceFact? GetOccurrence(SourceDocument document, Expr owner) => _occurrences.GetValueOrDefault(new(document, owner));
    public EditorTypeUseFact? GetTypeUse(SourceDocument document, TypeNode owner) => _typeUses.GetValueOrDefault(new(document, owner));
    public EditorTypeUseFact? FindTypeUse(SourceDocument document, int offset) => TypeUses
        .Where(fact => ReferenceEquals(fact.Document, document) && fact.Span?.Contains(offset) == true)
        .OrderBy(fact => fact.Span!.Value.Length).ThenByDescending(fact => fact.Span!.Value.Start).FirstOrDefault();
    public EditorOccurrenceFact? FindOccurrence(SourceDocument document, int offset) => Occurrences
        .Where(fact => ReferenceEquals(fact.Document, document) && fact.Span?.Contains(offset) == true)
        .OrderBy(fact => fact.Span!.Value.Length).ThenByDescending(fact => fact.Span!.Value.Start).FirstOrDefault();
    public IReadOnlyList<EditorDeclarationFact> GetDeclarations(SourceDocument? document, object owner) => Declarations
        .Where(fact => ReferenceEquals(fact.Source.Document, document) && ReferenceEquals(fact.Source.Owner, owner)).ToImmutableArray();
    public IReadOnlyList<EditorDeclarationFact> FindDeclarations(SourceDocument document, int offset) => Declarations
        .Where(fact => ReferenceEquals(fact.Source.Document, document) && fact.Source.Name?.Span.Contains(offset) == true).ToImmutableArray();
    public EditorReceiverSet GetReceiverMembers(SourceDocument document, Expr owner) => _receivers.GetValueOrDefault(new(document, owner), EditorReceiverSet.Unavailable);
    public EditorInvocationFact? GetInvocation(SourceDocument document, Expr owner) => _invocations.GetValueOrDefault(new(document, owner));
    public EditorInvocationFact? FindInvocation(SourceDocument document, int offset) => document.EditorSyntax?.FindInvocation(offset) is { } syntax
        ? GetInvocation(document, syntax.Owner) : null;

    public IReadOnlyList<EditorVisibleBinding> GetVisibleBindings(SourceDocument document, int offset)
    {
        var scope = Scopes.Where(scope => ReferenceEquals(scope.Document, document) && scope.Span is { } span &&
                (span.Contains(offset) || scope.Kind == EditorScopeKind.Source && offset == document.Text.Length && span.End == offset))
            .OrderBy(scope => scope.Span!.Value.Length).ThenByDescending(scope => scope.Id).FirstOrDefault();
        if (scope is null || !HasCheckedSourceContext(document, offset) || _unvisitedLocalScopes.Contains(scope.Id)) return [];
        var visible = new Dictionary<(string, BindingNamespace), EditorVisibleBinding>();
        var visited = new HashSet<int>();
        while (scope is not null && visited.Add(scope.Id))
        {
            if (_unvisitedLocalScopes.Contains(scope.Id)) return [];
            foreach (var binding in scope.Bindings)
                visible.TryAdd((binding.LocalName, binding.Facet), offset < binding.AvailableFrom
                    ? binding with { Availability = EditorFactAvailability.Unavailable } : binding);
            scope = scope.ParentScopeId is { } parent ? _scopes.GetValueOrDefault(parent) : null;
        }
        return visible.Values.OrderBy(binding => binding.LocalName, StringComparer.Ordinal).ThenBy(binding => binding.Facet).ToImmutableArray();
    }

    private bool HasCheckedSourceContext(SourceDocument document, int offset)
    {
        if (!_lexicalContexts.TryGetValue(document, out var contexts)) return true;
        foreach (var context in contexts.Where(record => record.Span.Contains(offset)))
        {
            bool registered = _ownerScopes.TryGetValue(new(document, context.Node), out var scopes) &&
                scopes.Any(scope => scope.Span?.Contains(offset) == true);
            if (!registered) return false;
            if (context.Role == EditorSyntaxRole.Body &&
                (!_enteredBodies.TryGetValue(new(document, context.Node), out var entered) ||
                 !entered.Any(span => span.Start <= context.Span.Start && span.End >= context.Span.End))) return false;
        }
        return true;
    }

    private IEnumerable<int> FindUnvisitedLocalScopes(CancellationToken cancellationToken)
    {
        foreach (var (document, contexts) in _lexicalContexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document.EditorSyntax is not { } syntax) continue;
            foreach (var declaration in syntax.Records.Where(record => record.IsAuthoritative &&
                record.Kind == EditorSyntaxKind.Name && record.Node is Stmt.Var or Stmt.Const &&
                record.Role is EditorSyntaxRole.Name or EditorSyntaxRole.DeclarationName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                object context = contexts.Where(record => record.Span.Contains(declaration.Span.Start))
                    .OrderBy(record => record.Span.Length).Select(record => record.Node).FirstOrDefault() ?? document;
                if (!_ownerScopes.TryGetValue(new(document, context), out var scopes)) continue;
                foreach (var scope in scopes.Where(scope => scope.Span?.Contains(declaration.Span.Start) == true))
                {
                    var visited = new HashSet<int>();
                    EditorSourceScope? candidate = scope;
                    bool found = false;
                    while (candidate is not null && visited.Add(candidate.Id))
                    {
                        if (candidate.Bindings.Any(binding => ReferenceEquals(binding.DeclarationOwner, declaration.Node))) { found = true; break; }
                        candidate = candidate.ParentScopeId is { } parent ? _scopes.GetValueOrDefault(parent) : null;
                    }
                    if (!found) yield return scope.Id;
                }
            }
        }
    }

    private IReadOnlyList<EditorSyntaxRecord> BuildLexicalContexts(SourceDocument document)
    {
        var contexts = document.EditorSyntax?.Records.Where(IsLexicalContext).ToList() ?? [];
        // Namespace provenance currently carries its declaration name and ordinary parser span.
        // Only an actually registered namespace scope may add a context anchor here; this does
        // not create a scope or infer any binding from namespace syntax.
        contexts.AddRange(Scopes.Where(scope => ReferenceEquals(scope.Document, document) &&
                scope.Owner is Stmt.Namespace && scope.Span is not null)
            .Select(scope => new EditorSyntaxRecord(scope.Owner, EditorSyntaxKind.Block, scope.Span!.Value,
                Origin: EditorSyntaxOrigin.SourceEquivalent)));
        return contexts.ToImmutableArray();
    }

    private static bool IsLexicalContext(EditorSyntaxRecord record) => record.IsAuthoritative &&
        record.Kind is EditorSyntaxKind.Function or EditorSyntaxKind.Class or EditorSyntaxKind.Block &&
        record.Role is EditorSyntaxRole.Whole or EditorSyntaxRole.Body;
}
