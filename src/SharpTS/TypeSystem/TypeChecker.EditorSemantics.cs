using System.Runtime.CompilerServices;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private EditorSemanticIndex? _editorFacts;
    private ConditionalWeakTable<SourceDocument, EditorDocumentDeclarations>? _editorDeclarations;
    private Dictionary<SourceDocument, Dictionary<TypeEnvironment, int>>? _editorScopeEnvironments;
    private int? _editorGlobalScope;
    private static readonly object EditorGlobalOwner = new();
    private Dictionary<TypeInfo, EditorSourceSlot>? _editorSignatureSources;
    private Dictionary<TypeInfo, IReadOnlyList<TypeInfo>>? _editorPublicSignatures;
    private Dictionary<int, SourceSpan?>? _editorScopeSpans;
    private Stack<EditorActiveScope>? _editorActiveScopes;
    private Dictionary<int, EditorScopeKind>? _editorScopeKinds;
    private Dictionary<int, SourceDocument>? _editorScopeDocuments;
    private HashSet<BindingSymbol>? _editorNonGlobalBindings;

    public EditorSemanticIndex EditorFacts => _editorFacts ?? EditorSemanticIndex.Empty;
    private bool ShouldCaptureEditorFacts => EditorFacts.IsEnabled &&
        (_suppressDiagnostics == 0 || _currentModule?.IsDeclarationFile == true);

    public TypeChecker WithEditorMetadata(bool enabled = true)
    {
        if (enabled)
        {
            _editorFacts ??= new EditorSemanticIndex();
            WithMemberProvenance();
        }
        else
        {
            ResetEditorMetadata();
            _editorFacts = null;
        }
        return this;
    }

    private void ResetEditorMetadata()
    {
        _editorFacts?.Clear();
        _editorDeclarations = null;
        _editorScopeEnvironments = null;
        _editorGlobalScope = null;
        _editorSignatureSources = null;
        _editorPublicSignatures = null;
        _editorScopeSpans = null;
        _editorActiveScopes = null;
        _editorScopeKinds = null;
        _editorScopeDocuments = null;
        _editorNonGlobalBindings = null;
        _editorReceivers = null;
    }

    private bool IsEditorSourceExpression(Expr expression) => ShouldCaptureEditorFacts &&
        CurrentSourceDocument?.EditorSyntax?.GetRecords(expression).Any(record => record.IsAuthoritative &&
            record.Kind is EditorSyntaxKind.Expression or EditorSyntaxKind.Grouping or EditorSyntaxKind.Literal) == true;

    private bool HasEditorExpressionView(Expr expression) => ShouldCaptureEditorFacts &&
        CurrentSourceDocument?.EditorSyntax?.GetRecords(expression).Any(record => !record.Span.IsHidden &&
            record.Kind is EditorSyntaxKind.Expression or EditorSyntaxKind.Grouping or EditorSyntaxKind.Literal or
                EditorSyntaxKind.Invocation or EditorSyntaxKind.MemberAccess) == true;

    private void RecordEditorExpressionType(Expr expression, TypeInfo type)
    {
        if (!IsEditorSourceExpression(expression)) return;
        EditorFacts.CompleteExpression(CurrentSourceDocument, expression, type);
        if (expression is Expr.ClassExpr) CaptureEditorClassDeclarations(expression, type);
        CaptureEditorReceiver(expression, type);
    }

    private void BeginEditorExpression(Expr expression)
    {
        if (!HasEditorExpressionView(expression)) return;
        EditorFacts.BeginExpression(CurrentSourceDocument, expression);
        if (expression is Expr.ArrowFunction or Expr.ClassExpr)
            EditorFacts.ClearBodyEntered(CurrentSourceDocument, expression);
        if (expression is Expr.ClassExpr) CaptureEditorClassDeclarations(expression, null);
        ResetEditorReceiver(expression);
    }

    private void FailEditorExpression(Expr expression)
    {
        if (!HasEditorExpressionView(expression)) return;
        EditorFacts.FailExpression(CurrentSourceDocument, expression, preserveReceiver: expression is Expr.Super);
        if (expression is not Expr.Super) ResetEditorReceiver(expression);
    }

    private void MarkEditorBodyEntered(object owner)
    {
        if (!ShouldCaptureEditorFacts || CurrentSourceDocument is not { EditorSyntax: { } syntax } document) return;
        foreach (var view in syntax.GetRecords(owner).Where(record => record.IsAuthoritative && record.Role == EditorSyntaxRole.Body &&
                     record.Kind is EditorSyntaxKind.Function or EditorSyntaxKind.Class or EditorSyntaxKind.Block))
            EditorFacts.MarkBodyEntered(document, owner, view.Span);
    }

    private sealed record EditorDeclarationSyntax(object Owner, Token Name, int AvailableFrom);
    private sealed class EditorDocumentDeclarations
    {
        public Dictionary<Token, EditorDeclarationSyntax> Names { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private EditorDeclarationSyntax? GetEditorDeclaration(Token semanticName)
    {
        if (!EditorFacts.IsEnabled || CurrentSourceDocument is not { EditorSyntax: { } syntax } document) return null;
        var declarations = (_editorDeclarations ??= new()).GetValue(document, _ =>
        {
            var result = new EditorDocumentDeclarations();
            foreach (var record in syntax.Records)
            {
                if (!record.IsAuthoritative || record.Kind != EditorSyntaxKind.Name || record.Token is not { } written) continue;
                Token? semantic = record.Node switch
                {
                    Stmt.Var declaration => declaration.Name,
                    Stmt.Const declaration => declaration.Name,
                    Stmt.Function declaration => declaration.Name,
                    Stmt.Class declaration => declaration.Name,
                    Stmt.Parameter declaration => declaration.Name,
                    Stmt.Interface declaration => declaration.Name,
                    Stmt.Enum declaration => declaration.Name,
                    Stmt.TypeAlias declaration => declaration.Name,
                    Stmt.Namespace declaration => declaration.Name,
                    Stmt.ImportAlias declaration => declaration.AliasName,
                    Stmt.ImportRequire declaration => declaration.AliasName,
                    Stmt.TryCatch declaration => declaration.CatchParam,
                    Stmt.ForOf declaration => declaration.Variable,
                    Stmt.ForIn { IsDeclaration: true } declaration => declaration.Variable,
                    TypeParam declaration => declaration.Name,
                    Expr.ArrowFunction declaration => declaration.Name,
                    Expr.ClassExpr declaration => declaration.Name,
                    _ => null,
                };
                if (semantic is null || semantic.Lexeme != written.Lexeme) continue;
                int availableFrom = record.Node is Stmt.Const or Stmt.Var { IsVar: false } or Stmt.Class or Stmt.Parameter ? written.End : 0;
                if (availableFrom != 0)
                {
                    var whole = syntax.GetRecords(record.Node).FirstOrDefault(view => view.IsAuthoritative &&
                        view.Role == EditorSyntaxRole.Parameter)?.Span;
                    if (whole is { } parameterSpan) availableFrom = parameterSpan.End;
                    else if (document.Spans.TryGetSpan(record.Node, out var declarationSpan) && !declarationSpan.IsHidden)
                        availableFrom = declarationSpan.End;
                }
                result.Names.TryAdd(semantic, new(record.Node, written, availableFrom));
            }
            return result;
        });
        return declarations.Names.GetValueOrDefault(semanticName);
    }

    private int EnsureEditorGlobalScope() => _editorGlobalScope ??=
        EditorFacts.RegisterScope(null, EditorGlobalOwner, null, kind: EditorScopeKind.Global);

    private void ClearEditorDocument(SourceDocument? document)
    {
        if (document is null) return;
        // Member-only capture also visits preparatory source passes. Keep declarations,
        // but never let their discarded uses become authoritative query results.
        Members.ClearDocumentUses(document);
        if (!EditorFacts.IsEnabled) return;
        EditorFacts.ClearDocument(document);
        _editorScopeEnvironments?.Remove(document);
        _editorDeclarations?.Remove(document);
        _editorReceivers?.Remove(document);
        // Source-query auxiliary IDs are recreated; source maps/signature originals remain exact.
        // Entries for other documents stay live until their own authoritative pass starts.
        if (_editorScopeDocuments is not null)
            foreach (int scope in _editorScopeDocuments.Where(pair => ReferenceEquals(pair.Value, document)).Select(pair => pair.Key).ToArray())
            {
                _editorScopeDocuments.Remove(scope);
                _editorScopeSpans?.Remove(scope);
                _editorScopeKinds?.Remove(scope);
            }
    }

    private int? FindEditorScope(TypeEnvironment environment)
    {
        if (CurrentSourceDocument is not { } document ||
            _editorScopeEnvironments?.TryGetValue(document, out var scopes) != true || scopes is null) return null;
        for (TypeEnvironment? current = environment; current is not null; current = current.Enclosing)
            if (scopes.TryGetValue(current, out int scope)) return scope;
        return null;
    }

    private void RegisterEditorScope(object owner, TypeEnvironment environment,
        EditorScopeKind kind = EditorScopeKind.Block, EditorSyntaxRole role = EditorSyntaxRole.Body)
    {
        if (!ShouldCaptureEditorFacts || CurrentSourceDocument is not { EditorSyntax: { } syntax } document) return;
        SourceSpan? span = owner is SourceDocument ? new SourceSpan(0, document.Text.Length) :
            syntax.GetRecords(owner).FirstOrDefault(record => record.IsAuthoritative && record.Role == role &&
                (kind != EditorScopeKind.ParameterList || record.Kind == EditorSyntaxKind.ParameterList) &&
                (kind != EditorScopeKind.Function || record.Kind == EditorSyntaxKind.Function) &&
                (kind != EditorScopeKind.Class || record.Kind == EditorSyntaxKind.Class))?.Span;
        if (span is null && role == EditorSyntaxRole.Whole &&
            syntax.GetRecords(owner).Any(record => record.IsAuthoritative && record.Role == EditorSyntaxRole.DeclarationName) &&
            document.Spans.TryGetSpan(owner, out var declaredSpan) && !declaredSpan.IsHidden)
            span = declaredSpan;
        if (span is null) return;
        int parent = ActiveEditorScope(document)?.Id ??
            FindEditorScope(environment.Enclosing ?? environment) ?? EnsureEditorGlobalScope();
        int scope = EditorFacts.RegisterScope(document, owner, span, parent, kind);
        (_editorScopeSpans ??= [])[scope] = span;
        (_editorScopeKinds ??= [])[scope] = kind;
        (_editorScopeDocuments ??= [])[scope] = document;
        var documents = _editorScopeEnvironments ??= new(ReferenceEqualityComparer.Instance);
        if (!documents.TryGetValue(document, out var environments))
            documents.Add(document, environments = new(ReferenceEqualityComparer.Instance));
        environments[environment] = scope;
    }

    private sealed record EditorActiveScope(SourceDocument Document, int Id, SourceSpan Span);
    private EditorActiveScope? ActiveEditorScope(SourceDocument document) =>
        _editorActiveScopes is { Count: > 0 } scopes && ReferenceEquals(scopes.Peek().Document, document) ? scopes.Peek() : null;

    private sealed class EditorScopeExit(Stack<EditorActiveScope> scopes) : IDisposable
    {
        public void Dispose() => scopes.Pop();
    }

    private IDisposable? EnterEditorLexicalScope(object owner, EditorSyntaxRole role = EditorSyntaxRole.Body,
        TokenType? keyword = null)
    {
        if (!ShouldCaptureEditorFacts || CurrentSourceDocument is not { EditorSyntax: { } syntax } document) return null;
        var view = syntax.GetRecords(owner).FirstOrDefault(record => record.IsAuthoritative &&
            record.Kind == EditorSyntaxKind.Block && record.Role == role && (keyword is null || record.Token?.Type == keyword));
        if (view is null) return null;
        int parent = ActiveEditorScope(document)?.Id ?? FindEditorScope(_environment) ?? EnsureEditorGlobalScope();
        int scope = EditorFacts.RegisterScope(document, owner, view.Span, parent, EditorScopeKind.Block);
        (_editorScopeSpans ??= [])[scope] = view.Span;
        (_editorScopeKinds ??= [])[scope] = EditorScopeKind.Block;
        (_editorScopeDocuments ??= [])[scope] = document;
        var active = _editorActiveScopes ??= new();
        active.Push(new(document, scope, view.Span));
        return new EditorScopeExit(active);
    }

    private void RegisterEditorParameterScope(object owner, TypeEnvironment environment)
    {
        RegisterEditorScope(owner, environment, EditorScopeKind.ParameterList, EditorSyntaxRole.Whole);
        if (!ShouldCaptureEditorFacts || CurrentSourceDocument is not { } document ||
            _editorScopeEnvironments?.TryGetValue(document, out var scopes) != true || scopes is null ||
            !scopes.TryGetValue(environment, out int scope)) return;
        IEnumerable<Stmt.Parameter> parameters = owner switch
        {
            Stmt.Function function => function.Parameters,
            Expr.ArrowFunction function => function.Parameters,
            _ => [],
        };
        // Install all spellings before defaults are checked. A later unavailable parameter still
        // shadows an outer variable; progressively declared parameters overwrite these slots.
        foreach (var parameter in parameters)
            if (GetEditorDeclaration(parameter.Name) is { } source)
                EditorFacts.BindLocal(scope, source.Name.Lexeme, BindingNamespace.Value, null, null,
                    source.Owner, source.AvailableFrom, EditorFactAvailability.Unavailable);
    }

    private void RecordEditorBinding(TypeEnvironment environment, Token semanticName, BindingSymbol symbol,
        BindingNamespace facet, TypeInfo? type, EditorFactAvailability availability)
    {
        if (!ShouldCaptureEditorFacts) return;
        if (GetEditorDeclaration(semanticName) is not { } source) return;
        EditorFacts.RecordDeclaration(CurrentSourceDocument, source.Owner, source.Name, symbol, facet, type, availability);
        if ((source.Owner is Stmt.Parameter or TypeParam) &&
            (CurrentSourceDocument is not { } currentDocument ||
             _editorScopeEnvironments?.TryGetValue(currentDocument, out var sourceScopes) != true || sourceScopes is null ||
             !sourceScopes.ContainsKey(environment))) return;
        int? selectedScope = FindEditorScope(environment);
        if (CurrentSourceDocument is { } sourceDocument && ActiveEditorScope(sourceDocument) is { } active &&
            active.Span.Contains(source.Name.Start) && (selectedScope is null ||
                _editorScopeSpans?.GetValueOrDefault(selectedScope.Value) is not { } mapped || active.Span.Length < mapped.Length))
            selectedScope = active.Id;
        if (selectedScope is { } scope)
        {
            EditorFacts.BindLocal(scope, source.Name.Lexeme, facet, symbol, type, source.Owner,
                source.AvailableFrom, availability);
            if (_editorScopeKinds?.GetValueOrDefault(scope) is not EditorScopeKind.Source and not EditorScopeKind.Global)
                (_editorNonGlobalBindings ??= new(ReferenceEqualityComparer.Instance)).Add(symbol);
        }
    }

    private void RecordEditorFinalDeclaration(Token name, TypeInfo type, BindingNamespace facet = BindingNamespace.Value)
    {
        if (!ShouldCaptureEditorFacts) return;
        BindingSymbol? symbol = facet == BindingNamespace.Type ? _environment.GetTypeSymbol(name.Lexeme) :
            _environment.GetValueBinding(name.Lexeme);
        if (symbol is not null)
            RecordEditorBinding(_environment, name, symbol, facet, type, EditorFactAvailability.Available);
    }

    private void BeginEditorDeclaration(Stmt statement)
    {
        if (!ShouldCaptureEditorFacts) return;
        if (statement is Stmt.Function or Stmt.Class) EditorFacts.ClearBodyEntered(CurrentSourceDocument, statement);
        Token? name = statement switch
        {
            Stmt.Var declaration => declaration.Name, Stmt.Const declaration => declaration.Name,
            Stmt.Function declaration => declaration.Name, Stmt.Class declaration => declaration.Name,
            Stmt.Interface declaration => declaration.Name, Stmt.Enum declaration => declaration.Name,
            Stmt.TypeAlias declaration => declaration.Name, Stmt.Namespace declaration => declaration.Name,
            Stmt.ImportAlias declaration => declaration.AliasName, Stmt.ImportRequire declaration => declaration.AliasName,
            _ => null,
        };
        if (name is null) return;
        if (statement is Stmt.Class) CaptureEditorClassDeclarations(statement, null);
        BindingSymbol? value = _environment.GetValueBinding(name.Lexeme);
        BindingSymbol? type = _environment.GetTypeSymbol(name.Lexeme);
        if (statement is not Stmt.Interface and not Stmt.TypeAlias && value is not null)
            RecordEditorBinding(_environment, name, value, BindingNamespace.Value, null, EditorFactAvailability.Unavailable);
        if (statement is Stmt.Class or Stmt.Interface or Stmt.Enum or Stmt.TypeAlias or Stmt.Namespace && type is not null)
            RecordEditorBinding(_environment, name, type, BindingNamespace.Type, null, EditorFactAvailability.Unavailable);
    }

    private void CompleteEditorDeclaration(Stmt statement)
    {
        if (!ShouldCaptureEditorFacts) return;
        Token? name = statement switch
        {
            Stmt.Function declaration => declaration.Name, Stmt.Class declaration => declaration.Name,
            Stmt.Interface declaration => declaration.Name, Stmt.Enum declaration => declaration.Name,
            Stmt.TypeAlias declaration => declaration.Name, Stmt.Namespace declaration => declaration.Name,
            Stmt.ImportAlias declaration => declaration.AliasName, Stmt.ImportRequire declaration => declaration.AliasName,
            _ => null,
        };
        if (name is null) return;
        if (statement is not Stmt.Interface and not Stmt.TypeAlias && _environment.Get(name.Lexeme) is { } value)
        {
            RecordEditorFinalDeclaration(name, value);
            if (statement is Stmt.Class) CaptureEditorClassDeclarations(statement, value);
        }
        if (statement is Stmt.Class or Stmt.Interface or Stmt.Enum or Stmt.TypeAlias or Stmt.Namespace &&
            _environment.GetTypeBinding(name.Lexeme) is { } type)
            RecordEditorFinalDeclaration(name, type, BindingNamespace.Type);
    }

    private EditorSourceSlot? GetEditorSignatureSource(TypeInfo signature) =>
        _editorSignatureSources?.GetValueOrDefault(signature);

    private IReadOnlyList<TypeInfo>? GetEditorPublicSignatures(TypeInfo signature) =>
        _editorPublicSignatures?.GetValueOrDefault(signature);

    private void RegisterEditorPublicSignatures(TypeInfo actual, IReadOnlyList<TypeInfo> signatures)
    {
        if (!EditorFacts.IsEnabled) return;
        // One sentinel preserves incompleteness without retaining an unbounded public family.
        (_editorPublicSignatures ??= new(ReferenceEqualityComparer.Instance))[actual] = signatures.Take(33).ToArray();
    }

    private EditorSourceSlot? GetEditorClassSource(TypeInfo type) =>
        Members.GetClassInfo(SelectedSourceClassId(type)) is { } source
            ? new(source.Document, source.Owner, source.Owner switch
            {
                Stmt.Class declaration => declaration.Name,
                Expr.ClassExpr expression => expression.Name,
                _ => null,
            })
            : null;

    private void RegisterEditorSignature(TypeInfo signature, object owner, Token name, int? ordinal = null)
    {
        if (GetEditorDeclaration(name) is not { } source) return;
        (_editorSignatureSources ??= new(ReferenceEqualityComparer.Instance))[signature] =
            new(CurrentSourceDocument, owner, source.Name, ordinal);
    }

    private void CaptureEditorGlobals(TypeEnvironment environment)
    {
        if (!ShouldCaptureEditorFacts) return;
        int scope = EnsureEditorGlobalScope();
        var seenValues = new HashSet<string>(StringComparer.Ordinal);
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);
        for (TypeEnvironment? current = environment; current is not null; current = current.Enclosing)
        {
            foreach (string name in current.Names)
                if (seenValues.Add(name) && (current.GetValueBinding(name) is not { } symbol ||
                    _editorNonGlobalBindings?.Contains(symbol) != true))
                    EditorFacts.BindLocal(scope, name, BindingNamespace.Value, current.GetValueBinding(name), current.Get(name));
            foreach (string name in current.TypeNames.Concat(current.TypeBindingNames))
                if (seenTypes.Add(name) && (current.GetTypeSymbol(name) is not { } symbol ||
                    _editorNonGlobalBindings?.Contains(symbol) != true))
                    EditorFacts.BindLocal(scope, name, BindingNamespace.Type, current.GetTypeSymbol(name), current.GetTypeBinding(name));
        }
    }
}
