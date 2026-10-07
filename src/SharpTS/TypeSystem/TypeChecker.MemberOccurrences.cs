using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // Observations belong to one real checker lookup. Nested receiver/RHS checks get their
    // own frame; no class hierarchy is walked again merely to provide editor metadata.
    private SourceMemberLookup? _sourceMemberLookup;

    private SourceMemberLookup? BeginSourceMemberLookup(Expr owner, Token name, MemberOperation operation)
    {
        if (!Members.IsEnabled || CurrentSourceDocument is not { } document) return null;
        Token? written = WrittenSourceMemberName(document, owner, name);
        if (written is null) return null;
        return _sourceMemberLookup = new SourceMemberLookup(this, _sourceMemberLookup,
            document, owner, name, written, operation);
    }

    private static Token? WrittenSourceMemberName(SourceDocument document, object owner, Token name)
    {
        // Parser-generated accesses may reuse a real declaration token (parameter properties
        // do this). An in-range token alone is not proof of a written member occurrence.
        Token? written = document.EditorSyntax?.GetRecords(owner)
            .FirstOrDefault(record => record.IsAuthoritative && record.Token is { Start: >= 0 } token &&
                token.Lexeme == name.Lexeme && record.Role is EditorSyntaxRole.MemberName or EditorSyntaxRole.PrivateName)
            ?.Token;
        return written is not null && written.End <= document.Text.Length &&
            document.Text.AsSpan(written.Start, written.Lexeme.Length).SequenceEqual(written.Lexeme)
            ? written : null;
    }

    private void ExpectSourceMemberReceiver(TypeInfo receiver) =>
        _sourceMemberLookup?.ExpectReceiver(receiver);

    private void ObserveSourceMemberSelection(Token name, TypeInfo declaringType, MemberFacet facet)
    {
        if (_sourceMemberLookup is not { } lookup || !ReferenceEquals(lookup.Name, name)) return;
        int declarationId = SelectedSourceClassId(declaringType);
        lookup.Observe(Members.ResolveSelected(declarationId, facet, name.Lexeme));
    }

    private static int SelectedSourceClassId(TypeInfo type) => type switch
    {
        TypeInfo.Class @class => @class.Core.DeclarationId,
        TypeInfo.GenericClass generic => generic.Core.DeclarationId,
        TypeInfo.MutableClass mutable => mutable.DeclarationId,
        TypeInfo.InstantiatedGeneric generic => SelectedSourceClassId(generic.GenericDefinition),
        TypeInfo.Instance instance => SelectedSourceClassId(instance.ResolvedClassType),
        _ => 0,
    };

    private void ObservePrivateSourceMemberSelection(Token name, TypeInfo receiver,
        TypeInfo.Class lexicalOwner, MemberFacet facet)
    {
        if (_sourceMemberLookup is not { } lookup || !ReferenceEquals(lookup.Name, name)) return;
        // The existing private checker consults lexical maps permissively. Metadata must not
        // claim a nominal owner for an any/structural receiver or the wrong static facet.
        if (!HasPrivateSourceOwner(receiver, lexicalOwner.Core.DeclarationId,
                facet == MemberFacet.PrivateStatic)) return;
        lookup.ExpectSingleSelection();
        lookup.Observe(Members.ResolveSelected(lexicalOwner.Core.DeclarationId, facet, name.Lexeme));
    }

    private bool HasPrivateSourceOwner(TypeInfo receiver, int ownerId, bool isStatic, int depth = 0)
    {
        if (ownerId == 0 || depth > 32) return false;
        if (receiver is TypeInfo.Union union)
            return union.FlattenedTypes.All(part => HasPrivateSourceOwner(part, ownerId, isStatic, depth + 1));
        if (receiver is TypeInfo.Intersection intersection)
            return intersection.FlattenedTypes.All(part => HasPrivateSourceOwner(part, ownerId, isStatic, depth + 1));
        if (receiver is TypeInfo.TypeParameter { Constraint: { } constraint })
            return HasPrivateSourceOwner(constraint, ownerId, isStatic, depth + 1);
        if (isStatic)
            return receiver is TypeInfo.Class or TypeInfo.GenericClass or TypeInfo.InstantiatedGeneric &&
                Members.IsSameSourceClass(SelectedSourceClassId(receiver), ownerId);
        return receiver is TypeInfo.Instance instance &&
            EnumerateClassCores(instance.ResolvedClassType).Any(core => Members.IsSameSourceClass(core.DeclarationId, ownerId));
    }

    private void RecordSourceMemberCall(Expr.Call call)
    {
        if (!Members.IsEnabled || CurrentSourceDocument is not { } document) return;
        Expr callee = call.Callee;
        while (true)
        {
            switch (callee)
            {
                case Expr.Grouping grouping: callee = grouping.Expression; continue;
                case Expr.NonNullAssertion assertion: callee = assertion.Expression; continue;
                case Expr.TypeAssertion assertion: callee = assertion.Expression; continue;
            }
            break;
        }
        Token? name = callee switch
        {
            Expr.Get get => get.Name,
            Expr.GetPrivate get => get.Name,
            Expr.Super { Method: { } method } => method,
            _ => null,
        };
        if (name is null) return;
        RecordProvenSourceMemberCall(callee, name, call);
    }

    private void RecordProvenSourceMemberCall(Expr nameOwner, Token name, Expr call)
    {
        if (!Members.IsEnabled || CurrentSourceDocument is not { } document ||
            WrittenSourceMemberName(document, nameOwner, name) is not { } written) return;
        // This runs only after the real call checker succeeds. Invalid arguments can retain
        // their independently proven member read, without acquiring call eligibility.
        Members.Bind(document, written, Members.GetResolution(document, written, MemberOperation.Read),
            MemberOperation.Call, owner: call);
    }

    private void ForgetSourceMemberCall(Expr owner, Token name, Expr nameOwner)
    {
        if (!Members.IsEnabled || CurrentSourceDocument is not { } document ||
            WrittenSourceMemberName(document, nameOwner, name) is not { } written) return;
        Members.RemoveOperation(document, written, MemberOperation.Call, owner);
    }

    private void ForgetSourceMemberCall(Expr.Call call)
    {
        if (!Members.IsEnabled) return;
        Expr callee = call.Callee;
        while (true)
        {
            switch (callee)
            {
                case Expr.Grouping grouping: callee = grouping.Expression; continue;
                case Expr.NonNullAssertion assertion: callee = assertion.Expression; continue;
                case Expr.TypeAssertion assertion: callee = assertion.Expression; continue;
            }
            break;
        }
        Token? name = callee switch
        {
            Expr.Get get => get.Name,
            Expr.GetPrivate get => get.Name,
            Expr.Super { Method: { } method } => method,
            _ => null,
        };
        if (name is not null) ForgetSourceMemberCall(call, name, callee);
    }

    private void RecordPrivateSourceMemberRead(Expr owner, Token name, TypeInfo receiver,
        TypeInfo.Class lexicalOwner, MemberFacet facet)
    {
        using var lookup = BeginSourceMemberLookup(owner, name, MemberOperation.Read);
        ObservePrivateSourceMemberSelection(name, receiver, lexicalOwner, facet);
        lookup?.Succeed();
    }

    private sealed class SourceMemberLookup(TypeChecker checker, SourceMemberLookup? previous,
        SourceDocument document, Expr owner, Token name, Token writtenName, MemberOperation operation) : IDisposable
    {
        private List<MemberResolution>? _selections;
        private int _requiredSelections = 1;
        private bool _supportedReceiver = true;
        private bool _succeeded;
        public Token Name { get; } = name;

        public void ExpectReceiver(TypeInfo receiver)
        {
            (_requiredSelections, _supportedReceiver) = CountRequiredSelections(receiver, 0);
        }

        public void ExpectSingleSelection() => (_requiredSelections, _supportedReceiver) = (1, true);
        public void Observe(MemberResolution resolution) => (_selections ??= []).Add(resolution);
        public void Succeed() => _succeeded = true;

        public void Dispose()
        {
            checker._sourceMemberLookup = previous;
            List<MemberResolution> parts = _selections ?? [];
            if (!_succeeded || !_supportedReceiver || parts.Count != _requiredSelections)
                parts.Add(MemberResolution.Unavailable);
            checker.Members.Bind(document, writtenName, MemberResolution.CombineRequired(parts), operation, owner: owner);
        }

        private static (int Count, bool Supported) CountRequiredSelections(TypeInfo receiver, int depth)
        {
            if (depth > 32) return (1, false);
            if (receiver is TypeInfo.TypeParameter { Constraint: { } constraint })
                return CountRequiredSelections(constraint, depth + 1);
            IEnumerable<TypeInfo>? constituents = receiver switch
            {
                TypeInfo.Union union => union.FlattenedTypes,
                TypeInfo.Intersection intersection => intersection.FlattenedTypes,
                _ => null,
            };
            if (constituents is not null)
            {
                int count = 0;
                bool supported = true;
                foreach (TypeInfo part in constituents)
                {
                    var required = CountRequiredSelections(part, depth + 1);
                    count += required.Count;
                    supported &= required.Supported;
                }
                return (count, supported && count != 0);
            }
            return (1, receiver is TypeInfo.Class or TypeInfo.GenericClass or TypeInfo.Instance or
                TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass });
        }
    }
}
