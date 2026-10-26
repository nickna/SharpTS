using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed class UnprovedLoopBodyFlow : Exception { }
    private enum LoopBodyNodeKind { Pass, Expression, Condition, Declaration, Scope, Subject, Each, Case, Return, End }
    private sealed class LoopBodyScope(LoopBodyScope? parent, TypeEnvironment environment)
    {
        internal LoopBodyScope? Parent { get; } = parent;
        internal TypeEnvironment Environment { get; } = environment;
        internal Dictionary<string, object> Locals { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> TypeNames { get; } = new(StringComparer.Ordinal);
        internal object? Resolve(string name) => Locals.GetValueOrDefault(name) ?? Parent?.Resolve(name) ?? Environment.GetValueBinding(name);
    }
    private sealed class LoopBodyNode(LoopBodyNodeKind kind, LoopBodyScope scope, Expr? expression = null)
    {
        internal LoopBodyNodeKind Kind { get; } = kind;
        internal LoopBodyScope Scope { get; } = scope;
        internal Expr? Expression { get; } = expression;
        internal Expr? Subject { get; init; }
        internal object? Slot { get; init; }
        internal object? AssignedSlot { get; init; }
        internal TypeInfo? AssignedType { get; init; }
        internal bool IsConst { get; init; }
        internal bool LoopTest { get; init; }
        internal int Next { get; set; } = -1;
        internal int Otherwise { get; set; } = -1;
        internal int Exception { get; init; } = -1;
    }
    private sealed record LoopBodyTargets(int Break, int Continue, int Return, int Throw,
        IReadOnlyDictionary<string, (int Break, int Continue)> Labels);
    private sealed class LoopBodyGraph(TypeEnvironment environment, LoopBodyGraph? parent = null)
    {
        internal TypeEnvironment Environment { get; } = environment;
        internal List<LoopBodyNode> Nodes { get; } = [];
        internal Dictionary<object, TypeInfo> Initial { get; } = parent?.Initial ?? new(ReferenceEqualityComparer.Instance);
        internal Dictionary<object, TypeInfo> Contracts { get; } = parent?.Contracts ?? new(ReferenceEqualityComparer.Instance);
        internal Dictionary<object, Dictionary<string, object>> MemberSlots { get; } = parent?.MemberSlots ?? new(ReferenceEqualityComparer.Instance);
        internal Dictionary<object, (object Root, string[] Path)> MemberPaths { get; } = parent?.MemberPaths ?? new(ReferenceEqualityComparer.Instance);
        internal int Entry { get; set; }
        internal int Exit { get; set; }
        internal int ThrowExit { get; set; }
        internal object? ReturnSlot { get; set; }
        internal TypeInfo? ThisType { get; set; }
    }

    // Build type-transfer edges before checking the source body. These nodes never enter
    // checker scopes, bind source declarations or publish expression/member/call facts.
    private LoopBodyGraph BuildLoopBodyGraph(Stmt.DoWhile? loop, Expr.ArrowFunction? callable = null,
        LoopBodyScope? callScope = null, LoopBodyGraph? parent = null)
    {
        var graph = new LoopBodyGraph(callScope?.Environment ?? _environment, parent);
        graph.ThisType = parent?.ThisType ?? _currentFunctionThisType;
        var root = callScope ?? new LoopBodyScope(null, _environment);
        int Add(LoopBodyNode node)
        {
            if (graph.Nodes.Count >= 2048) throw new UnprovedLoopBodyFlow();
            graph.Nodes.Add(node);
            return graph.Nodes.Count - 1;
        }
        int end = Add(new(LoopBodyNodeKind.End, root));
        int entry = Add(new(LoopBodyNodeKind.Pass, root));
        graph.Entry = entry;
        graph.Exit = end;
        graph.ThrowExit = end;
        if (loop is not null)
        {
            int condition = Add(new(LoopBodyNodeKind.Condition, root, loop.Condition) { Next = entry, Otherwise = end, LoopTest = true });
            var labels = _valueFlowTargets.Where(target => target.Label is not null).ToDictionary(target => target.Label!,
                target => (end, ReferenceEquals(target.LoopBody, loop.Body) ? condition : end), StringComparer.Ordinal);
            var targets = new LoopBodyTargets(end, condition, end, end, labels);
            graph.Nodes[entry].Next = Build(loop.Body, condition, root, targets);
        }
        else
        {
            graph.ThrowExit = Add(new(LoopBodyNodeKind.End, root));
            graph.ReturnSlot = new();
            graph.Initial[graph.ReturnSlot] = TypeInfo.Undefined.Shared;
            var targets = new LoopBodyTargets(-1, -1, end, graph.ThrowExit, new Dictionary<string, (int, int)>(StringComparer.Ordinal));
            if (callable?.ExpressionBody is { } expression)
                graph.Nodes[entry].Next = Add(new(LoopBodyNodeKind.Return, root, expression) { Slot = graph.ReturnSlot, Next = end, Exception = graph.ThrowExit });
            else
            {
                var body = callable?.BlockBody ?? throw new UnprovedLoopBodyFlow();
                var local = BlockScope(body, root);
                graph.Nodes[entry].Next = Add(new(LoopBodyNodeKind.Scope, local) { Next = Statements(body, end, local, targets) });
            }
        }
        return graph;

        LoopBodyScope BlockScope(IEnumerable<Stmt> statements, LoopBodyScope parent)
        {
            var scope = new LoopBodyScope(parent, parent.Environment);
            foreach (var statement in statements)
            {
                if (statement is Stmt.TypeAlias alias) scope.TypeNames.Add(alias.Name.Lexeme);
                else if (statement is Stmt.Interface iface) scope.TypeNames.Add(iface.Name.Lexeme);
                else if (statement is Stmt.Class cls) scope.TypeNames.Add(cls.Name.Lexeme);
                Token? name = statement switch
                {
                    Stmt.Var { IsVar: false } variable => variable.Name,
                    Stmt.Const constant => constant.Name,
                    Stmt.Function function => function.Name,
                    Stmt.Class declaration => declaration.Name,
                    _ => null
                };
                if (name is not null) scope.Locals[name.Lexeme] = name;
            }
            return scope;
        }
        int Statements(IEnumerable<Stmt> statements, int next, LoopBodyScope scope, LoopBodyTargets exits)
        {
            foreach (var statement in statements.Reverse()) next = Build(statement, next, scope, exits);
            return next;
        }
        int Expression(Expr? expression, int next, LoopBodyScope scope, LoopBodyTargets exits) => expression is null ? next :
            Add(new(LoopBodyNodeKind.Expression, scope, expression) { Next = next, Exception = exits.Throw });
        int Build(Stmt statement, int next, LoopBodyScope scope, LoopBodyTargets exits, string? label = null)
        {
            switch (statement)
            {
                case Stmt.Block block:
                {
                    var local = BlockScope(block.Statements, scope);
                    return Add(new(LoopBodyNodeKind.Scope, local)
                    { Next = Statements(block.Statements, next, local, exits) });
                }
                case Stmt.Sequence sequence: return Statements(sequence.Statements, next, scope, exits);
                case Stmt.Expression expression: return Expression(expression.Expr, next, scope, exits);
                case Stmt.Var variable:
                    return Add(new(LoopBodyNodeKind.Declaration, scope, variable.Initializer)
                    { Slot = scope.Resolve(variable.Name.Lexeme) ?? variable.Name, Next = next, Exception = exits.Throw });
                case Stmt.Const constant:
                    return Add(new(LoopBodyNodeKind.Declaration, scope, constant.Initializer)
                    { Slot = scope.Resolve(constant.Name.Lexeme) ?? constant.Name, IsConst = true, Next = next, Exception = exits.Throw });
                case Stmt.If conditional:
                    return Add(new(LoopBodyNodeKind.Condition, scope, conditional.Condition)
                    {
                        Next = Build(conditional.ThenBranch, next, scope, exits),
                        Otherwise = conditional.ElseBranch is null ? next : Build(conditional.ElseBranch, next, scope, exits),
                        Exception = exits.Throw
                    });
                case Stmt.Break jump:
                    return jump.Label is { } breakLabel ? exits.Labels.TryGetValue(breakLabel.Lexeme, out var breakTarget)
                        ? breakTarget.Break : throw new UnprovedLoopBodyFlow() : exits.Break;
                case Stmt.Continue jump:
                    return jump.Label is { } continueLabel ? exits.Labels.TryGetValue(continueLabel.Lexeme, out var continueTarget) && continueTarget.Continue >= 0
                        ? continueTarget.Continue : throw new UnprovedLoopBodyFlow() : exits.Continue;
                case Stmt.Return returned:
                    return graph.ReturnSlot is null ? Expression(returned.Value, exits.Return, scope, exits)
                        : Add(new(LoopBodyNodeKind.Return, scope, returned.Value) { Slot = graph.ReturnSlot, Next = exits.Return, Exception = exits.Throw });
                case Stmt.Throw thrown: return Expression(thrown.Value, exits.Throw, scope, exits);
                case Stmt.LabeledStatement labeled:
                {
                    var labels = exits.Labels.ToDictionary(StringComparer.Ordinal);
                    labels[labeled.Label.Lexeme] = (next, -1);
                    return Build(labeled.Statement, next, scope, exits with { Labels = labels }, labeled.Label.Lexeme);
                }
                case Stmt.While nested:
                {
                    int test = Add(new(LoopBodyNodeKind.Condition, scope, nested.Condition) { Otherwise = next, LoopTest = true, Exception = exits.Throw });
                    var nestedExits = LoopTargets(exits, next, test, label);
                    graph.Nodes[test].Next = Build(nested.Body, test, scope, nestedExits);
                    return test;
                }
                case Stmt.DoWhile nested:
                {
                    int test = Add(new(LoopBodyNodeKind.Condition, scope, nested.Condition) { Otherwise = next, LoopTest = true, Exception = exits.Throw });
                    int body = Build(nested.Body, test, scope, LoopTargets(exits, next, test, label));
                    graph.Nodes[test].Next = body;
                    return body;
                }
                case Stmt.For nested:
                {
                    var local = nested.Initializer is null ? scope : BlockScope([nested.Initializer], scope);
                    int test = Add(new(LoopBodyNodeKind.Condition, local, nested.Condition) { Otherwise = next, LoopTest = true, Exception = exits.Throw });
                    int increment = Expression(nested.Increment, test, local, exits);
                    graph.Nodes[test].Next = Build(nested.Body, increment, local, LoopTargets(exits, next, increment, label));
                    return nested.Initializer is null ? test : Build(nested.Initializer, test, local, exits);
                }
                case Stmt.ForOf nested:
                    return Each(nested.Variable, nested.IsDeclaration, nested.IsVar, nested.Iterable, nested.Body, false);
                case Stmt.ForIn nested:
                    return Each(nested.Variable, nested.IsDeclaration, nested.IsVar, nested.Object, nested.Body, true);
                case Stmt.Switch selection:
                {
                    object subject = new();
                    var switchExits = exits with { Break = next };
                    var local = BlockScope(selection.Clauses.SelectMany(clause => clause.Body), scope);
                    var bodies = new int[selection.Clauses.Count];
                    int fallthrough = next;
                    for (int index = selection.Clauses.Count - 1; index >= 0; index--)
                    {
                        bodies[index] = Statements(selection.Clauses[index].Body, fallthrough, local, switchExits);
                        fallthrough = bodies[index];
                    }
                    int defaultIndex = selection.Clauses.FindIndex(clause => clause.IsDefault);
                    int test = defaultIndex < 0 ? next : bodies[defaultIndex];
                    for (int index = selection.Clauses.Count - 1; index >= 0; index--)
                        if (selection.Clauses[index].Value is { } value)
                            test = Add(new(LoopBodyNodeKind.Case, local, value)
                            { Slot = subject, Subject = selection.Subject, Next = bodies[index], Otherwise = test, Exception = exits.Throw });
                    int enterScope = Add(new(LoopBodyNodeKind.Scope, local) { Next = test });
                    return Add(new(LoopBodyNodeKind.Subject, scope, selection.Subject)
                    { Slot = subject, Next = enterScope, Exception = exits.Throw });
                }
                case Stmt.TryCatch guarded:
                {
                    Dictionary<int, int> finalizers = [];
                    int Finally(int target)
                    {
                        if (guarded.FinallyBlock is not { Count: > 0 } body) return target;
                        if (finalizers.TryGetValue(target, out int result)) return result;
                        var finalTargets = exits;
                        if (graph.ReturnSlot is { } returnSlot)
                        {
                            int Cancel(int destination) => destination < 0 ? destination :
                                Add(new(LoopBodyNodeKind.Declaration, scope)
                                { Slot = returnSlot, AssignedType = TypeInfo.Undefined.Shared, IsConst = true, Next = destination });
                            var escapingLabels = exits.Labels.ToDictionary(pair => pair.Key,
                                pair => (Cancel(pair.Value.Break), Cancel(pair.Value.Continue)), StringComparer.Ordinal);
                            // An escaping abrupt finally completion cancels a pending return.
                            // Nested loop targets override these wrappers; a new return writes
                            // its own value and follows the original return target.
                            finalTargets = exits with
                            {
                                Break = Cancel(exits.Break), Continue = Cancel(exits.Continue),
                                Throw = Cancel(exits.Throw), Labels = escapingLabels
                            };
                        }
                        return finalizers[target] = Statements(body, target, BlockScope(body, scope), finalTargets);
                    }
                    var wrappedLabels = exits.Labels.ToDictionary(pair => pair.Key,
                        pair => (Finally(pair.Value.Break), pair.Value.Continue < 0 ? -1 : Finally(pair.Value.Continue)), StringComparer.Ordinal);
                    var wrapped = new LoopBodyTargets(Finally(exits.Break), Finally(exits.Continue), Finally(exits.Return), Finally(exits.Throw), wrappedLabels);
                    int after = Finally(next);
                    int catcher = wrapped.Throw;
                    if (guarded.CatchBlock is { } catchBody)
                    {
                        var local = BlockScope(catchBody, scope);
                        if (guarded.CatchParam is { } parameter) local.Locals[parameter.Lexeme] = parameter;
                        catcher = Statements(catchBody, after, local, wrapped);
                        if (guarded.CatchParam is { } catchParameter)
                            catcher = Add(new(LoopBodyNodeKind.Declaration, local)
                            { Slot = catchParameter, Next = catcher, IsConst = true, AssignedType = TypeInfo.Unknown.Shared });
                    }
                    return Statements(guarded.TryBlock, after, BlockScope(guarded.TryBlock, scope), wrapped with { Throw = catcher });
                }
                case Stmt.Class declaration:
                {
                    if (declaration.Decorators is { Count: > 0 } || declaration.Fields.Any(field => field.IsStatic && field.Initializer is not null))
                        throw new UnprovedLoopBodyFlow();
                    foreach (var initializer in (declaration.StaticInitializers ?? []).AsEnumerable().Reverse())
                        if (initializer is Stmt.StaticBlock block) next = Statements(block.Body, next, BlockScope(block.Body, scope), exits);
                    return Expression(declaration.SuperclassExpr, next, scope, exits);
                }
                case Stmt.StaticBlock block: return Statements(block.Body, next, BlockScope(block.Body, scope), exits);
                case Stmt.Export { Declaration: { } declaration }: return Build(declaration, next, scope, exits);
                case Stmt.Function or Stmt.Interface or Stmt.TypeAlias or Stmt.Directive or Stmt.FileDirective:
                    return next;
                default: throw new UnprovedLoopBodyFlow();
            }

            int Each(Token variable, bool declaration, bool isVar, Expr iterable, Stmt body, bool keys)
            {
                var local = new LoopBodyScope(scope, scope.Environment);
                if (declaration && !isVar) local.Locals[variable.Lexeme] = variable;
                object source = new();
                var binding = local.Resolve(variable.Lexeme) ?? throw new UnprovedLoopBodyFlow();
                int test = Add(new(LoopBodyNodeKind.Each, local)
                { Slot = source, AssignedSlot = binding, AssignedType = keys ? TypeInfo.String.Shared : null, Otherwise = next });
                graph.Nodes[test].Next = Build(body, test, local, LoopTargets(exits, next, test, label));
                return Add(new(LoopBodyNodeKind.Subject, scope, iterable) { Slot = source, Next = test, Exception = exits.Throw });
            }
        }
        static LoopBodyTargets LoopTargets(LoopBodyTargets current, int @break, int @continue, string? label)
        {
            if (label is null) return current with { Break = @break, Continue = @continue };
            var labels = current.Labels.ToDictionary(StringComparer.Ordinal);
            labels[label] = (@break, @continue);
            return current with { Break = @break, Continue = @continue, Labels = labels };
        }
    }
}
