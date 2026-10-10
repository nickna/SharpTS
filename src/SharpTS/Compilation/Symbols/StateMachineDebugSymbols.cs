using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation.Symbols;

/// <summary>
/// Preserves source binding identity separately from the storage names used by suspension lowering.
/// Hoisted slots are portable-PDB slots, rather than CLR local-signature slots.
/// </summary>
internal sealed class StateMachineDebugSymbols
{
    internal sealed class Scope(int depth, bool isRoot = false)
    {
        internal int Depth { get; } = depth;
        internal bool IsRoot { get; } = isRoot;
        internal int StartOffset { get; set; } = int.MaxValue;
        internal int EndOffset { get; set; }
    }

    internal sealed class Binding(object declaration, Token sourceToken, string storageName, Scope scope, bool isParameter)
    {
        internal object Declaration { get; } = declaration;
        internal string SourceName { get; } = sourceToken.Lexeme;
        internal int SourceOffset { get; } = sourceToken.Start;
        internal string StorageName { get; } = storageName;
        internal Scope LexicalScope { get; } = scope;
        internal bool IsParameter { get; } = isParameter;
        internal int Slot { get; set; }
        internal int ReservedDisplayClassSlot { get; set; }
        internal FieldBuilder? Field { get; set; }
        internal bool IsAuthoritative { get; set; } = true;
        internal int? DisplayClassSlot { get; set; }
        internal string FieldName => $"<{SourceName}>5__{Slot + 1}";
        internal string MachineFieldName => DisplayClassSlot is null ? FieldName : $"<>7__{StorageName}";
    }

    private readonly List<Binding> _bindings = [];
    private readonly Dictionary<object, List<Scope>> _statementScopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Binding> _byStorage = new(StringComparer.Ordinal);
    private readonly List<Scope?> _slots = [];
    private readonly Dictionary<int, Binding> _machineSlots = [];
    private readonly Scope _root = new(0, isRoot: true);

    internal IReadOnlyList<Binding> Bindings => _bindings;
    internal int SlotCount => _slots.Count;
    internal int DescendantSlotBase { get; private set; }

    internal static StateMachineDebugSymbols Create(
        IReadOnlyList<Stmt.Parameter> parameters,
        IReadOnlyList<Stmt>? body,
        IReadOnlyDictionary<object, string>? renames,
        int ancestorSlotCount = 0)
    {
        var symbols = new StateMachineDebugSymbols();
        foreach (Stmt.Parameter parameter in parameters)
            if (parameter.Name.Start >= 0)
                symbols.AddBinding(parameter, parameter.Name, parameter.Name.Lexeme, symbols._root, isParameter: true);

        if (body is not null)
            new BindingCollector(symbols, renames).Collect(body);

        // Expression evaluators choose the first live field for a source name. Emit inner bindings
        // before outer bindings, so lexical shadows resolve correctly while both slots are live.
        symbols._bindings.Sort((left, right) =>
        {
            int depth = right.LexicalScope.Depth.CompareTo(left.LexicalScope.Depth);
            return depth != 0 ? depth : left.SourceOffset.CompareTo(right.SourceOffset);
        });
        for (int slot = 0; slot < ancestorSlotCount; slot++) symbols._slots.Add(null);
        foreach (Binding binding in symbols._bindings)
        {
            binding.Slot = symbols._slots.Count;
            symbols._slots.Add(binding.LexicalScope);
            symbols._machineSlots.Add(binding.Slot, binding);
            // Reserve the authoritative closure slot before descendants define their fields. A
            // nested arrow can then reuse ancestor DC indices without colliding with its own locals.
            binding.ReservedDisplayClassSlot = symbols._slots.Count;
            symbols._slots.Add(null);
        }
        symbols.DescendantSlotBase = symbols._slots.Count;
        return symbols;
    }

    private void AddBinding(object declaration, Token token, string storage, Scope scope, bool isParameter = false)
    {
        if (token.Start < 0 || _byStorage.ContainsKey(storage)) return;
        var binding = new Binding(declaration, token, storage, scope, isParameter);
        _bindings.Add(binding);
        _byStorage.Add(storage, binding);
    }

    internal string SourceName(string storage) => _byStorage.TryGetValue(storage, out Binding? binding)
        ? binding.SourceName : storage;

    internal string DefineDisplayClassFieldName(string storage)
    {
        if (!_byStorage.TryGetValue(storage, out Binding? binding)) return storage;
        binding.IsAuthoritative = false;
        int slot = binding.DisplayClassSlot ?? binding.ReservedDisplayClassSlot;
        if (binding.DisplayClassSlot is null)
        {
            binding.DisplayClassSlot = slot;
            _slots[slot] = binding.LexicalScope;
        }
        return $"<{binding.SourceName}>5__{slot + 1}";
    }

    internal Binding? GetBinding(string storage) => _byStorage.GetValueOrDefault(storage);

    internal int AddDisplayClassSlot()
    {
        int slot = _slots.Count;
        _slots.Add(_root);
        return slot;
    }

    internal string AddCapturedValueField(string sourceName)
    {
        int slot = _slots.Count;
        _slots.Add(_root);
        DescendantSlotBase = Math.Max(DescendantSlotBase, _slots.Count);
        return $"<{sourceName}>5__{slot + 1}";
    }

    internal void ImportCapturedField(FieldBuilder field)
    {
        string name = field.Name;
        int separator = name.LastIndexOf(">5__", StringComparison.Ordinal);
        if (!name.StartsWith('<') || separator <= 0 || !int.TryParse(name.AsSpan(separator + 4), out int suffix)) return;
        int slot = suffix - 1;
        while (_slots.Count <= slot) _slots.Add(null);
        if (_machineSlots.ContainsKey(slot))
            throw new InvalidOperationException("An inherited capture slot overlaps this state machine's own source binding.");
        _slots[slot] = _root;
    }

    internal (int Start, int Length) GetScope(int slot, int methodSize)
    {
        if (_machineSlots.TryGetValue(slot, out Binding? binding) && (!binding.IsAuthoritative || binding.Field is null))
            return (0, 0);
        Scope? scope = _slots[slot];
        if (scope is null) return (0, 0);
        if (scope.IsRoot) return (0, methodSize);
        return scope.StartOffset == int.MaxValue || scope.EndOffset <= scope.StartOffset
            ? (0, 0) : (scope.StartOffset, scope.EndOffset - scope.StartOffset);
    }

    internal void RecordStatementStart(Stmt statement, int offset)
    {
        if (!_statementScopes.TryGetValue(statement, out List<Scope>? scopes)) return;
        foreach (Scope scope in scopes)
            scope.StartOffset = Math.Min(scope.StartOffset, offset);
    }

    internal void RecordStatementEnd(Stmt statement, int offset)
    {
        if (!_statementScopes.TryGetValue(statement, out List<Scope>? scopes)) return;
        foreach (Scope scope in scopes)
            scope.EndOffset = Math.Max(scope.EndOffset, offset);
    }

    private sealed class BindingCollector(
        StateMachineDebugSymbols symbols,
        IReadOnlyDictionary<object, string>? renames) : AstVisitorBase
    {
        private readonly List<Scope> _scopes = [symbols._root];

        internal void Collect(IReadOnlyList<Stmt> body)
        {
            foreach (Stmt statement in body) Visit(statement);
        }

        public override void Visit(Stmt statement)
        {
            Track(statement);
            base.Visit(statement);
        }

        private void Track(Stmt statement)
        {
            if (!symbols._statementScopes.TryGetValue(statement, out List<Scope>? scopes))
                symbols._statementScopes.Add(statement, scopes = []);
            foreach (Scope scope in _scopes)
                if (!scope.IsRoot && !scopes.Contains(scope)) scopes.Add(scope);
        }

        private string Storage(object node, Token token) => renames?.GetValueOrDefault(node) ?? token.Lexeme;

        private void Add(object node, Token token, bool functionScoped = false)
        {
            string storage = Storage(node, token);
            symbols.AddBinding(node, token, storage, functionScoped ? symbols._root : _scopes[^1]);
        }

        private void InScope(Action emit, Stmt? container = null)
        {
            _scopes.Add(new Scope(_scopes.Count));
            if (container is not null) Track(container);
            emit();
            _scopes.RemoveAt(_scopes.Count - 1);
        }

        protected override void VisitVar(Stmt.Var statement) => Add(statement, statement.Name, statement.IsVar);
        protected override void VisitConst(Stmt.Const statement) => Add(statement, statement.Name);
        protected override void VisitClass(Stmt.Class statement) => Add(statement, statement.Name);
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitBlock(Stmt.Block statement) => InScope(() => base.VisitBlock(statement), statement);
        protected override void VisitFor(Stmt.For statement) => InScope(() => base.VisitFor(statement), statement);
        protected override void VisitForOf(Stmt.ForOf statement) => InScope(() =>
        {
            if (statement.IsDeclaration) Add(statement, statement.Variable, statement.IsVar);
            Visit(statement.Body);
        }, statement);
        protected override void VisitForIn(Stmt.ForIn statement) => InScope(() =>
        {
            if (statement.IsDeclaration) Add(statement, statement.Variable, statement.IsVar);
            Visit(statement.Body);
        }, statement);
        protected override void VisitSwitch(Stmt.Switch statement) => InScope(() => base.VisitSwitch(statement), statement);
        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            InScope(() => Collect(statement.TryBlock));
            if (statement.CatchBlock is not null)
                InScope(() =>
                {
                    if (statement.CatchParam is not null) Add(statement, statement.CatchParam);
                    Collect(statement.CatchBlock);
                });
            if (statement.FinallyBlock is not null) InScope(() => Collect(statement.FinallyBlock));
        }
        protected override void VisitUsing(Stmt.Using statement)
        {
            foreach (Stmt.UsingBinding binding in statement.Bindings)
                if (binding.Name is not null) Add(binding, binding.Name);
        }
    }
}
