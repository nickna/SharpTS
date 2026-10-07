using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private InvocationQueryAttempt? _editorInvocationAttempt;

    private InvocationQueryAttempt? BeginEditorInvocation(Expr owner, EditorInvocationKind kind,
        IReadOnlyList<Expr> arguments)
    {
        if (!EditorFacts.IsEnabled) return null;
        if (!ShouldCaptureEditorFacts)
        {
            if (_editorInvocationAttempt is null) return null;
            return _editorInvocationAttempt = new(this, _editorInvocationAttempt,
                null, owner, kind, recovered: false, holes: false);
        }
        SourceDocument? document = CurrentSourceDocument;
        var records = document?.EditorSyntax?.GetRecords(owner);
        bool hasSourceOwner = records?.Any(record => record.Kind == EditorSyntaxKind.Expression &&
            record.Role == EditorSyntaxRole.Whole && !record.Span.IsHidden && !record.Span.IsEmpty &&
            record.Origin is EditorSyntaxOrigin.Written or EditorSyntaxOrigin.Grouping or
                EditorSyntaxOrigin.SourceEquivalent or EditorSyntaxOrigin.Recovered) == true;
        bool recovered = records?.Any(record => record.Origin == EditorSyntaxOrigin.Recovered) == true;
        bool holes = document?.EditorSyntax is { } syntax && arguments.Any(argument =>
            syntax.GetRecords(argument).Any(record => record.Span.IsHidden ||
                record.Origin is EditorSyntaxOrigin.Synthetic or EditorSyntaxOrigin.Recovered));
        // An enabled but generated invocation still shadows its parent frame. Its helper calls
        // must never accidentally populate the enclosing source invocation's candidates.
        return _editorInvocationAttempt = new(this, _editorInvocationAttempt,
            hasSourceOwner ? document : null, owner, kind, recovered, holes);
    }

    private void RecordEditorCallCandidates(TypeInfo callee, bool optional = false) =>
        _editorInvocationAttempt?.SetCallCandidates(callee, optional);

    private void RecordEditorConstructorCandidates(TypeInfo callee)
    {
        if (_editorInvocationAttempt is not { CanPublish: true } attempt) return;
        if (callee is TypeInfo.Class or TypeInfo.GenericClass)
        {
            bool complete = TryFindEditorConstructor(callee, out TypeInfo? constructor);
            attempt.SetConstructorCandidates(constructor, new TypeInfo.Instance(callee),
                callee is TypeInfo.GenericClass generic ? generic.TypeParams : null,
                GetEditorClassSource(callee), complete);
        }
        else attempt.SetConstructSignatures(callee);
    }

    private bool TryFindEditorConstructor(TypeInfo classType, out TypeInfo? constructor)
    {
        constructor = null;
        var visited = new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance);
        TypeInfo? current = classType;
        for (int depth = 0; current is not null && depth < 32; depth++)
        {
            ThrowIfCancellationRequested();
            if (!visited.Add(current)) return false;
            if (current is TypeInfo.MutableClass mutable && mutable.Methods.TryGetValue("constructor", out constructor))
                return true;
            if (GetMethods(current)?.TryGetValue("constructor", out constructor) == true) return true;
            current = GetSuperclass(current);
        }
        return current is null;
    }

    private void RecordEditorInvocationInstantiation(TypeInfo original, TypeInfo instantiated) =>
        _editorInvocationAttempt?.Instantiate(original, instantiated);

    private void RecordEditorInvocationSelection(TypeInfo signature, TypeInfo? instantiated = null) =>
        _editorInvocationAttempt?.Select(signature, instantiated);

    private void RecordEditorConstructedType(TypeInfo classType)
    {
        if (_editorInvocationAttempt is { CanPublish: true } attempt)
            attempt.SetConstructedType(new TypeInfo.Instance(classType));
    }

    private void RecordEditorConstructorInstantiation(TypeInfo original, IReadOnlyList<TypeInfo> parameterTypes,
        int minArity, bool hasRest, TypeInfo? thisType = null, List<string>? names = null, bool selected = false)
    {
        if (_editorInvocationAttempt is not { CanPublish: true } attempt) return;
        var signature = new TypeInfo.Function(parameterTypes.ToList(),
            attempt.ConstructedType ?? TypeInfo.Unknown.Shared, minArity, hasRest, thisType, names);
        attempt.Instantiate(original, signature);
        if (selected) attempt.Select(original, signature);
    }

    private sealed class InvocationQueryAttempt : IDisposable
    {
        private readonly TypeChecker _checker;
        private readonly InvocationQueryAttempt? _previous;
        private readonly SourceDocument? _document;
        private readonly Expr _owner;
        private readonly EditorInvocationKind _kind;
        private readonly bool _recovered;
        private readonly bool _holes;
        private readonly List<EditorInvocationCandidate> _candidates = [];
        private Dictionary<TypeInfo, int>? _instantiations;
        private bool _complete = true;
        private bool _succeeded;
        private int? _selectedOrdinal;
        private TypeInfo? _selectedSignature;
        private TypeInfo? _result;

        public InvocationQueryAttempt(TypeChecker checker, InvocationQueryAttempt? previous,
            SourceDocument? document, Expr owner, EditorInvocationKind kind, bool recovered, bool holes)
        {
            _checker = checker; _previous = previous; _document = document; _owner = owner;
            _kind = kind; _recovered = recovered; _holes = holes;
            if (document is not null)
                checker.EditorFacts.BeginInvocation(document, owner, kind, recovered, holes);
        }

        public bool CanPublish => _document is not null;
        public TypeInfo? ConstructedType { get; private set; }

        public void SetConstructedType(TypeInfo type) => ConstructedType = type;

        public void SetCallCandidates(TypeInfo callee, bool optional)
        {
            if (!CanPublish) return;
            ResetCandidates();
            AddCallCandidates(callee, optional, 0);
        }

        private void AddCallCandidates(TypeInfo callee, bool optional, int depth)
        {
            _checker.ThrowIfCancellationRequested();
            if (depth > 32 || _candidates.Count >= 32) { _complete = false; return; }
            if (callee is not (TypeInfo.OverloadedFunction or TypeInfo.GenericOverloadedFunction or TypeInfo.OverloadSet) &&
                _checker.GetEditorPublicSignatures(callee) is { } publicSignatures)
            {
                foreach (var signature in publicSignatures)
                {
                    if (_candidates.Count >= 32) { _complete = false; break; }
                    if (callee is TypeInfo.GenericFunction generic && signature is TypeInfo.Function)
                        Add(signature, generic.TypeParams);
                    else AddCallCandidates(signature, optional, depth + 1);
                }
                return;
            }
            switch (callee)
            {
                case TypeInfo.FunctionSupertype:
                    _complete = false;
                    break;
                case TypeInfo.Function function:
                    Add(function);
                    break;
                case TypeInfo.GenericFunction generic:
                    Add(generic, generic.TypeParams);
                    break;
                case TypeInfo.OverloadedFunction overloaded:
                    foreach (var signature in overloaded.Signatures) if (!Add(signature)) break;
                    break;
                case TypeInfo.GenericOverloadedFunction overloaded:
                    foreach (var signature in overloaded.Signatures) if (!Add(signature, overloaded.TypeParams)) break;
                    break;
                case TypeInfo.OverloadSet overloads:
                    foreach (var signature in overloads.Signatures)
                    {
                        if (_candidates.Count >= 32) { _complete = false; break; }
                        AddCallCandidates(signature, optional, depth + 1);
                    }
                    break;
                case TypeInfo.Interface { CallSignatures: { } signatures }:
                    foreach (var signature in signatures) if (!Add(signature, signature.TypeParams)) break;
                    break;
                case TypeInfo.GenericInterface { CallSignatures: { } signatures } generic:
                    foreach (var signature in signatures)
                        if (!Add(signature, CombineParameters(generic.TypeParams, signature.TypeParams))) break;
                    break;
                case TypeInfo.Record { CallSignatures: { } signatures }:
                    foreach (var signature in signatures) if (!Add(signature, signature.TypeParams)) break;
                    break;
                case TypeInfo.Union union:
                    foreach (var constituent in union.FlattenedTypes)
                    {
                        if (_candidates.Count >= 32) { _complete = false; break; }
                        AddCallCandidates(constituent, optional, depth + 1);
                    }
                    break;
                case TypeInfo.Null or TypeInfo.Undefined when optional:
                    break;
                default:
                    _complete = false;
                    break;
            }
        }

        public void SetConstructorCandidates(TypeInfo? constructor, TypeInfo constructedType,
            IReadOnlyList<TypeInfo.TypeParameter>? typeParameters, EditorSourceSlot? implicitOrigin,
            bool isComplete)
        {
            if (!CanPublish) return;
            ResetCandidates();
            ConstructedType = constructedType;
            _complete = isComplete;
            if (constructor is null && isComplete)
            {
                Add(new TypeInfo.Function([], constructedType, 0), typeParameters,
                    isImplicit: true, origin: implicitOrigin);
            }
            else if (constructor is not null && _checker.GetEditorPublicSignatures(constructor) is { } publicSignatures)
            {
                foreach (var signature in publicSignatures) if (!Add(signature, typeParameters)) break;
            }
            else if (constructor is TypeInfo.OverloadedFunction overloaded)
            {
                foreach (var signature in overloaded.Signatures) if (!Add(signature, typeParameters)) break;
            }
            else if (constructor is TypeInfo.Function function) Add(function, typeParameters);
            else _complete = false;
        }

        public void SetConstructSignatures(TypeInfo callee)
        {
            if (!CanPublish) return;
            ResetCandidates();
            switch (callee)
            {
                case TypeInfo.Interface { ConstructorSignatures: { } signatures }:
                    foreach (var signature in signatures) if (!Add(signature, signature.TypeParams)) break;
                    break;
                case TypeInfo.GenericInterface { ConstructorSignatures: { } signatures } generic:
                    foreach (var signature in signatures)
                        if (!Add(signature, CombineParameters(generic.TypeParams, signature.TypeParams))) break;
                    break;
                default: _complete = false; break;
            }
        }

        private static IReadOnlyList<TypeInfo.TypeParameter>? CombineParameters(
            IReadOnlyList<TypeInfo.TypeParameter> outer, IReadOnlyList<TypeInfo.TypeParameter>? inner) =>
            inner is null || inner.Count == 0 ? outer : outer.Concat(inner).Take(33).ToArray();

        private bool Add(TypeInfo signature, IReadOnlyList<TypeInfo.TypeParameter>? parameters = null,
            bool isImplicit = false, EditorSourceSlot? origin = null)
        {
            _checker.ThrowIfCancellationRequested();
            if (_candidates.Count >= 32) { _complete = false; return false; }
            _candidates.Add(new(_candidates.Count, signature,
                TypeParameters: parameters, Origin: origin ?? _checker.GetEditorSignatureSource(signature),
                ConstructedType: ConstructedType, IsImplicit: isImplicit));
            return true;
        }

        private void ResetCandidates()
        {
            _candidates.Clear(); _instantiations?.Clear(); _selectedOrdinal = null; _selectedSignature = null;
            _complete = true; ConstructedType = null;
        }

        private int FindOriginal(TypeInfo signature)
        {
            for (int ordinal = 0; ordinal < _candidates.Count; ordinal++)
                if (ReferenceEquals(_candidates[ordinal].OriginalSignature, signature)) return ordinal;
            return -1;
        }

        public void Instantiate(TypeInfo original, TypeInfo instantiated)
        {
            if (!CanPublish) return;
            int ordinal = FindOriginal(original);
            if (ordinal < 0) return;
            _candidates[ordinal] = _candidates[ordinal] with { InstantiatedSignature = instantiated };
            (_instantiations ??= new(ReferenceEqualityComparer.Instance))[instantiated] = ordinal;
        }

        public void Select(TypeInfo signature, TypeInfo? instantiated = null)
        {
            if (!CanPublish || _recovered || _holes || !_complete) return;
            int ordinal = FindOriginal(signature);
            if (ordinal < 0 && _instantiations?.TryGetValue(signature, out int mapped) == true) ordinal = mapped;
            if (ordinal < 0) return;
            _selectedOrdinal = ordinal;
            _selectedSignature = instantiated ?? _candidates[ordinal].InstantiatedSignature ?? signature;
        }

        public void SelectImplicitConstructor()
        {
            if (_candidates is [{ IsImplicit: true } candidate]) Select(candidate.OriginalSignature, candidate.OriginalSignature);
        }

        public void Succeed(TypeInfo result) { _succeeded = true; _result = result; }

        public void Dispose()
        {
            _checker._editorInvocationAttempt = _previous;
            if (!CanPublish) return;
            bool selected = _succeeded && _selectedOrdinal is not null && !_recovered && !_holes && _complete;
            _checker.EditorFacts.RecordInvocation(_document, _owner, _kind, _candidates,
                selected ? _selectedOrdinal : null, selected ? _selectedSignature : null,
                selected ? EditorInvocationStatus.Selected :
                    _candidates.Count == 0 ? EditorInvocationStatus.Unavailable : EditorInvocationStatus.CandidatesOnly,
                _succeeded ? _result : null, _complete, _recovered, _holes);
        }
    }
}
