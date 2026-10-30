using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Keeps checked source identities across lifting. Representation flow uses the checker's
/// immutable value-binding snapshot, never a second name resolver or the editor index.
/// </summary>
internal sealed partial class RawCallStorageAnalysis
{
    private sealed record Slot(object Declaration, Token Name, Expr? Initializer, Stmt.Parameter? Parameter);
    private sealed record Flow(Token Target, Expr Value, object? Declaration);

    private readonly TypeMap _map;
    private readonly List<Slot> _slots = [];
    private readonly List<Flow> _flows = [];
    private readonly HashSet<BindingSymbol> _rawBindings = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BindingSymbol> _rawCallBindings = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _rawSlots = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _rawCallSlots = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BindingSymbol> _rawNumericBindings = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _rawNumericSlots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, HashSet<object>> _rewrites = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr, HashSet<Expr>> _equivalentExpressions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, List<Slot>> _slotsByToken = new(ReferenceEqualityComparer.Instance);

    internal IReadOnlyList<Stmt> Statements { get; }
    internal IReadOnlyList<IReadOnlyList<Stmt>> Modules { get; }
    internal HashSet<Expr> Returns { get; } = new(ReferenceEqualityComparer.Instance);
    internal HashSet<Stmt.Function> Functions { get; } = new(ReferenceEqualityComparer.Instance);

    private RawCallStorageAnalysis(IEnumerable<Stmt> statements, TypeMap map,
        IEnumerable<IEnumerable<Stmt>>? modules)
    {
        _map = map;
        Statements = statements.ToArray();
        // Module preparation clears and refills the original lists after this point.
        Modules = modules?.Select(module => (IReadOnlyList<Stmt>)module.ToArray()).ToArray()
            ?? [Statements];
        var inventory = new Inventory(this);
        foreach (var statement in Statements) inventory.Visit(statement);
        foreach (var slot in _slots)
        {
            bool raw = slot.Parameter is { } parameter
                ? map.IsUndefinedReachableNumericParam(parameter)
                : map.IsUndefinedReachableNumericLocal(slot.Declaration)
                    || slot.Initializer is { } value && map.IsUndefinedReachableNumericLocal(value);
            if (!raw) continue;
            _rawSlots.Add(slot.Declaration);
            if (map.GetValueBinding(slot.Name) is { } binding) _rawBindings.Add(binding);
        }
        _ = SeedNumericUpdates();
    }

    internal static RawCallStorageAnalysis Capture(IEnumerable<Stmt> statements, TypeMap map,
        IEnumerable<IEnumerable<Stmt>>? modules = null) => new(statements, map, modules);

    internal void Analyze(List<Stmt> rewritten, RuntimeFeatureSet? features) =>
        ForwardedReturnValueAnalyzer.Analyze(rewritten, _map, features, modules: null, this);

    /// <summary>Only the lifter's equivalent with-clones carry expression proof identity.</summary>
    internal void RecordRewrite(object source, object target)
    {
        if (ReferenceEquals(source, target)) return;
        if (!_rewrites.TryGetValue(source, out var targets))
            _rewrites[source] = targets = new(ReferenceEqualityComparer.Instance);
        targets.Add(target);
        if (source is Expr expression && target is Expr rewritten)
        {
            if (!_equivalentExpressions.TryGetValue(expression, out var expressions))
                _equivalentExpressions[expression] = expressions = new(ReferenceEqualityComparer.Instance);
            expressions.Add(rewritten);
        }
    }

    internal IEnumerable<Expr> EquivalentExpressions(Expr source)
    {
        yield return source;
        if (!_equivalentExpressions.TryGetValue(source, out var first)) yield break;
        var seen = new HashSet<Expr>(ReferenceEqualityComparer.Instance) { source };
        var pending = new Stack<Expr>(first);
        while (pending.TryPop(out var expression))
        {
            if (!seen.Add(expression)) continue;
            yield return expression;
            if (_equivalentExpressions.TryGetValue(expression, out var next))
                foreach (var item in next) pending.Push(item);
        }
    }

    internal enum Evidence { Any, Call, Numeric }

    internal bool IsRawBinding(Token token, Evidence evidence) =>
        _map.GetValueBinding(token) is { } binding && (evidence switch
        {
            Evidence.Call => _rawCallBindings,
            Evidence.Numeric => _rawNumericBindings,
            _ => _rawBindings
        }).Contains(binding);

    internal bool Propagate(Func<Expr, Evidence, bool> passesRawValue)
    {
        bool changed = SeedNumericUpdates();
        if (HasNumericProducer) changed |= PropagateCallableValues();
        foreach (var flow in _flows)
            changed |= MarkRawFlow(flow.Target, flow.Declaration,
                passesRawValue(flow.Value, Evidence.Any), passesRawValue(flow.Value, Evidence.Call),
                passesRawValue(flow.Value, Evidence.Numeric));
        return PropagateArguments(passesRawValue) | changed;
    }

    private bool MarkRawFlow(Token name, object? declaration, bool raw, bool callRaw, bool numericRaw)
    {
        bool changed = false;
        if (raw)
        {
            if (declaration is not null) changed |= _rawSlots.Add(declaration);
            if (_map.GetValueBinding(name) is { } binding) changed |= _rawBindings.Add(binding);
        }
        if (callRaw)
        {
            if (declaration is not null) changed |= _rawCallSlots.Add(declaration);
            if (_map.GetValueBinding(name) is { } binding) changed |= _rawCallBindings.Add(binding);
        }
        if (numericRaw)
        {
            if (declaration is not null) changed |= _rawNumericSlots.Add(declaration);
            if (_map.GetValueBinding(name) is { } binding) changed |= _rawNumericBindings.Add(binding);
        }
        return changed;
    }

    internal void Publish(IEnumerable<Expr> rawExpressions, Func<Stmt.Function, bool> functionReturnsRaw,
        List<Stmt> rewritten)
    {
        foreach (var expression in rawExpressions)
            foreach (var node in Descendants(expression))
                if (node is Expr value) _map.MarkUndefinedReachableReturn(value);

        foreach (var slot in _slots)
        {
            bool raw = _rawSlots.Contains(slot.Declaration) || IsRawBinding(slot.Name, Evidence.Any);
            bool callRaw = _rawCallSlots.Contains(slot.Declaration) || IsRawBinding(slot.Name, Evidence.Call);
            if (!raw && !callRaw) continue;
            bool numericRaw = _rawNumericSlots.Contains(slot.Declaration) || IsRawBinding(slot.Name, Evidence.Numeric);
            foreach (var node in Descendants(slot.Declaration)) PublishSlot(node, callRaw, numericRaw);
            if (slot.Initializer is { } initializer)
                foreach (var node in Descendants(initializer))
                    if (node is Expr value) _map.MarkUndefinedReachableNumericLocal(value);
        }

        // Explicit lifted declarations and forwarding arrows are one-to-many owner rewrites.
        // A synthetic call does not gain a native proof, but must preserve a raw source return.
        foreach (var function in Functions)
            if (functionReturnsRaw(function))
                foreach (var node in Descendants(function))
                    if (node is Expr.ArrowFunction { ExpressionBody: { } value })
                        _map.MarkUndefinedReachableReturn(value);

        // Parameter records can be copied separately into a relocated declaration and its
        // forwarder. Their original Name token is authoritative; fresh captures have new tokens.
        var emitted = new EmittedSlots(this);
        foreach (var statement in rewritten) emitted.Visit(statement);
    }

    private void PublishSlot(object node, bool callRaw, bool numericRaw)
    {
        switch (node)
        {
            case Stmt.Parameter parameter:
                _map.MarkUndefinedReachableNumericParam(parameter);
                if (callRaw) _map.MarkRawCallStorage(parameter.Name);
                if (numericRaw) _map.MarkRawNumericStorage(parameter.Name);
                break;
            case Stmt.Var variable:
                _map.MarkUndefinedReachableNumericLocal(variable);
                if (variable.Initializer is { } value) _map.MarkUndefinedReachableNumericLocal(value);
                if (callRaw) _map.MarkRawCallStorage(variable.Name);
                if (numericRaw) _map.MarkRawNumericStorage(variable.Name);
                break;
            case Stmt.Const constant:
                _map.MarkUndefinedReachableNumericLocal(constant);
                _map.MarkUndefinedReachableNumericLocal(constant.Initializer);
                if (callRaw) _map.MarkRawCallStorage(constant.Name);
                if (numericRaw) _map.MarkRawNumericStorage(constant.Name);
                break;
        }
    }

    private IEnumerable<object> Descendants(object source)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(source);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node)) continue;
            yield return node;
            if (_rewrites.TryGetValue(node, out var next))
                foreach (var target in next) pending.Push(target);
        }
    }

    private void AddSlot(object declaration, Token name, Expr? initializer, Stmt.Parameter? parameter = null)
    {
        var slot = new Slot(declaration, name, initializer, parameter);
        _slots.Add(slot);
        if (!_slotsByToken.TryGetValue(name, out var slots)) _slotsByToken[name] = slots = [];
        slots.Add(slot);
        if (initializer is not null) _flows.Add(new(name, initializer, declaration));
    }

    private sealed class EmittedSlots(RawCallStorageAnalysis analysis) : Inventory(analysis, collect: false)
    {
        protected override void Slot(object declaration, Token name, Expr? initializer, Stmt.Parameter? parameter = null)
        {
            if (!Analysis._slotsByToken.TryGetValue(name, out var sources)) return;
            if (!sources.Any(slot => Analysis._rawSlots.Contains(slot.Declaration)) && !Analysis.IsRawBinding(name, Evidence.Any)) return;
            bool callRaw = sources.Any(slot => Analysis._rawCallSlots.Contains(slot.Declaration)) || Analysis.IsRawBinding(name, Evidence.Call);
            bool numericRaw = sources.Any(slot => Analysis._rawNumericSlots.Contains(slot.Declaration)) || Analysis.IsRawBinding(name, Evidence.Numeric);
            Analysis.PublishSlot(declaration, callRaw, numericRaw);
        }
    }

    private class Inventory(RawCallStorageAnalysis analysis, bool collect = true) : AstVisitorBase
    {
        protected RawCallStorageAnalysis Analysis { get; } = analysis;
        private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);
        private bool _emittedSourceBody = true;
        protected override void VisitCall(Expr.Call call)
        {
            if (collect && _emittedSourceBody) Analysis._argumentSites.Add(new(call.Callee, Array.AsReadOnly(call.Arguments.ToArray())));
            base.VisitCall(call);
        }
        protected override void VisitCallPrivate(Expr.CallPrivate call)
        {
            if (collect && _emittedSourceBody) Analysis._argumentSites.Add(new(null, Array.AsReadOnly(call.Arguments.ToArray())));
            base.VisitCallPrivate(call);
        }
        protected override void VisitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral tagged)
        {
            if (collect && _emittedSourceBody) Analysis._argumentSites.Add(new(null, Array.AsReadOnly(tagged.Expressions.ToArray())));
            base.VisitTaggedTemplateLiteral(tagged);
        }
        protected override void VisitPrefixIncrement(Expr.PrefixIncrement update)
        {
            if (collect && _emittedSourceBody)
            {
                Analysis._numericUpdates.Add((update, update.Operand));
                if (update.Operand is Expr.Get or Expr.GetIndex or Expr.GetPrivate or Expr.Super)
                    Analysis._argumentSites.Add(new(null, Array.AsReadOnly<Expr>([update])));
            }
            base.VisitPrefixIncrement(update);
        }
        protected override void VisitPostfixIncrement(Expr.PostfixIncrement update)
        {
            if (collect && _emittedSourceBody)
            {
                Analysis._numericUpdates.Add((update, update.Operand));
                if (update.Operand is Expr.Get or Expr.GetIndex or Expr.GetPrivate or Expr.Super)
                    Analysis._argumentSites.Add(new(null, Array.AsReadOnly<Expr>([update])));
            }
            base.VisitPostfixIncrement(update);
        }
        public override void Visit(Stmt statement) { if (_seen.Add(statement)) base.Visit(statement); }
        public override void Visit(Expr expression)
        {
            if (!_seen.Add(expression)) return;
            Expr? setterValue = expression switch
            {
                Expr.Set assignment => assignment.Value,
                Expr.SetIndex assignment => assignment.Value,
                Expr.SetSuper assignment => assignment.Value,
                Expr.SetPrivate assignment => assignment.Value,
                Expr.LogicalSet assignment => assignment.Value,
                Expr.LogicalSetIndex assignment => assignment.Value,
                Expr.LogicalSetPrivate assignment => assignment.Value,
                _ => null
            };
            if (collect && _emittedSourceBody && setterValue is not null)
                Analysis._argumentSites.Add(new(null, Array.AsReadOnly(new[] { setterValue })));
            base.Visit(expression);
        }
        protected virtual void Slot(object declaration, Token name, Expr? initializer, Stmt.Parameter? parameter = null) =>
            Analysis.AddSlot(declaration, name, initializer, parameter);
        protected override void VisitVar(Stmt.Var statement)
        {
            if (!statement.IsDeclare) Slot(statement, statement.Name, statement.Initializer);
            base.VisitVar(statement);
            if (statement.HoistTypeInferenceInitializer is { } value) Visit(value);
        }
        protected override void VisitConst(Stmt.Const statement) { Slot(statement, statement.Name, statement.Initializer); base.VisitConst(statement); }
        protected override void VisitAssign(Expr.Assign expression)
        {
            if (collect) Analysis._flows.Add(new(expression.Name, expression.Value, null));
            base.VisitAssign(expression);
        }
        protected override void VisitAssertedAssignment(Expr.AssertedAssignment expression)
        {
            Token? target = expression.LoweredAssignment switch
            {
                Expr.Assign assignment => assignment.Name,
                Expr.CompoundAssign assignment => assignment.Name,
                Expr.LogicalAssign assignment => assignment.Name,
                _ => null
            };
            if (collect && target is not null && Analysis._map.GetValueBinding(target) is { } binding)
            {
                Analysis._rawBindings.Add(binding);
                Analysis._map.MarkAssertedAssignmentStorage(binding);
            }
            base.VisitAssertedAssignment(expression);
        }
        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            if (collect) Analysis._flows.Add(new(expression.Name, expression.Value, null));
            base.VisitLogicalAssign(expression);
        }
        protected override void VisitReturn(Stmt.Return statement)
        {
            if (collect && statement.Value is { } value) Analysis.Returns.Add(value);
            base.VisitReturn(statement);
        }
        private void Parameters(IEnumerable<Stmt.Parameter> parameters)
        {
            foreach (var parameter in parameters)
            {
                Slot(parameter, parameter.Name, parameter.DefaultValue, parameter);
                Decorators(parameter.Decorators);
                if (parameter.DefaultValue is { } value) Visit(value);
                foreach (var property in parameter.DestructuredProperties ?? [])
                    if (property.DefaultValue is { } fallback)
                    {
                        if (collect) Analysis._flows.Add(new(property.Binding, fallback, null));
                        Visit(fallback);
                    }
            }
        }
        private void Decorators(IEnumerable<Decorator>? decorators)
        {
            foreach (var decorator in decorators ?? []) Visit(decorator.Expression);
        }
        protected override void VisitFunction(Stmt.Function function)
        {
            if (collect) Analysis.Functions.Add(function);
            if (collect && _emittedSourceBody && function.Body is not null && !function.IsDeclare)
                Analysis.RegisterCallable(function, function.Parameters, function.Name);
            Parameters(function.Parameters); Decorators(function.Decorators);
            if (function.ComputedKey is { } key) Visit(key);
            foreach (var statement in function.Body ?? []) Visit(statement);
        }
        protected override void VisitArrowFunction(Expr.ArrowFunction arrow)
        {
            if (collect && _emittedSourceBody) Analysis.RegisterCallable(arrow, arrow.Parameters, arrow.Name);
            Parameters(arrow.Parameters);
            if (arrow.ExpressionBody is { } body) { if (collect) Analysis.Returns.Add(body); Visit(body); }
            foreach (var statement in arrow.BlockBody ?? []) Visit(statement);
        }
        protected override void VisitNew(Expr.New expression)
        {
            if (collect && _emittedSourceBody) Analysis._argumentSites.Add(new(null, Array.AsReadOnly(expression.Arguments.ToArray())));
            if (expression.Callee is { } callee) Visit(callee);
            base.VisitNew(expression);
        }
        protected override void VisitDestructuringAssign(Expr.DestructuringAssign expression)
        {
            base.VisitDestructuringAssign(expression);
            if (expression.RawTarget is { } target) Visit(target);
            if (expression.RawDefault is { } fallback) Visit(fallback);
        }
        protected override void VisitObjectLiteral(Expr.ObjectLiteral expression)
        {
            foreach (var property in expression.Properties)
                if (property.SetterParam is { } parameter)
                {
                    if (collect && _emittedSourceBody) Analysis._sourceParameters.Add(parameter);
                    Parameters([parameter]);
                }
            base.VisitObjectLiteral(expression);
        }
        protected override void VisitAccessor(Stmt.Accessor accessor)
        {
            if (accessor.SetterParam is { } parameter)
            {
                if (collect && _emittedSourceBody) Analysis._sourceParameters.Add(parameter);
                Parameters([parameter]);
            }
            Decorators(accessor.Decorators);
            base.VisitAccessor(accessor);
        }
        protected override void VisitField(Stmt.Field field) { Decorators(field.Decorators); base.VisitField(field); }
        protected override void VisitAutoAccessor(Stmt.AutoAccessor accessor) { Decorators(accessor.Decorators); base.VisitAutoAccessor(accessor); }
        protected override void VisitClass(Stmt.Class declaration)
        {
            Decorators(declaration.Decorators);
            bool previous = _emittedSourceBody;
            _emittedSourceBody &= ILCompiler.EmitsSourceClassBody(declaration);
            try
            {
                Members(declaration.SuperclassExpr, declaration.Fields, declaration.Methods,
                    declaration.Accessors, declaration.AutoAccessors, declaration.StaticInitializers);
            }
            finally { _emittedSourceBody = previous; }
        }
        protected override void VisitClassExpr(Expr.ClassExpr expression) =>
            Members(expression.SuperclassExpr, expression.Fields, expression.Methods,
                expression.Accessors, expression.AutoAccessors, expression.StaticInitializers);
        private void Members(Expr? parent, IEnumerable<Stmt.Field> fields, IEnumerable<Stmt.Function> methods,
            IEnumerable<Stmt.Accessor>? accessors, IEnumerable<Stmt.AutoAccessor>? autoAccessors, IEnumerable<Stmt>? initializers)
        {
            if (parent is not null) Visit(parent);
            foreach (var field in fields) Visit(field);
            foreach (var method in methods) VisitFunction(method);
            foreach (var accessor in accessors ?? []) Visit(accessor);
            foreach (var accessor in autoAccessors ?? []) Visit(accessor);
            foreach (var initializer in initializers ?? []) Visit(initializer);
        }
    }
}
