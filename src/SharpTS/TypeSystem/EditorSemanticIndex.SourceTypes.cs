using System.Collections.Immutable;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>A source-backed checked type display; unsupported observations retain their exact source span.</summary>
public sealed record CheckedSourceTypeObservation(SourceSpan Span, CheckedTypeDisplayResult Display);

public sealed partial class EditorSemanticIndex
{
    // Alias expansions are captured only when semantic checking already requests them.
    // Like signature proofs, they may survive a discarded source pass because the exact
    // resolved type can be reused by the checker's alias cache in the authoritative pass.
    private readonly Dictionary<TypeNode, OccurrenceDraft> _resolvedAliasDefinitions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<OwnerKey, OccurrenceDraft> _memberReadTypes = new(OwnerComparer.Instance);

    internal void RecordMemberReadType(SourceDocument? document, Expr owner, TypeInfo type)
    {
        if (IsEnabled && document is not null)
            _memberReadTypes[new(document, owner)] = new(type, EditorFactAvailability.Available);
    }

    internal void RecordResolvedAliasDefinition(TypeNode? definition, TypeInfo type, bool isProven)
    {
        if (IsEnabled && definition is not null)
            _resolvedAliasDefinitions[definition] = new(isProven ? type : null,
                isProven ? EditorFactAvailability.Available : EditorFactAvailability.Unavailable);
    }

    /// <summary>
    /// Copies checked expression, declaration-name and member-name displays for one exact source document.
    /// Call after checking with editor metadata enabled. The result contains immutable display values,
    /// never live checker types or environments, and remains valid when this index is cleared.
    /// Synthetic/recovered syntax is excluded; missing semantic facts remain explicit unsupported observations.
    /// Queries must not run concurrently with a checker mutating this index.
    /// </summary>
    public IReadOnlyList<CheckedSourceTypeObservation> GetSourceTypeObservations(SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsEnabled || document.EditorSyntax is not { } syntax) return [];
        var observations = new Dictionary<SourceSpan, (int Priority, CheckedTypeDisplayResult Display)>();
        var publicCallables = new Dictionary<BindingSymbol, TypeInfo>(ReferenceEqualityComparer.Instance);
        foreach (var declaration in _declarations.Where(pair => ReferenceEquals(pair.Key.Document, document))
                     .SelectMany(pair => pair.Value))
            if (declaration.Symbol is { } symbol && declaration.Type is { } type &&
                declaration.Availability == EditorFactAvailability.Available &&
                _callableSurfaces.TryGetValue(type, out var surface) && surface.Signatures.Count > 1)
                publicCallables[symbol] = type;
        foreach (var (key, fact) in _occurrences)
        {
            if (!ReferenceEquals(key.Document, document) || key.Owner is not Expr expression ||
                expression is Expr.Literal { Value: null }) continue;
            foreach (var view in syntax.GetRecords(expression).Where(view => view.IsAuthoritative &&
                view.Role == EditorSyntaxRole.Whole && view.Kind is EditorSyntaxKind.Expression or EditorSyntaxKind.Literal or EditorSyntaxKind.Grouping))
                Add(view.Span, fact.Type, fact.Availability, key, 10);
        }
        foreach (var member in syntax.Members.Where(member => !member.IsRecovered))
        {
            var key = new OwnerKey(document, member.Owner);
            bool separateRead = _memberReadTypes.TryGetValue(key, out var read);
            if (separateRead || _occurrences.TryGetValue(key, out read))
            {
                Add(member.Name.Span, read!.Type, read.Availability, key, 15);
                if (separateRead)
                    foreach (var view in syntax.GetRecords(member.Owner).Where(view => view.IsAuthoritative &&
                                 view.Kind == EditorSyntaxKind.MemberAccess && view.Role == EditorSyntaxRole.Whole))
                        Add(view.Span, read.Type, read.Availability, key, 15);
            }
        }
        foreach (var (key, declarations) in _declarations)
        {
            if (!ReferenceEquals(key.Document, document) || key.Owner is TypeParam or Stmt.Interface) continue;
            foreach (var declaration in declarations)
            {
                if (!syntax.GetRecords(key.Owner).Any(view => view.IsAuthoritative &&
                    view.Span == declaration.Name.Span)) continue;
                var alias = key.Owner is Stmt.TypeAlias { TypeDefinitionNode: { } body } &&
                    _resolvedAliasDefinitions.TryGetValue(body, out var resolved) ? resolved : null;
                var selectedType = alias?.Type ?? declaration.Type;
                if (key.Owner is Stmt.Function && declaration.Symbol is { } symbol &&
                    publicCallables.TryGetValue(symbol, out var publicCallable))
                    selectedType = publicCallable;
                // TypeWriter queries a class declaration's instance name, while an
                // expression that references its constructor retains the value facet.
                if (key.Owner is Stmt.Class)
                    selectedType = selectedType switch
                    {
                        TypeInfo.GenericClass generic => new TypeInfo.InstantiatedGeneric(generic, generic.TypeParams.Cast<TypeInfo>().ToList()),
                        TypeInfo.Class or TypeInfo.MutableClass => new TypeInfo.Instance(selectedType),
                        _ => selectedType,
                    };
                Add(declaration.Name.Span, selectedType, alias?.Availability ?? declaration.Availability, key,
                    declaration.Facet == BindingNamespace.Value ? 30 : 20);
            }
        }
        return observations.OrderBy(pair => pair.Key.Start).ThenByDescending(pair => pair.Key.Length)
            .Select(pair => new CheckedSourceTypeObservation(pair.Key, pair.Value.Display)).ToImmutableArray();

        void Add(SourceSpan span, TypeInfo? type, EditorFactAvailability availability, OwnerKey owner, int priority)
        {
            if (span.IsHidden || span.IsEmpty || span.End > document.Text.Length ||
                observations.TryGetValue(span, out var previous) && previous.Priority > priority) return;
            bool unproven = _annotationProofs.TryGetValue(owner, out var annotations) && annotations.Values.Any(proven => !proven);
            if (type is not null && _callableSurfaces.TryGetValue(type, out var surface) && surface.Signatures.Count > 1)
            {
                if (surface.Signatures.Count > 32 || surface.TypeParameters?.Count > 32)
                {
                    observations[span] = (priority, new(null, new(type.GetType().Name,
                        "The retained public callable surface is incomplete.")));
                    return;
                }
                type = new TypeInfo.OverloadSet(surface.Signatures.Select(signature => signature is TypeInfo.Function function &&
                    surface.TypeParameters is { Count: > 0 } parameters
                    ? new TypeInfo.GenericFunction(parameters.ToList(), function.ParamTypes, function.ReturnType,
                        function.RequiredParams, function.HasRestParam, function.ThisType, function.ParamNames)
                    : signature).ToList());
            }
            var display = availability != EditorFactAvailability.Available || type is null || unproven
                ? new CheckedTypeDisplayResult(null, new UnsupportedTypeDisplay(type?.GetType().Name ?? "MissingType",
                    "No authoritative checked type is available for this source occurrence."))
                : CheckedTypeDisplay.Render(type);
            observations[span] = (priority, display);
        }
    }
}
