using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime;

// Context-owned binding data. Never retain an evaluation environment or host here.
internal sealed class VmGlobalLexicalState
{
    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.Ordinal);
    internal IEnumerable<string> Names => _bindings.Keys;
    internal bool Contains(string name) => _bindings.ContainsKey(name);
    internal bool IsUninitialized(string name) => _bindings.TryGetValue(name, out var cell) && !cell.Initialized;

    internal bool TryRead(string name, out RuntimeValue value)
    {
        if (!_bindings.TryGetValue(name, out var cell)) { value = default; return false; }
        EnsureInitialized(name, cell);
        value = cell.Value;
        return true;
    }

    internal bool TryInitialize(string name, RuntimeValue value)
    {
        if (!_bindings.TryGetValue(name, out var cell)) return false;
        if (cell.Initialized) throw Duplicate(name);
        cell.Value = value;
        cell.Initialized = true;
        return true;
    }

    internal bool TryAssign(string name, RuntimeValue value)
    {
        if (!_bindings.TryGetValue(name, out var cell)) return false;
        EnsureInitialized(name, cell);
        if (cell.Immutable) throw new ThrowException(new SharpTSTypeError("Assignment to constant variable."));
        cell.Value = value;
        return true;
    }

    internal IEnumerable<RuntimeValue> InitializedValues()
    {
        foreach (var cell in _bindings.Values)
            if (cell.Initialized) yield return cell.Value;
    }

    internal void Prepare(IEnumerable<Stmt> statements, Func<string, bool> restricted)
    {
        var declarations = CollectLexicals(statements).ToArray();
        var vars = new VarCollector();
        foreach (var statement in statements) vars.Visit(statement);
        foreach (var name in TopLevelFunctions(statements)) vars.Names.Add(name);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, _) in declarations)
            if (!names.Add(name) || Contains(name) || vars.Names.Contains(name) || restricted(name))
                throw Duplicate(name);
        foreach (var name in vars.Names)
            if (Contains(name)) throw Duplicate(name);
        // Validate the complete declaration list before reserving any cells.
        foreach (var (name, immutable) in declarations)
            _bindings.Add(name, new Binding(immutable));
    }

    private static IEnumerable<(string Name, bool Immutable)> CollectLexicals(IEnumerable<Stmt> statements)
    {
        foreach (var statement in statements)
            switch (statement)
            {
                case Stmt.Var { IsVar: false, IsDeclare: false } variable:
                    if (!variable.Name.IsGeneratedTemporary)
                        yield return (variable.Name.Lexeme, variable.IsSourceConst);
                    break;
                case Stmt.Const constant:
                    yield return (constant.Name.Lexeme, true); break;
                case Stmt.Class { IsDeclare: false } type:
                    yield return (type.Name.Lexeme, false); break;
                case Stmt.Sequence sequence:
                    foreach (var binding in CollectLexicals(sequence.Statements)) yield return binding;
                    break;
            }
    }

    internal static IEnumerable<string> TopLevelFunctions(IEnumerable<Stmt> statements)
    {
        foreach (var statement in statements)
            switch (statement)
            {
                case Stmt.Function { Body: not null } function: yield return function.Name.Lexeme; break;
                case Stmt.Sequence sequence:
                    foreach (var name in TopLevelFunctions(sequence.Statements)) yield return name;
                    break;
            }
    }

    private static void EnsureInitialized(string name, Binding cell)
    {
        if (!cell.Initialized) throw new ThrowException(new SharpTSReferenceError($"Cannot access '{name}' before initialization"));
    }
    private static ThrowException Duplicate(string name) =>
        new(new SharpTSSyntaxError($"Identifier '{name}' has already been declared"));

    private sealed class Binding(bool immutable)
    {
        internal RuntimeValue Value;
        internal bool Initialized;
        internal readonly bool Immutable = immutable;
    }

    private sealed class VarCollector : AstVisitorBase
    {
        internal HashSet<string> Names { get; } = new(StringComparer.Ordinal);
        public override void Visit(Expr expr) { }
        protected override void VisitFunction(Stmt.Function stmt) { }
        protected override void VisitClass(Stmt.Class stmt) { }
        protected override void VisitNamespace(Stmt.Namespace stmt) { }
        protected override void VisitStaticBlock(Stmt.StaticBlock stmt) { }
        protected override void VisitDeclareModule(Stmt.DeclareModule stmt) { }
        protected override void VisitDeclareGlobal(Stmt.DeclareGlobal stmt) { }
        protected override void VisitVar(Stmt.Var stmt)
        {
            if (stmt.IsVar && !stmt.IsDeclare) Names.Add(stmt.Name.Lexeme);
        }
        protected override void VisitForOf(Stmt.ForOf stmt)
        {
            if (stmt.IsDeclaration && stmt.IsVar) Names.Add(stmt.Variable.Lexeme);
            Visit(stmt.Body);
        }
        protected override void VisitForIn(Stmt.ForIn stmt)
        {
            if (stmt.IsDeclaration && stmt.IsVar) Names.Add(stmt.Variable.Lexeme);
            Visit(stmt.Body);
        }
    }
}
