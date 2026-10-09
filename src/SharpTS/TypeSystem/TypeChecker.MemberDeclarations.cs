using System.Runtime.CompilerServices;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // This table has weak document ownership, so reused checker clients do not retain prior
    // source captures. The syntax index already records class nesting without another AST.
    private ConditionalWeakTable<SourceDocument, Dictionary<object, SourceClassNesting>>? _sourceClassNesting;

    private sealed class SourceClassNesting
    {
        public bool IsNested { get; init; }
        public bool ContainsNestedClass { get; set; }
    }

    private void DeclareParameterValue(TypeEnvironment environment, Stmt.Parameter parameter, TypeInfo type)
    {
        BindingSymbol symbol = RegisterValueDeclaration(environment, parameter.Name);
        if (parameter.IsParameterProperty)
            symbol.DenyRename(BindingRenameEligibility.ParameterPropertyRequiresCoordinatedEdits);
        environment.Define(parameter.Name.Lexeme, type);
        environment.DefineValueBinding(parameter.Name.Lexeme, symbol);
        RecordEditorBinding(environment, parameter.Name, symbol, BindingNamespace.Value, type, EditorFactAvailability.Available);
    }

    private void RegisterSourceClassMembers(Stmt.Class declaration, int declarationId) =>
        RegisterSourceClassMembers(declarationId, declaration, declaration.Fields, declaration.Methods,
            declaration.Accessors, declaration.AutoAccessors);

    private void RegisterSourceClassMembers(Expr.ClassExpr declaration, int declarationId) =>
        RegisterSourceClassMembers(declarationId, declaration, declaration.Fields, declaration.Methods,
            declaration.Accessors, declaration.AutoAccessors);

    private void RegisterSourceClassMembers(int declarationId, object owner, IReadOnlyList<Stmt.Field> fields,
        IReadOnlyList<Stmt.Function> methods, IReadOnlyList<Stmt.Accessor>? accessors,
        IReadOnlyList<Stmt.AutoAccessor>? autoAccessors)
    {
        if (!Members.IsEnabled || declarationId == 0 || CurrentSourceDocument is not { IsVirtual: false } document)
            return;
        ThrowIfCancellationRequested();

        var candidates = new List<SourceMemberGroup>();
        var parameterProperties = methods.Where(method => method.Name.Lexeme == "constructor")
            .SelectMany(method => method.Parameters).Where(parameter => parameter.IsParameterProperty).ToArray();

        foreach (var field in fields)
        {
            ThrowIfCancellationRequested();
            if (field.ComputedKey is not null || field.IsLiteralName) continue;
            var parameter = parameterProperties.FirstOrDefault(parameter => ReferenceEquals(parameter.Name, field.Name));
            object declarationOwner = parameter is null ? field : parameter;
            if (GetWrittenMemberName(document, declarationOwner, field.Name) is not { } name) continue;
            SourceMemberKind kind = parameter is null ? SourceMemberKind.Field : SourceMemberKind.ParameterProperty;
            candidates.Add(new(field.Name.Lexeme, GetSourceMemberFacet(field.IsStatic, field.IsPrivate), kind,
                [new SourceMemberDeclaration(document, name, kind, declarationOwner)]));
        }

        foreach (var group in methods.Where(method => method.ComputedKey is null)
                     .GroupBy(method => (method.IsStatic, method.IsPrivate, method.Name.Lexeme)))
        {
            ThrowIfCancellationRequested();
            // More than one implementation is an invalid duplicate, not a legal overload group.
            if (group.Count(method => method.Body is not null) > 1) continue;
            var declarations = group.Select(method => (Method: method, Name: GetWrittenMemberName(document, method, method.Name)))
                .Where(pair => pair.Name is not null)
                .Select(pair => new SourceMemberDeclaration(document, pair.Name!, SourceMemberKind.Method, pair.Method)).ToArray();
            if (declarations.Length != group.Count()) continue;
            candidates.Add(new(group.Key.Lexeme, GetSourceMemberFacet(group.Key.IsStatic, group.Key.IsPrivate),
                SourceMemberKind.Method, declarations));
        }

        foreach (var group in (accessors ?? []).Where(accessor => accessor.ComputedKey is null)
                     .GroupBy(accessor => (accessor.IsStatic, accessor.Name.Lexeme)))
        {
            ThrowIfCancellationRequested();
            if (group.Count(accessor => accessor.Kind.Type == TokenType.GET) > 1 ||
                group.Count(accessor => accessor.Kind.Type == TokenType.SET) > 1) continue;
            var declarations = group.Select(accessor => (Accessor: accessor, Name: GetWrittenMemberName(document, accessor, accessor.Name)))
                .Where(pair => pair.Name is not null)
                .Select(pair => new SourceMemberDeclaration(document, pair.Name!, SourceMemberKind.Accessor, pair.Accessor)).ToArray();
            if (declarations.Length != group.Count()) continue;
            candidates.Add(new(group.Key.Lexeme, GetSourceMemberFacet(group.Key.IsStatic,
                declarations[0].Name.Type == TokenType.PRIVATE_IDENTIFIER), SourceMemberKind.Accessor, declarations));
        }

        foreach (var accessor in autoAccessors ?? [])
        {
            ThrowIfCancellationRequested();
            if (GetWrittenMemberName(document, accessor, accessor.Name) is not { } name) continue;
            candidates.Add(new(accessor.Name.Lexeme, GetSourceMemberFacet(accessor.IsStatic, false), SourceMemberKind.AutoAccessor,
                [new SourceMemberDeclaration(document, name, SourceMemberKind.AutoAccessor, accessor)]));
        }

        // A source name cannot become one canonical property merely because invalid declarations
        // collide. Only overload signatures and getter/setter pairs were grouped above.
        var groups = candidates.GroupBy(candidate => (candidate.Facet, candidate.Name))
            .Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
        var nesting = GetSourceClassNesting(document, owner);
        Members.RegisterSourceClass(declarationId, document, owner, groups,
            isNestedPrivateEnvironment: nesting.IsNested, containsNestedClass: nesting.ContainsNestedClass);
    }

    private static MemberFacet GetSourceMemberFacet(bool isStatic, bool isPrivate) =>
        (isStatic, isPrivate) switch
        {
            (false, false) => MemberFacet.Instance,
            (true, false) => MemberFacet.Static,
            (false, true) => MemberFacet.PrivateInstance,
            _ => MemberFacet.PrivateStatic,
        };

    private static Token? GetWrittenMemberName(SourceDocument document, object owner, Token semanticName)
    {
        // Contextual keywords and literal spellings may be normalized by the parser. Only the
        // parser's exact written alias is authoritative; never calculate its end from a decoded key.
        var written = document.EditorSyntax?.GetRecords(owner)
            .FirstOrDefault(record => record.IsAuthoritative && record.Kind == EditorSyntaxKind.Name &&
                record.Role is EditorSyntaxRole.DeclarationName or EditorSyntaxRole.MemberName or EditorSyntaxRole.PrivateName &&
                record.Token is not null && record.Token.Lexeme == semanticName.Lexeme)?.Token;
        written ??= semanticName.Start >= 0 ? semanticName : null;
        if (written is null || written.Type is TokenType.STRING or TokenType.NUMBER || written.Start < 0 ||
            written.End > document.Text.Length || written.Lexeme.Length == 0 ||
            !document.Text.AsSpan(written.Start, written.Lexeme.Length).SequenceEqual(written.Lexeme)) return null;
        int first = written.Type == TokenType.PRIVATE_IDENTIFIER ? 1 : 0;
        if (first == written.Lexeme.Length ||
            !(char.IsLetter(written.Lexeme[first]) || written.Lexeme[first] is '_' or '$')) return null;
        for (int index = first + 1; index < written.Lexeme.Length; index++)
            if (!(char.IsLetterOrDigit(written.Lexeme[index]) || written.Lexeme[index] is '_' or '$')) return null;
        return written;
    }

    private SourceClassNesting GetSourceClassNesting(SourceDocument document, object owner)
    {
        var nesting = (_sourceClassNesting ??= new()).GetValue(document, source =>
        {
            var result = new Dictionary<object, SourceClassNesting>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<(object Owner, SourceSpan Span)>();
            foreach (var record in (source.EditorSyntax?.Records ?? []).Where(record => record.IsAuthoritative &&
                         record.Kind == EditorSyntaxKind.Class && record.Role == EditorSyntaxRole.Whole)
                         .OrderBy(record => record.Span.Start).ThenByDescending(record => record.Span.End))
            {
                ThrowIfCancellationRequested();
                if (result.ContainsKey(record.Node)) continue;
                while (stack.TryPeek(out var enclosing) &&
                       !(enclosing.Span.Start <= record.Span.Start && record.Span.End <= enclosing.Span.End)) stack.Pop();
                result.Add(record.Node, new SourceClassNesting { IsNested = stack.Count != 0 });
                if (stack.TryPeek(out var parent)) result[parent.Owner].ContainsNestedClass = true;
                stack.Push((record.Node, record.Span));
            }
            return result;
        });
        if (nesting.TryGetValue(owner, out var facts)) return facts;
        var visitor = new NestedSourceClassVisitor(owner);
        if (owner is Stmt statement) visitor.Visit(statement);
        else if (owner is Expr expression) visitor.Visit(expression);
        return new SourceClassNesting { IsNested = _currentClass is not null, ContainsNestedClass = visitor.Found };
    }

    private sealed class NestedSourceClassVisitor(object root) : AstVisitorBase
    {
        public bool Found { get; private set; }
        protected override void VisitClass(Stmt.Class statement)
        {
            if (!ReferenceEquals(statement, root)) { Found = true; ShouldContinue = false; return; }
            base.VisitClass(statement);
        }
        protected override void VisitClassExpr(Expr.ClassExpr expression)
        {
            if (!ReferenceEquals(expression, root)) { Found = true; ShouldContinue = false; return; }
            base.VisitClassExpr(expression);
        }
    }
}
