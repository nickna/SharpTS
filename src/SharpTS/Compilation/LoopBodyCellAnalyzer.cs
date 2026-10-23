using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation;

/// <summary>
/// Selects live lexical cells for residual generator expressions and synchronous
/// for-of and for body closures.
/// Identity is the original declaration token, rather than the callable's name set.
/// </summary>
internal sealed class LoopBodyCellAnalyzer : AstVisitorBase
{
    private sealed class Owner(object node, bool canOwnCells, bool canForward,
        bool canOwnGeneratorCells = true, bool canRelayClassSelf = true,
        Expr.ClassExpr? classCaptureOwner = null)
    {
        public object Node { get; } = node;
        public bool CanOwnCells { get; } = canOwnCells;
        public bool CanForward { get; } = canForward;
        public bool CanOwnGeneratorCells { get; } = canOwnGeneratorCells;
        public bool CanRelayClassSelf { get; } = canRelayClassSelf;
        public Expr.ClassExpr? ClassCaptureOwner { get; } = classCaptureOwner;
    }

    private sealed class Binding(Token name, Owner owner, bool candidate, Binding? shadowOf, bool lexical,
        bool ordinaryHandlerCandidate, object? moduleOwner)
    {
        public Token Name { get; } = name;
        public Owner Owner { get; } = owner;
        public bool Candidate { get; } = candidate;
        public bool Eligible { get; set; } = candidate;
        public bool Captured { get; set; }
        public Binding? ShadowOf { get; } = shadowOf;
        public bool Lexical { get; } = lexical;
        public bool GeneratorCaptured { get; set; }
        public bool GeneratorEligible { get; set; } = lexical;
        public bool OrdinaryHandlerCandidate { get; } = ordinaryHandlerCandidate;
        public object? ModuleOwner { get; } = moduleOwner;
        public bool ParameterBodyCopy { get; set; }
        public List<Stmt>? FunctionBody { get; set; }
        public bool BodyVarDeclared { get; set; }
        public bool NeedsUndefinedCell { get; set; }
        public bool ParameterPattern { get; set; }
        public bool ClassStaticBlockVar { get; set; }
    }

    private readonly List<Owner> _owners = [];
    private readonly Stack<Dictionary<string, Binding>> _scopes = new();
    private readonly Dictionary<Token, Binding> _declarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.Variable, Binding> _variableBindings = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Dictionary<string, Binding>> _moduleRootBindings = new(ReferenceEqualityComparer.Instance);
    private readonly object _singleSourceRootOwner = new();
    private readonly Dictionary<object, Dictionary<string, Binding>> _tentative = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Token> _cells = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, HashSet<string>> _cellOnlyCapturedNames = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Token> _ordinaryBlockShadows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<List<Stmt>, IReadOnlyList<(Token Declaration, Token Parameter)>> _parameterBodyCopies = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<List<Stmt>, List<Token>> _undefinedBodyVars = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, Token> _functionBodyVarDeclarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, Expr.ClassExpr> _classSelfDeclarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.Variable, Expr.ClassExpr> _classSelfReferences = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.Variable> _transportedClassSelfReferences = new(ReferenceEqualityComparer.Instance);
    private int _loopBodyDepth;
    private int _classDepth;
    private readonly Dictionary<List<Stmt>, IReadOnlyList<Token>> _classStaticBlockVars = new(ReferenceEqualityComparer.Instance);
    private int? _staticBlockClassDepth;
    private IReadOnlyDictionary<Stmt.Class, Expr.ClassExpr>? _runtimeClassDeclarations;
    private object? _currentModuleOwner;

    public Dictionary<object, Dictionary<string, Token>> ClosureCaptures { get; } = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Dictionary<string, Token>> _privateSelfCaptures = new(ReferenceEqualityComparer.Instance);
    public IEnumerable<Token> CellDeclarations => _cells;
    public bool IsCellDeclaration(Token token) => _cells.Contains(token);
    public object? GetCellOwner(Token token) => _declarations.GetValueOrDefault(token)?.Owner.Node;
    public bool IsOrdinaryBlockShadowDeclaration(Token token) => _ordinaryBlockShadows.Contains(token);
    public bool IsFunctionBodyVarDeclaration(Token token) => _functionBodyVarDeclarations.ContainsKey(token);
    public Token? GetFunctionBodyVarDeclaration(Token token) => _functionBodyVarDeclarations.GetValueOrDefault(token);
    public IReadOnlyList<(Token Declaration, Token Parameter)> GetParameterBodyCopies(List<Stmt> body) =>
        _parameterBodyCopies.GetValueOrDefault(body) ?? [];
    public IReadOnlyList<Token> GetUndefinedBodyVars(List<Stmt> body) => _undefinedBodyVars.GetValueOrDefault(body) ?? [];
    public IReadOnlyList<Token> GetClassStaticBlockVars(List<Stmt> body) => _classStaticBlockVars.GetValueOrDefault(body) ?? [];
    public void MarkUndefinedBodyVarReferences(TypeSystem.TypeMap typeMap)
    {
        foreach (var (reference, binding) in _variableBindings)
            if (binding.NeedsUndefinedCell && !binding.ParameterBodyCopy)
                typeMap.MarkUndefinedReachableReturn(reference);
    }
    public HashSet<string> GetCellOnlyCapturedNames(object owner) =>
        _cellOnlyCapturedNames.GetValueOrDefault(owner) ?? [];
    public bool TryGetClassSelfOwner(Expr.Variable reference, out Expr.ClassExpr owner) =>
        _classSelfReferences.TryGetValue(reference, out owner!);
    public bool CanTransportClassSelf(Expr.Variable reference) => _transportedClassSelfReferences.Contains(reference);
    public bool IsReferenceToDeclaration(Expr.Variable reference, Token declaration) =>
        _variableBindings.TryGetValue(reference, out var binding) && ReferenceEquals(binding.Name, declaration);
    public bool TryGetPrivateSelfCapture(object closure, string name, out Token declaration)
    {
        if (_privateSelfCaptures.TryGetValue(closure, out var captures) && captures.TryGetValue(name, out declaration!))
            return true;
        declaration = null!;
        return false;
    }

    public bool TryGetCaptureDeclaration(object closure, string name, out Token declaration)
    {
        if (ClosureCaptures.TryGetValue(closure, out var captures) && captures.TryGetValue(name, out declaration!))
            return true;
        declaration = null!;
        return false;
    }

    public void Analyze(List<Stmt> statements, IReadOnlyDictionary<Stmt, object>? moduleOwners = null,
        IReadOnlyDictionary<Stmt.Class, Expr.ClassExpr>? runtimeClassDeclarations = null)
    {
        _owners.Clear(); _scopes.Clear(); _declarations.Clear(); _tentative.Clear(); _cells.Clear(); ClosureCaptures.Clear();
        _privateSelfCaptures.Clear();
        _variableBindings.Clear();
        _classSelfDeclarations.Clear();
        _moduleRootBindings.Clear();
        _ordinaryBlockShadows.Clear();
        _parameterBodyCopies.Clear();
        _undefinedBodyVars.Clear();
        _classStaticBlockVars.Clear();
        _functionBodyVarDeclarations.Clear();
        _cellOnlyCapturedNames.Clear();
        _classSelfReferences.Clear();
        _transportedClassSelfReferences.Clear();
        _loopBodyDepth = 0;
        _classDepth = 0;
        _staticBlockClassDepth = null;
        _runtimeClassDeclarations = runtimeClassDeclarations;
        _currentModuleOwner = null;
        _owners.Add(new(new object(), !HasDirectSuspension(statements), false));
        EnterScope(statements, allowCandidates: false, hoistVars: true, moduleOwners: moduleOwners);
        try
        {
            foreach (var statement in statements)
            {
                _currentModuleOwner = moduleOwners?.GetValueOrDefault(statement);
                Visit(statement);
            }
        }
        finally { _scopes.Pop(); _owners.Clear(); _currentModuleOwner = null; }

        foreach (var binding in _declarations.Values)
        {
            if (binding.ClassStaticBlockVar && binding.Captured && binding.Eligible)
                binding.NeedsUndefinedCell = true;
            if (binding.ParameterBodyCopy || binding.NeedsUndefinedCell || binding.ParameterPattern && binding.Captured
                || binding.Eligible && binding.Captured && binding.ShadowOf is null)
                _cells.Add(binding.Name);
            if (binding.NeedsUndefinedCell && !binding.ParameterBodyCopy && binding.FunctionBody is { } body)
            {
                if (!_undefinedBodyVars.TryGetValue(body, out var declarations))
                    _undefinedBodyVars[body] = declarations = [];
                declarations.Add(binding.Name);
            }
        }
        HashSet<Token> generatorCells = new(ReferenceEqualityComparer.Instance);
        foreach (var binding in _declarations.Values.Where(b => b.GeneratorCaptured))
        {
            if (!binding.GeneratorEligible)
                throw new CompileException($"Generator lexical capture '{binding.Name.Lexeme}' crosses an unsupported closure or binding scope.");
            _cells.Add(binding.Name);
            generatorCells.Add(binding.Name);
        }
        bool added;
        do
        {
            added = false;
            foreach (var binding in _declarations.Values)
            {
                if (binding.ShadowOf is not { } parent || !_cells.Contains(parent.Name)) continue;
                if (generatorCells.Contains(parent.Name))
                {
                    if (!binding.GeneratorEligible)
                        throw new CompileException($"Generator lexical shadow '{binding.Name.Lexeme}' crosses an unsupported closure or binding scope.");
                    added |= _cells.Add(binding.Name);
                    added |= generatorCells.Add(binding.Name);
                }
                else if (binding.Captured && binding.Eligible) added |= _cells.Add(binding.Name);
            }
        } while (added);
        foreach (var owner in _declarations.Values.Where(binding => binding.Captured)
            .GroupBy(binding => binding.Owner.Node, ReferenceEqualityComparer.Instance))
        {
            var names = owner.GroupBy(binding => binding.Name.Lexeme, StringComparer.Ordinal)
                .Where(bindings => bindings.All(binding => _cells.Contains(binding.Name)))
                .Select(bindings => bindings.Key).ToHashSet(StringComparer.Ordinal);
            if (names.Count > 0) _cellOnlyCapturedNames[owner.Key] = names;
        }
        foreach (var (closure, captures) in _tentative)
            foreach (var (name, binding) in captures)
                if (IsAsyncPrivateSelfBinding(binding))
                {
                    // A private function name carries the function value, never a lexical box.
                    if (!_privateSelfCaptures.TryGetValue(closure, out var output))
                        _privateSelfCaptures[closure] = output = new(StringComparer.Ordinal);
                    output[name] = binding.Name;
                }
                else if (_cells.Contains(binding.Name))
                {
                    if (!ClosureCaptures.TryGetValue(closure, out var output))
                        ClosureCaptures[closure] = output = new(StringComparer.Ordinal);
                    output[name] = binding.Name;
                }
    }

    private Binding? VisibleBinding(string name)
    {
        foreach (var scope in _scopes)
            if (scope.TryGetValue(name, out var binding)) return binding;
        return null;
    }

    private Binding? VisibleOrdinaryBlockBinding(string name)
    {
        int remainingScopes = _scopes.Count;
        foreach (var scope in _scopes)
        {
            // Closure analysis shares a flattened root dictionary across modules.
            // Only the new shadow policy uses the actual module's root bindings.
            if (--remainingScopes == 0)
                return _moduleRootBindings.GetValueOrDefault(_currentModuleOwner ?? _singleSourceRootOwner)?.GetValueOrDefault(name);
            if (scope.TryGetValue(name, out var binding)) return binding;
        }
        return null;
    }

    private void Declare(Token name, bool candidate = false, bool unsupportedShadow = false, Binding? shadowOf = null,
        bool lexical = false, bool ordinaryHandlerCandidate = false, bool rootOwned = true)
    {
        // Header, catch and switch lists have their own selected-cell scope
        // emitters. A same-name binding must mask a selected generator cell
        // even when this inner binding is never captured itself.
        if (lexical && !candidate && shadowOf == null
            && VisibleBinding(name.Lexeme) is { } parent
            && parent is { Candidate: true } or { Lexical: true })
            shadowOf = parent;
        if (unsupportedShadow)
            foreach (var scope in _scopes)
                if (scope.TryGetValue(name.Lexeme, out var outer))
                {
                    if (!ReferenceEquals(outer.Name, name))
                    {
                        outer.Eligible = false;
                        // Header/catch/switch lexical shadows get their own cell scopes;
                        // opaque declaration forms still cannot carry a selected parent.
                        if (!lexical) outer.GeneratorEligible = false;
                    }
                    break;
                }
        if (!_declarations.TryGetValue(name, out var binding))
            _declarations[name] = binding = new(name, _owners[^1], candidate, shadowOf, lexical, ordinaryHandlerCandidate, _currentModuleOwner);
        if (lexical && !_owners[^1].CanOwnGeneratorCells) binding.GeneratorEligible = false;
        _scopes.Peek()[name.Lexeme] = binding;
        if (_scopes.Count == 1 && rootOwned)
        {
            var module = _currentModuleOwner ?? _singleSourceRootOwner;
            if (!_moduleRootBindings.TryGetValue(module, out var roots))
                _moduleRootBindings[module] = roots = new(StringComparer.Ordinal);
            roots[name.Lexeme] = binding;
        }
    }

    private void EnterScope(IEnumerable<Stmt> statements, bool allowCandidates, bool hoistVars = false,
        bool unsupportedShadows = false, bool generatorLexicals = false, bool ordinaryHandler = false,
        bool ordinaryBlock = false, IReadOnlyDictionary<Stmt, object>? moduleOwners = null)
    {
        _scopes.Push(new(StringComparer.Ordinal));
        if (hoistVars)
        {
            var vars = new VarCollector();
            foreach (var statement in statements)
            {
                vars.Names.Clear();
                vars.OwnedNames.Clear();
                vars.Visit(statement);
                var previousModule = _currentModuleOwner;
                if (moduleOwners is not null) _currentModuleOwner = moduleOwners.GetValueOrDefault(statement);
                try { foreach (var token in vars.Names) Declare(token, rootOwned: vars.OwnedNames.Contains(token)); }
                finally { _currentModuleOwner = previousModule; }
            }
        }
        foreach (var statement in statements)
        {
            var previousModule = _currentModuleOwner;
            if (moduleOwners is not null) _currentModuleOwner = moduleOwners.GetValueOrDefault(statement);
            try { Predeclare(statement, allowCandidates, unsupportedShadows, generatorLexicals, ordinaryHandler, ordinaryBlock); }
            finally { _currentModuleOwner = previousModule; }
        }
    }

    private void Predeclare(Stmt statement, bool allowCandidates, bool unsupportedShadows, bool generatorLexicals,
        bool ordinaryHandler, bool ordinaryBlock)
    {
        void Lexical(Token name)
        {
            bool handlerCandidate = ordinaryHandler && _owners[^1].CanOwnCells;
            bool loopCandidate = allowCandidates && _loopBodyDepth > 0 && _owners[^1].CanOwnCells;
            bool blockShadow = ordinaryBlock && VisibleOrdinaryBlockBinding(name.Lexeme) is { } outer
                && !ReferenceEquals(outer.Name, name)
                && ReferenceEquals(outer.ModuleOwner, _currentModuleOwner);
            if (blockShadow) _ordinaryBlockShadows.Add(name);
            // A captured ordinary block shadow must own storage independently of
            // the outer parameter/local, even when that outer binding has no cell.
            // Existing loop candidates retain their broader forwarding policy.
            bool asyncFunctionBlock = _classDepth == 0 && _owners[^1].Node is
                Stmt.Function { IsAsync: true, IsGenerator: false };
            bool blockCandidate = blockShadow && !loopCandidate &&
                (_owners[^1].CanOwnCells || asyncFunctionBlock);
            bool direct = handlerCandidate || loopCandidate || blockCandidate;
            var parent = !direct && allowCandidates && _owners[^1].CanOwnGeneratorCells
                ? VisibleBinding(name.Lexeme) : null;
            bool relatedShadow = parent is { Candidate: true } or { Lexical: true };
            // A callback's inner shadow also needs a cell when it masks an actual
            // outer cell. Final selection waits for that parent's selected status.
            Declare(name, direct || (relatedShadow && _owners[^1].CanOwnCells),
                unsupportedShadows && !handlerCandidate, relatedShadow ? parent : null,
                lexical: generatorLexicals, ordinaryHandlerCandidate: handlerCandidate || blockCandidate);
        }
        switch (statement)
        {
            case Stmt.Var variable when !variable.IsVar: Lexical(variable.Name); break;
            case Stmt.Const constant: Lexical(constant.Name); break;
            case Stmt.Function function: Declare(function.Name, unsupportedShadow: true); break;
            case Stmt.Class @class: Declare(@class.Name, unsupportedShadow: true); break;
            case Stmt.Enum @enum: Declare(@enum.Name, unsupportedShadow: true); break;
            case Stmt.Namespace @namespace: Declare(@namespace.Name, unsupportedShadow: true); break;
            case Stmt.Using resource:
                foreach (var binding in resource.Bindings)
                    if (binding.Name is { } name) Declare(name, unsupportedShadow: true);
                    else foreach (var scope in _scopes)
                        foreach (var outer in scope.Values)
                        {
                            outer.Eligible = false;
                            outer.GeneratorEligible = false;
                        }
                break;
            case Stmt.Sequence sequence:
                foreach (var child in sequence.Statements) Predeclare(child, allowCandidates, unsupportedShadows, generatorLexicals, ordinaryHandler, ordinaryBlock);
                break;
        }
    }

    private void Reference(string name)
    {
        var binding = VisibleBinding(name);
        if (binding is null) return;
        if (binding.ParameterPattern)
        {
            if (ReferenceEquals(binding.Owner, _owners[^1])) return;
            binding.Captured = true;
            for (int index = _owners.Count - 1; index >= 0 && !ReferenceEquals(_owners[index], binding.Owner); index--)
                RecordCapture(_owners[index].Node, name, binding);
            return;
        }
        if (binding.ClassStaticBlockVar)
        {
            if (ReferenceEquals(binding.Owner, _owners[^1])) return;
            binding.Captured = true;
            for (int index = _owners.Count - 1; index >= 0 && !ReferenceEquals(_owners[index], binding.Owner); index--)
            {
                var owner = _owners[index];
                if (!owner.CanForward || owner.Node is not Expr.ArrowFunction { IsAsync: false, IsGenerator: false })
                { binding.Eligible = false; continue; }
                RecordCapture(owner.Node, name, binding);
            }
            return;
        }
        if (binding.FunctionBody != null)
        {
            // The binding exists from body entry, while its initializer still
            // runs at the original statement. Hoisted declarations can be called
            // before either their own source position or the var's initializer.
            if (!binding.BodyVarDeclared) binding.NeedsUndefinedCell = true;
            if (ReferenceEquals(binding.Owner, _owners[^1])) return;
            binding.Captured = true;
            for (int index = _owners.Count - 1; index >= 0 && !ReferenceEquals(_owners[index], binding.Owner); index--)
            {
                if (_owners[index].Node is Stmt.Function) binding.NeedsUndefinedCell = true;
                RecordCapture(_owners[index].Node, name, binding);
            }
            return;
        }
        if (ReferenceEquals(binding.Owner, _owners[^1])) return;
        binding.Captured = true;
        if (IsAsyncPrivateSelfBinding(binding))
            for (int index = _owners.Count - 1; index >= 0 && !ReferenceEquals(_owners[index], binding.Owner); index--)
                RecordCapture(_owners[index].Node, name, binding);
        if (binding.Lexical)
        {
            bool generator = false;
            for (int index = _owners.Count - 1; index >= 0 && !ReferenceEquals(_owners[index], binding.Owner); index--)
            {
                var node = _owners[index].Node;
                generator |= node is Expr.ArrowFunction { IsGenerator: true };
                bool supported = node is Expr.ArrowFunction { IsGenerator: true }
                    or Expr.ArrowFunction { IsAsync: false, IsGenerator: false };
                if (!supported) binding.GeneratorEligible = false;
                else RecordCapture(node, name, binding);
            }
            binding.GeneratorCaptured |= generator;
        }
        if (!binding.Candidate) return;

        for (int index = _owners.Count - 1; index >= 0 && !ReferenceEquals(_owners[index], binding.Owner); index--)
        {
            var owner = _owners[index];
            // Only a direct synchronous member may relay through its evaluated
            // class definition. Class evaluation, static blocks and nested
            // callable barriers keep their existing eligibility rules.
            bool classCaptureRelay = owner.ClassCaptureOwner is { } expression &&
                (!ReferenceEquals(owner.Node, expression) || index + 1 < _owners.Count &&
                    ReferenceEquals(_owners[index + 1].ClassCaptureOwner, expression));
            if (!owner.CanForward && !classCaptureRelay || binding.OrdinaryHandlerCandidate &&
                owner.Node is not Expr.ArrowFunction { IsAsync: false, IsGenerator: false })
            { binding.Eligible = false; continue; }
            RecordCapture(owner.Node, name, binding);
        }
    }

    private static bool IsAsyncPrivateSelfBinding(Binding binding) =>
        binding.Owner.Node is Expr.ArrowFunction { IsAsync: true, IsGenerator: false, Name: { } name }
        && ReferenceEquals(binding.Name, name);

    private void RecordCapture(object node, string name, Binding binding)
    {
        if (!_tentative.TryGetValue(node, out var captures))
            _tentative[node] = captures = new(StringComparer.Ordinal);
        if (captures.TryGetValue(name, out var previous) && !ReferenceEquals(previous, binding))
        {
            // One name-keyed closure field cannot hold two lexical declarations.
            previous.Eligible = false;
            binding.Eligible = false;
            previous.GeneratorEligible = false;
            binding.GeneratorEligible = false;
        }
        else captures[name] = binding;
    }

    protected override void VisitVariable(Expr.Variable expr)
    {
        var binding = VisibleBinding(expr.Name.Lexeme);
        if (binding != null) _variableBindings[expr] = binding;
        if (binding != null &&
            _classSelfDeclarations.TryGetValue(binding.Name, out var owner))
        {
            _classSelfReferences[expr] = owner;
            bool canTransport = true;
            for (int index = _owners.Count - 1;
                 index >= 0 && !ReferenceEquals(_owners[index], binding.Owner); index--)
                canTransport &= _owners[index].CanRelayClassSelf;
            if (canTransport) _transportedClassSelfReferences.Add(expr);
            return;
        }
        Reference(expr.Name.Lexeme);
    }
    protected override void VisitAssign(Expr.Assign expr) { Reference(expr.Name.Lexeme); base.VisitAssign(expr); }
    protected override void VisitCompoundAssign(Expr.CompoundAssign expr) { Reference(expr.Name.Lexeme); base.VisitCompoundAssign(expr); }
    protected override void VisitLogicalAssign(Expr.LogicalAssign expr) { Reference(expr.Name.Lexeme); base.VisitLogicalAssign(expr); }

    protected override void VisitVar(Stmt.Var stmt)
    {
        // Self-initializers must still see the binding's initial undefined value.
        base.VisitVar(stmt);
        if (stmt.IsVar && _functionBodyVarDeclarations.TryGetValue(stmt.Name, out var canonical))
            _declarations[canonical].BodyVarDeclared = true;
    }

    protected override void VisitBlock(Stmt.Block stmt)
    {
        EnterScope(stmt.Statements, allowCandidates: true, generatorLexicals: true, ordinaryBlock: true);
        try { base.VisitBlock(stmt); }
        finally { _scopes.Pop(); }
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        Visit(stmt.Iterable); // Iterator acquisition precedes the loop binding's scope.
        _scopes.Push(new(StringComparer.Ordinal));
        if (stmt.IsDeclaration && !stmt.IsVar)
            Declare(stmt.Variable,
                candidate: VisibleBinding(stmt.Variable.Lexeme) != null && _owners[^1].CanOwnGeneratorCells,
                unsupportedShadow: true, lexical: true);
        else if (!stmt.IsDeclaration) Reference(stmt.Variable.Lexeme);
        try { VisitLoopBody(stmt.Body, !stmt.IsAsync); }
        finally { _scopes.Pop(); }
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        Visit(stmt.Object);
        _scopes.Push(new(StringComparer.Ordinal));
        if (stmt.IsDeclaration && !stmt.IsVar)
            Declare(stmt.Variable,
                candidate: VisibleBinding(stmt.Variable.Lexeme) != null && _owners[^1].CanOwnGeneratorCells,
                unsupportedShadow: true, lexical: true);
        else if (!stmt.IsDeclaration) Reference(stmt.Variable.Lexeme);
        try { Visit(stmt.Body); }
        finally { _scopes.Pop(); }
    }

    protected override void VisitFor(Stmt.For stmt)
    {
        EnterScope(stmt.Initializer is null ? [] : [stmt.Initializer], allowCandidates: false,
            unsupportedShadows: true, generatorLexicals: true, ordinaryBlock: _owners[^1].CanOwnCells);
        try
        {
            // Header bindings keep the separate copy-forward cell policy (#650).
            if (stmt.Initializer is { } initializer) Visit(initializer);
            if (stmt.Condition is { } condition) Visit(condition);
            if (stmt.Increment is { } increment) Visit(increment);
            VisitLoopBody(stmt.Body);
        }
        finally { _scopes.Pop(); }
    }

    private void VisitLoopBody(Stmt body, bool synchronous = true)
    {
        int previousDepth = _loopBodyDepth;
        _loopBodyDepth = synchronous && _owners[^1].CanOwnCells && !HasDirectSuspension([body])
            ? previousDepth + 1 : 0;
        try { Visit(body); }
        finally { _loopBodyDepth = previousDepth; }
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        VisitStatementScope(stmt.TryBlock, ordinaryHandler: true);
        if (stmt.CatchBlock is { } catchBlock)
        {
            EnterScope(catchBlock, allowCandidates: false, unsupportedShadows: true,
                generatorLexicals: true, ordinaryHandler: true);
            if (stmt.CatchParam is { } parameter)
            {
                bool candidate = _owners[^1].CanOwnCells;
                Declare(parameter, candidate, unsupportedShadow: !candidate, lexical: true,
                    ordinaryHandlerCandidate: candidate);
            }
            try { foreach (var statement in catchBlock) Visit(statement); }
            finally { _scopes.Pop(); }
        }
        if (stmt.FinallyBlock is { } finallyBlock) VisitStatementScope(finallyBlock, ordinaryHandler: true);
    }

    private void VisitStatementScope(List<Stmt> statements, bool ordinaryHandler = false)
    {
        // These AST lists are not emitted through EmitBlock's cell prologue.
        EnterScope(statements, allowCandidates: false, unsupportedShadows: true,
            generatorLexicals: true, ordinaryHandler: ordinaryHandler);
        try { foreach (var statement in statements) Visit(statement); }
        finally { _scopes.Pop(); }
    }

    protected override void VisitSwitch(Stmt.Switch stmt)
    {
        Visit(stmt.Subject);
        var statements = stmt.Cases.SelectMany(c => c.Body).Concat(stmt.DefaultBody ?? []).ToList();
        EnterScope(statements, allowCandidates: false, unsupportedShadows: true, generatorLexicals: true);
        try
        {
            foreach (var @case in stmt.Cases)
            {
                Visit(@case.Value);
                foreach (var statement in @case.Body) Visit(statement);
            }
            if (stmt.DefaultBody is { } defaults) foreach (var statement in defaults) Visit(statement);
        }
        finally { _scopes.Pop(); }
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        VisitCallable(expr, expr.Parameters, expr.BlockBody ?? [], expr.ExpressionBody,
            !expr.IsAsync && !expr.IsGenerator, expr.HasOwnThis ? expr.Name : null);
    }

    protected override void VisitFunction(Stmt.Function stmt)
    {
        if (stmt.ComputedKey is { } key) Visit(key);
        VisitCallable(stmt, stmt.Parameters, stmt.Body ?? [], null,
            !stmt.IsAsync && !stmt.IsGenerator, stmt.Name, canRelayClassSelf: false);
    }

    private void VisitCallable(object node, List<Stmt.Parameter> parameters, List<Stmt> body,
        Expr? expressionBody, bool synchronous, Token? self, bool canOwnGeneratorCells = true,
        bool canRelayClassSelf = true, Expr.ClassExpr? classCaptureOwner = null)
    {
        int previousDepth = _loopBodyDepth;
        _loopBodyDepth = 0;
        bool directSuspension = HasDirectSuspension(body, expressionBody);
        bool localCellScope = _classDepth == 0 || _staticBlockClassDepth == _classDepth;
        _owners.Add(new(node, synchronous && localCellScope && !directSuspension,
            node is Expr.ArrowFunction { IsGenerator: false } && localCellScope,
            canOwnGeneratorCells, canRelayClassSelf,
            synchronous && !directSuspension ? classCaptureOwner : null));
        EnterScope([], allowCandidates: false);
        try
        {
            if (self is not null) Declare(self);
            if (node is Stmt.Function or Expr.ArrowFunction { HasOwnThis: true })
                Declare(new Token(TokenType.IDENTIFIER, "arguments", null, 0));
            foreach (var parameter in parameters) Declare(parameter.Name);
            var initializers = Runtime.ParameterBinder.ParameterInitializers(body);
            foreach (var initializer in initializers?.Values.AsEnumerable() ?? [])
                foreach (var name in LexicalDeclarationBindings.Collect(initializer.Statements))
                {
                    Declare(name);
                    _declarations[name].ParameterPattern = synchronous && !directSuspension;
                }
            foreach (var parameter in parameters)
            {
                if (parameter.DefaultValue is { } value) Visit(value);
                if (initializers?.TryGetValue(parameter.Name.Lexeme, out var initializer) == true)
                    Visit(initializer);
            }
            var statements = Runtime.ParameterBinder.BodyStatements(body);
            EnterScope(statements, allowCandidates: false, hoistVars: true);
            try
            {
                if (synchronous && !directSuspension) PrepareFunctionBodyVars(parameters, body);
                if (expressionBody is not null) Visit(expressionBody);
                foreach (var statement in statements) Visit(statement);
            }
            finally { _scopes.Pop(); }
        }
        finally { _scopes.Pop(); _owners.RemoveAt(_owners.Count - 1); _loopBodyDepth = previousDepth; }
    }

    private void PrepareFunctionBodyVars(List<Stmt.Parameter> parameters, List<Stmt> body)
    {
        var vars = new VarCollector();
        foreach (var statement in body) vars.Visit(statement);
        var functionNames = Runtime.ParameterBinder.BodyVarDeclarations(body)
            .Where(declaration => declaration.IsFunction)
            .Select(declaration => declaration.Name.Lexeme).ToHashSet(StringComparer.Ordinal);
        var initializers = Runtime.ParameterBinder.ParameterInitializers(body);
        var parameterNames = parameters.Select(parameter => parameter.Name)
            .Concat((initializers?.Values.AsEnumerable() ?? [])
                .SelectMany(initializer => LexicalDeclarationBindings.Collect(initializer.Statements)))
            .GroupBy(name => name.Lexeme)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        bool hasParameterExpressions = Runtime.ParameterBinder.HasParameterExpressions(parameters);
        var copies = new List<(Token Declaration, Token Parameter)>();
        foreach (var declarations in vars.Names.Where(vars.OwnedNames.Contains).GroupBy(token => token.Lexeme))
        {
            string name = declarations.Key;
            if (functionNames.Contains(name)) continue;
            parameterNames.TryGetValue(name, out var parameter);
            if (parameter != null && !hasParameterExpressions)
            {
                if (_declarations[parameter].ParameterPattern)
                {
                    _scopes.Peek()[name] = _declarations[parameter];
                    foreach (var declaration in declarations)
                        _functionBodyVarDeclarations[declaration] = parameter;
                }
                continue;
            }
            var binding = _declarations[declarations.First()];
            binding.FunctionBody = body;
            binding.ParameterBodyCopy = parameter != null;
            // Every redeclaration and var-loop header uses one function-owned
            // binding, regardless of its source token.
            _scopes.Peek()[name] = binding;
            foreach (var declaration in declarations)
                _functionBodyVarDeclarations[declaration] = binding.Name;
            if (parameter != null) copies.Add((binding.Name, parameter));
        }
        if (copies.Count > 0) _parameterBodyCopies[body] = copies;
    }

    protected override void VisitClass(Stmt.Class stmt)
    {
        if (_runtimeClassDeclarations?.TryGetValue(stmt, out var expression) == true)
        {
            VisitClassExpr(expression);
            return;
        }
        if (stmt.SuperclassExpr is { } parent) Visit(parent);
        VisitClassBody(stmt.Name, () =>
        {
            foreach (var field in stmt.Fields) VisitField(field);
            foreach (var method in stmt.Methods) VisitClassMethod(method);
            if (stmt.Accessors is { } accessors) foreach (var accessor in accessors) VisitAccessor(accessor);
            if (stmt.AutoAccessors is { } autoAccessors) foreach (var accessor in autoAccessors) VisitAutoAccessor(accessor);
            if (stmt.StaticInitializers is { } initializers)
                foreach (var initializer in initializers)
                    if (initializer is Stmt.StaticBlock block)
                        VisitClassStaticBlock(block, includeVars: false);
                    else Visit(initializer);
        });
    }

    protected override void VisitClassExpr(Expr.ClassExpr expr)
    {
        if (expr.SuperclassExpr is { } parent) Visit(parent);
        if (expr.Name is { } name) _classSelfDeclarations[name] = expr;
        VisitClassBody(expr.Name, () =>
        {
            foreach (var field in expr.Fields) VisitField(field);
            foreach (var method in expr.Methods) VisitClassMethod(method);
            if (expr.Accessors is { } accessors) foreach (var accessor in accessors) VisitAccessor(accessor);
            if (expr.AutoAccessors is { } autoAccessors) foreach (var accessor in autoAccessors) VisitAutoAccessor(accessor);
            if (expr.StaticInitializers is { } initializers)
                foreach (var initializer in initializers)
                    if (initializer is Stmt.StaticBlock block)
                        VisitClassStaticBlock(block, includeVars: true);
                    else Visit(initializer);
        }, expr);
    }

    private void VisitClassMethod(Stmt.Function method)
    {
        if (method.ComputedKey is { } key) Visit(key);
        // A method's property name does not introduce a callable self binding.
        VisitCallable(method, method.Parameters, method.Body ?? [], null,
            !method.IsAsync && !method.IsGenerator, self: null, canRelayClassSelf: true,
            classCaptureOwner: _owners[^1].Node as Expr.ClassExpr);
    }

    protected override void VisitAccessor(Stmt.Accessor accessor)
    {
        if (accessor.ComputedKey is { } key) Visit(key);
        VisitCallable(accessor, accessor.SetterParam is { } parameter ? [parameter] : [],
            accessor.Body, null, synchronous: true, self: null, canOwnGeneratorCells: false,
            classCaptureOwner: _owners[^1].Node as Expr.ClassExpr);
    }

    private void VisitClassStaticBlock(Stmt.StaticBlock block, bool includeVars)
    {
        // Each static block is one activation. Nested callables/classes retain
        // their existing cell-eligibility barriers.
        var previousDepth = _staticBlockClassDepth;
        _staticBlockClassDepth = _classDepth;
        _owners.Add(new(block, true, false, canOwnGeneratorCells: false));
        EnterScope(block.Body, allowCandidates: false, hoistVars: true, ordinaryHandler: true);
        try
        {
            if (includeVars) PrepareClassStaticBlockVars(block.Body);
            foreach (var statement in block.Body) Visit(statement);
        }
        finally
        {
            _scopes.Pop();
            _owners.RemoveAt(_owners.Count - 1);
            _staticBlockClassDepth = previousDepth;
        }
    }

    private void PrepareClassStaticBlockVars(List<Stmt> body)
    {
        // Match the bounded direct/Sequence and loop-head var inventory used by
        // interpreter static-block entry; nested ordinary var hoisting is unchanged.
        var declarations = Runtime.ParameterBinder.InterpreterBodyVarDeclarations(body).ToList();
        var functionNames = declarations.Where(declaration => declaration.IsFunction)
            .Select(declaration => declaration.Name.Lexeme).ToHashSet(StringComparer.Ordinal);
        List<Token> vars = [];
        foreach (var group in declarations.Where(declaration => !declaration.IsFunction)
            .GroupBy(declaration => declaration.Name.Lexeme, StringComparer.Ordinal))
        {
            if (functionNames.Contains(group.Key)) continue;
            var binding = _declarations[group.First().Name];
            binding.ClassStaticBlockVar = true;
            binding.FunctionBody = body;
            binding.Eligible = true;
            _scopes.Peek()[group.Key] = binding;
            foreach (var declaration in group)
                _functionBodyVarDeclarations[declaration.Name] = binding.Name;
            vars.Add(binding.Name);
        }
        _classStaticBlockVars[body] = vars;
    }

    protected override void VisitStaticBlock(Stmt.StaticBlock block)
    {
        // Static-block vars belong to this block, independently of the enclosing
        // function and other static blocks. These new bindings remain metadata
        // only under the existing class cell-eligibility barrier.
        EnterScope(block.Body, allowCandidates: false, hoistVars: true);
        try { foreach (var statement in block.Body) Visit(statement); }
        finally { _scopes.Pop(); }
    }

    protected override void VisitNamespace(Stmt.Namespace stmt) =>
        VisitClassBody(stmt.Name, () => VisitStatementScope(stmt.Members));

    private void VisitClassBody(Token? name, Action visit, Expr.ClassExpr? expression = null)
    {
        int previousDepth = _loopBodyDepth;
        _loopBodyDepth = 0;
        _classDepth++;
        _owners.Add(new(expression ?? (object)new object(), false, false,
            canOwnGeneratorCells: false, classCaptureOwner: expression));
        EnterScope([], allowCandidates: false);
        try { if (name is not null) Declare(name); visit(); }
        finally { _scopes.Pop(); _owners.RemoveAt(_owners.Count - 1); _classDepth--; _loopBodyDepth = previousDepth; }
    }

    private static bool HasDirectSuspension(IEnumerable<Stmt> statements, Expr? expression = null)
    {
        var visitor = new SuspensionVisitor();
        foreach (var statement in statements) visitor.Visit(statement);
        if (expression is not null) visitor.Visit(expression);
        return visitor.Found;
    }

    private sealed class SuspensionVisitor : AstVisitorBase
    {
        public bool Found { get; private set; }
        protected override void VisitAwait(Expr.Await expr) => Found = true;
        protected override void VisitYield(Expr.Yield expr) => Found = true;
        protected override void VisitForOf(Stmt.ForOf stmt)
        {
            if (stmt.IsAsync) Found = true;
            else base.VisitForOf(stmt);
        }
        protected override void VisitUsing(Stmt.Using stmt)
        {
            if (stmt.IsAsync) Found = true;
            else base.VisitUsing(stmt);
        }
        protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
        protected override void VisitFunction(Stmt.Function stmt) { }
        protected override void VisitClass(Stmt.Class stmt) =>
            VisitClassEvaluation(stmt.SuperclassExpr, stmt.Fields, stmt.Methods, stmt.Accessors);
        protected override void VisitClassExpr(Expr.ClassExpr expr) =>
            VisitClassEvaluation(expr.SuperclassExpr, expr.Fields, expr.Methods, expr.Accessors);

        private void VisitClassEvaluation(Expr? parent, List<Stmt.Field> fields,
            List<Stmt.Function> methods, List<Stmt.Accessor>? accessors)
        {
            // Heritage and computed names execute in the containing activation;
            // method bodies and instance initializers do not suspend that owner.
            if (parent is not null) Visit(parent);
            foreach (var field in fields) if (field.ComputedKey is { } key) Visit(key);
            foreach (var method in methods) if (method.ComputedKey is { } key) Visit(key);
            if (accessors is not null)
                foreach (var accessor in accessors) if (accessor.ComputedKey is { } key) Visit(key);
        }
    }

    private sealed class VarCollector : AstVisitorBase
    {
        // Keep the existing inventory unchanged; the subset is provenance for
        // the new ordinary-block shadow lookup, not a new var-hoisting policy.
        public List<Token> Names { get; } = [];
        public HashSet<Token> OwnedNames { get; } = new(ReferenceEqualityComparer.Instance);
        private int _independentOwnerDepth;

        private void Add(Token name, bool runtime = true)
        {
            Names.Add(name);
            if (_independentOwnerDepth == 0 && runtime) OwnedNames.Add(name);
        }

        protected override void VisitVar(Stmt.Var stmt) { if (stmt.IsVar) Add(stmt.Name, !stmt.IsDeclare); }
        protected override void VisitForOf(Stmt.ForOf stmt) { if (stmt.IsDeclaration && stmt.IsVar) Add(stmt.Variable); Visit(stmt.Body); }
        protected override void VisitForIn(Stmt.ForIn stmt) { if (stmt.IsDeclaration && stmt.IsVar) Add(stmt.Variable); Visit(stmt.Body); }
        protected override void VisitNamespace(Stmt.Namespace stmt)
        {
            _independentOwnerDepth++;
            try { base.VisitNamespace(stmt); }
            finally { _independentOwnerDepth--; }
        }
        protected override void VisitDeclareModule(Stmt.DeclareModule stmt)
        {
            _independentOwnerDepth++;
            try { base.VisitDeclareModule(stmt); }
            finally { _independentOwnerDepth--; }
        }
        protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
        protected override void VisitFunction(Stmt.Function stmt) { }
        protected override void VisitClass(Stmt.Class stmt) { }
        protected override void VisitClassExpr(Expr.ClassExpr expr) { }
    }
}
