using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Identifies exact direct call nodes and calls through initialized local const aliases. Name counts are
/// deliberately module-wide: any shadow disables the proof instead of guessing its scope.
/// The result is keyed by AST identity, never by an alias spelling in the emitter.
/// </summary>
internal static class StableNumericRestAliasAnalyzer
{
    public static Dictionary<Expr.Call, Stmt.Function> Analyze(
        IReadOnlyList<Stmt> statements, IReadOnlySet<Stmt.Function> stableFunctions)
    {
        var bindings = new BindingCollector();
        foreach (var statement in statements)
            bindings.Visit(statement);
        if (bindings.HasUnresolvedBindingPattern)
            return new(ReferenceEqualityComparer.Instance);
        var targets = stableFunctions
            .Where(f => bindings.Declarations.TryGetValue(f.Name.Lexeme, out var nodes)
                && nodes.Count == 1 && ReferenceEquals(nodes[0], f))
            .ToDictionary(f => f.Name.Lexeme, StringComparer.Ordinal);
        var result = new Dictionary<Expr.Call, Stmt.Function>(ReferenceEqualityComparer.Instance);
        foreach (var function in bindings.Functions)
        {
            if (!stableFunctions.Contains(function) || function.IsAsync || function.IsGenerator || function.Body == null)
                continue;
            var aliases = new Dictionary<string, Stmt.Function>(StringComparer.Ordinal);
            var calls = new CallCollector(aliases, targets, result);
            foreach (var statement in function.Body)
            {
                // Visit first: the initializer cannot read its own uninitialized binding.
                calls.Visit(statement);
                if (statement is Stmt.Const { Initializer: Expr.Variable source } alias
                    && bindings.Declarations[alias.Name.Lexeme].Count == 1
                    && !bindings.Writes.Contains(alias.Name.Lexeme)
                    && (targets.TryGetValue(source.Name.Lexeme, out var target)
                        || aliases.TryGetValue(source.Name.Lexeme, out target)))
                {
                    aliases[alias.Name.Lexeme] = target;
                }
            }
        }
        return result;
    }

    private sealed class CallCollector(
        Dictionary<string, Stmt.Function> aliases,
        Dictionary<string, Stmt.Function> targets,
        Dictionary<Expr.Call, Stmt.Function> result) : AstVisitorBase
    {
        protected override void VisitCall(Expr.Call expression)
        {
            if (!expression.Optional && expression.Callee is Expr.Variable variable
                && (aliases.TryGetValue(variable.Name.Lexeme, out var target)
                    || targets.TryGetValue(variable.Name.Lexeme, out target)))
                result[expression] = target;
            base.VisitCall(expression);
        }

        // A nested callable can execute before the alias initialization. This initial
        // proof covers module-level functions only; do not inherit enclosing facts.
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
    }

    private sealed class BindingCollector : AstVisitorBase
    {
        public Dictionary<string, List<object>> Declarations { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Writes { get; } = new(StringComparer.Ordinal);
        public List<Stmt.Function> Functions { get; } = [];
        public bool HasUnresolvedBindingPattern { get; private set; }
        private void Add(string name, object node)
        {
            if (!Declarations.TryGetValue(name, out var nodes))
                Declarations[name] = nodes = [];
            nodes.Add(node);
        }
        protected override void VisitConst(Stmt.Const s) { Add(s.Name.Lexeme, s); base.VisitConst(s); }
        protected override void VisitVar(Stmt.Var s) { Add(s.Name.Lexeme, s); base.VisitVar(s); }
        protected override void VisitFunction(Stmt.Function s)
        {
            Add(s.Name.Lexeme, s);
            Functions.Add(s);
            foreach (var p in s.Parameters) Add(p.Name.Lexeme, p);
            base.VisitFunction(s);
        }
        protected override void VisitArrowFunction(Expr.ArrowFunction e)
        {
            foreach (var p in e.Parameters) Add(p.Name.Lexeme, p);
            base.VisitArrowFunction(e);
        }
        protected override void VisitForOf(Stmt.ForOf s) { Add(s.Variable.Lexeme, s); base.VisitForOf(s); }
        protected override void VisitForIn(Stmt.ForIn s) { Add(s.Variable.Lexeme, s); base.VisitForIn(s); }
        protected override void VisitTryCatch(Stmt.TryCatch s)
        {
            if (s.CatchParam != null) Add(s.CatchParam.Lexeme, s);
            base.VisitTryCatch(s);
        }
        protected override void VisitClass(Stmt.Class s) { Add(s.Name.Lexeme, s); base.VisitClass(s); }
        protected override void VisitEnum(Stmt.Enum s) { Add(s.Name.Lexeme, s); base.VisitEnum(s); }
        protected override void VisitNamespace(Stmt.Namespace s) { Add(s.Name.Lexeme, s); base.VisitNamespace(s); }
        protected override void VisitImportAlias(Stmt.ImportAlias s) { Add(s.AliasName.Lexeme, s); base.VisitImportAlias(s); }
        protected override void VisitImportRequire(Stmt.ImportRequire s) { Add(s.AliasName.Lexeme, s); base.VisitImportRequire(s); }
        protected override void VisitImport(Stmt.Import s)
        {
            if (s.DefaultImport != null) Add(s.DefaultImport.Lexeme, s);
            if (s.NamespaceImport != null) Add(s.NamespaceImport.Lexeme, s);
            foreach (var specifier in s.NamedImports ?? [])
                Add((specifier.LocalName ?? specifier.Imported).Lexeme, specifier);
            base.VisitImport(s);
        }
        protected override void VisitUsing(Stmt.Using s)
        {
            foreach (var binding in s.Bindings)
            {
                if (binding.Name != null) Add(binding.Name.Lexeme, binding);
                else HasUnresolvedBindingPattern = true;
            }
            base.VisitUsing(s);
        }
        protected override void VisitAssign(Expr.Assign e) { Writes.Add(e.Name.Lexeme); base.VisitAssign(e); }
        protected override void VisitCompoundAssign(Expr.CompoundAssign e) { Writes.Add(e.Name.Lexeme); base.VisitCompoundAssign(e); }
        protected override void VisitLogicalAssign(Expr.LogicalAssign e) { Writes.Add(e.Name.Lexeme); base.VisitLogicalAssign(e); }
    }
}
