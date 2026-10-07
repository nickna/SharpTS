using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public enum EditorFactAvailability { Unavailable, Available, Recovered }
public enum EditorScopeKind { Global, Source, Function, ParameterList, Block, Class }
public enum EditorInvocationKind { Call, New, PrivateCall }
public enum EditorInvocationStatus { Unavailable, CandidatesOnly, Selected }
public enum EditorMemberKind { Property, Method, Accessor, Field, AutoAccessor, ParameterProperty, NamespaceMember }
public enum EditorTypeRenderContext { Type, Value }

public sealed record EditorSourceSlot(SourceDocument? Document, object Owner, Token? Name = null,
    int? OverloadOrdinal = null);

// Drafts are checker-build inputs. Frozen query DTOs below contain presentation values only.
public sealed record EditorInvocationCandidate(int Ordinal, TypeInfo OriginalSignature,
    TypeInfo? InstantiatedSignature = null, IReadOnlyList<TypeInfo.TypeParameter>? TypeParameters = null,
    EditorSourceSlot? Origin = null, TypeInfo? ConstructedType = null, bool IsImplicit = false);

public sealed record EditorReceiverCandidate(string Name, TypeInfo? Type,
    EditorMemberKind Kind = EditorMemberKind.Property, AccessModifier Access = AccessModifier.Public,
    MemberFacet? Facet = null, bool IsReadonly = false, bool IsOptional = false,
    SourceMemberSymbol? Source = null, IReadOnlyDictionary<string, TypeInfo>? Substitutions = null);

public sealed record EditorTypePresentation(string Text, bool IsAvailable, bool IsTruncated);
public sealed record EditorParameterPresentation(string Name, SourceSpan LabelRange,
    EditorTypePresentation Type, bool IsOptional, bool IsRest);
public sealed record EditorSignaturePresentation(string Label,
    IReadOnlyList<EditorParameterPresentation> Parameters, EditorTypePresentation ReturnType,
    int MinimumArity, bool HasRestParameter, bool IsAvailable, bool IsTruncated);
public sealed record EditorBindingIdentity(int Id, int Generation, string CanonicalName,
    BindingNamespace Facet, IReadOnlyList<BindingDeclaration> Declarations,
    BindingRenameEligibility RenameEligibility);
public sealed record EditorSourceMemberIdentity(int Id, long Generation, int DeclaringClassId,
    IReadOnlyList<SourceMemberDeclaration> Declarations);
public sealed record EditorOccurrenceFact(SourceDocument Document, Expr Owner, SourceSpan? Span,
    EditorTypePresentation Type, EditorFactAvailability Availability);
public sealed record EditorDeclarationFact(EditorSourceSlot Source, string LocalName,
    BindingNamespace Facet, EditorBindingIdentity? Binding, EditorTypePresentation Type,
    EditorFactAvailability Availability);
public sealed record EditorVisibleBinding(string LocalName, BindingNamespace Facet,
    EditorBindingIdentity? Binding, EditorTypePresentation Type, object? DeclarationOwner,
    int AvailableFrom, EditorFactAvailability Availability);
public sealed record EditorSourceScope(int Id, SourceDocument? Document, object Owner,
    SourceSpan? Span, int? ParentScopeId, EditorScopeKind Kind,
    IReadOnlyList<EditorVisibleBinding> Bindings);
public sealed record EditorReceiverMember(string Name, EditorTypePresentation Type,
    EditorMemberKind Kind, AccessModifier Access, MemberFacet? Facet, bool IsReadonly,
    bool IsOptional, EditorSourceMemberIdentity? Source);
public sealed record EditorReceiverSet(IReadOnlyList<EditorReceiverMember> Members,
    bool IsComplete, bool IsTruncated)
{
    public static EditorReceiverSet Unavailable { get; } = new([], false, false);
}
public sealed record EditorInvocationSignature(int Ordinal, int OriginalSignatureId,
    EditorSignaturePresentation Declared, EditorSignaturePresentation? Instantiated,
    EditorSourceSlot? Origin, EditorTypePresentation? ConstructedType, bool IsImplicit);
public sealed record EditorInvocationFact(SourceDocument Document, Expr Owner,
    EditorInvocationKind Kind, IReadOnlyList<EditorInvocationSignature> Candidates,
    int? SelectedOrdinal, EditorSignaturePresentation? SelectedSignature,
    EditorTypePresentation? ResultType, EditorInvocationStatus Status, bool IsComplete,
    bool IsRecovered, bool HasHoles);
