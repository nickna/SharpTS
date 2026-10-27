using System.Reflection;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;
using TypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Native-only, compilation-owned proof for a deliberately small typed CLR domain.
/// Records source identities before lowering; selected emission sites add exact edges.
/// This does not remove or replace any runtime reflection fallback.
/// </summary>
internal sealed partial class NativeClrAdmission
{
    internal enum Marshalling { MethodResult, BoxedSlot, RawBox, CallbackParameter, Literal, Constructed, TypeValue }
    internal sealed record Selection(Expr Source, MemberInfo Member, Type ResultType, Marshalling Conversion);
    private readonly List<Selection> _selections = [];
    internal IReadOnlyList<Selection> Selections => _selections.AsReadOnly();
    private readonly HashSet<BindingSymbol> _exports = new(ReferenceEqualityComparer.Instance);
    private readonly TypeMap _types;
    private readonly TypeProvider _framework;
    private readonly Dictionary<object, object> _source = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, object?> _parents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, object?> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingSymbol, Stmt.Const> _constants = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingSymbol, List<Expr.Variable>> _reads = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr, HashSet<Expr>> _typedEdges = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr> _opaque = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr> _effects = new(ReferenceEqualityComparer.Instance);
    private readonly List<(string Reason, SourceLocation? Location)> _failures = [];
    private readonly HashSet<object> _sharedSourceUses = new(ReferenceEqualityComparer.Instance);
    private bool _complete;
    private readonly Dictionary<BindingSymbol, object> _functions = new(ReferenceEqualityComparer.Instance);
    private readonly List<Stmt.TryCatch> _tries = [];
    private readonly List<(Expr? Source, string Reason)> _sourceProofBoundaries = [];

    private NativeClrAdmission(IEnumerable<Stmt> statements, TypeMap types, TypeProvider framework)
    {
        _types = types;
        _framework = framework;
        var inventory = new Inventory(this);
        foreach (var statement in statements) inventory.Visit(statement);
    }

    internal static NativeClrAdmission Capture(IEnumerable<Stmt> statements, TypeMap types, TypeProvider framework) => new(statements, types, framework);

    internal void RecordRewrite(object source, object target)
    {
        EnsureOpen();
        if (!ReferenceEquals(source, target)) _source[target] = Source(source);
    }

    private object Source(object node)
    {
        while (_source.TryGetValue(node, out var source)) node = source;
        return node;
    }

    internal void Record(Expr? expression, MemberInfo member, Type resultType, Marshalling marshalling,
        Expr? receiver = null, IReadOnlyList<Expr>? arguments = null)
    {
        EnsureOpen();
        if (expression is null || Source(expression) is not Expr source || !_parents.ContainsKey(source))
        {
            Fail($"selected CLR member {member.DeclaringType?.FullName}.{member.Name} has no original source expression");
            return;
        }
        if (_sharedSourceUses.Contains(source))
        {
            Fail("selected CLR operation has ambiguous original parent or execution owner", source);
            return;
        }
        _selections.Add(new(source, member, resultType, marshalling));
        _effects.Add(source);
        if (receiver is not null) AllowReceiver(source, receiver);
        else if (source is Expr.Call { Callee: Expr.Get get }) AllowReceiver(source, get.Object);
        else if (source is Expr.Get getValue) AllowEdge(source, getValue.Object);
        else if (source is Expr.New construction) AllowEdge(source, construction.Callee);
        foreach (var argument in arguments ?? [])
            if (source is not Expr.Set && source is not Expr.SetIndex) AllowEdge(source, argument);

        if (member is MethodBase method)
        {
            var parameters = method.GetParameters();
            if (parameters.Any(parameter => parameter.ParameterType.IsByRef && !parameter.IsIn) && !IsSafe(resultType, marshalling))
                Fail($"CLR ref/out tuple {member.Name} contains an unsupported CLR result");
            foreach (var parameter in parameters)
            {
                Type slot = parameter.ParameterType;
                if (slot.IsByRef && !parameter.IsIn && !IsSafe(slot.GetElementType()!, Marshalling.MethodResult))
                    Fail($"CLR ref/out result {member.Name}.{parameter.Name} requires unsupported container provenance");
                if (typeof(Delegate).IsAssignableFrom(slot)) RecordCallback(slot);
            }
        }
        if (marshalling == Marshalling.Constructed && resultType.IsValueType)
            Fail($"CLR value-type constructor {member.Name} requires unimplemented guest boxing", source);
        if (_framework.IsObject(resultType))
            Fail($"CLR member {member.Name} declares System.Object without scalar result proof");
        else if (resultType.IsArray && !IsSafe(resultType, marshalling))
            Fail($"CLR member {member.Name} returns an array with unsupported CLR element/storage provenance");
        else if (!IsSafe(resultType, marshalling)) _opaque.Add(source);
    }

    internal void RecordUnclassifiedSourceBoundary(Expr? expression, string reason)
    {
        EnsureOpen();
        _sourceProofBoundaries.Add((expression, reason));
    }

    internal void RecordTypeValue(Expr expression, Type type) =>
        Record(expression, type, typeof(Type), Marshalling.TypeValue);

    internal void RecordCallback(Type delegateType)
    {
        EnsureOpen();
        var invoke = _framework.TryGetMethod(delegateType, "Invoke");
        if (invoke is null || invoke.GetParameters().Any(parameter =>
                parameter.ParameterType.IsByRef || !IsSafe(parameter.ParameterType, Marshalling.CallbackParameter)))
            Fail($"CLR callback {delegateType.FullName} has unsupported reference, boxed-struct or by-ref ingress");
    }

    private bool IsSafe(Type type, Marshalling mode)
    {
        if (mode is Marshalling.Constructed or Marshalling.TypeValue) return false;
        if (_framework.IsVoid(type) || _framework.IsString(type) || _framework.IsBoolean(type) || _framework.IsDouble(type)) return true;
        if (mode == Marshalling.Literal) return !type.IsEnum;
        if (mode == Marshalling.RawBox) return false;
        if (type.IsArray)
        {
            if (mode is not (Marshalling.MethodResult or Marshalling.CallbackParameter) || type.GetArrayRank() != 1) return false;
            var element = type.GetElementType()!;
            return IsSafe(element, element.IsArray ? mode : mode == Marshalling.CallbackParameter ? mode : Marshalling.BoxedSlot);
        }
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return mode == Marshalling.CallbackParameter
                ? _framework.IsBoolean(underlying) || _framework.IsDouble(underlying)
                : IsSafe(underlying, mode);
        if (type == typeof(decimal)) return mode != Marshalling.CallbackParameter;
        return type == typeof(float) || type == typeof(byte) || type == typeof(sbyte) ||
            type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong);
    }

    private void AllowReceiver(Expr operation, Expr receiver)
    {
        if (operation is Expr.Call { Callee: Expr.Get get }) AllowEdge(get, receiver);
        else AllowEdge(operation, receiver);
    }

    private void AllowEdge(Expr parent, Expr child)
    {
        if (Source(parent) is not Expr owner || Source(child) is not Expr value) return;
        if (!_typedEdges.TryGetValue(owner, out var edges)) _typedEdges[owner] = edges = new(ReferenceEqualityComparer.Instance);
        edges.Add(value);
    }

    internal void Complete()
    {
        EnsureOpen();
        foreach (var expression in _opaque) CheckUse(expression, new(ReferenceEqualityComparer.Instance));
        CheckExceptionalCallableIngress();
        CheckExceptionIngress();
        // Late selections must participate, even when the static eval appeared first.
        if (_opaque.Count > 0 || _effects.Count > 0)
            foreach (var boundary in _sourceProofBoundaries)
                Fail(boundary.Reason, boundary.Source is null ? null : Source(boundary.Source));
        _complete = true;
        ThrowIfIncompatible();
    }

    private void CheckUse(Expr expression, HashSet<object> seen)
    {
        if (!seen.Add(expression)) return;
        if (_sharedSourceUses.Contains(expression))
        {
            Fail("CLR value has multiple original source uses requiring unsupported flow proof", expression);
            return;
        }
        if (!_parents.TryGetValue(expression, out var parent) || parent is null)
        {
            Fail("CLR value has no classified source use");
            return;
        }
        if (parent is Expr owner && _typedEdges.TryGetValue(owner, out var edges) && edges.Contains(expression)) return;
        if (parent is Stmt.Expression) return; // Explicit discard does not escape.
        if (parent is Expr.Grouping grouping) { CheckUse(grouping, seen); return; }
        if (parent is Stmt.Const declaration && ReferenceEquals(declaration.Initializer, expression) &&
            _types.GetValueBinding(declaration.Name) is { } binding && _constants.ContainsKey(binding) && !_exports.Contains(binding) &&
            _types.GetDeclaredValueType(declaration.Name) is { } declared && declared is not TypeInfo.Any)
        {
            foreach (var read in _reads.GetValueOrDefault(binding) ?? [])
            {
                if (!ReferenceEquals(_owners.GetValueOrDefault(read), _owners.GetValueOrDefault(declaration)))
                    Fail($"CLR local {declaration.Name.Lexeme} escapes through a captured binding");
                else CheckUse(read, seen);
            }
            return;
        }
        if (parent is Expr.Set or Expr.SetIndex)
            Fail("unimplemented CLR setter/indexer reference-store flow", parent);
        else Fail($"CLR value crosses an unsupported {parent.GetType().Name} use (dynamic/any, source return/throw, alias, heap/container or implicit operation)", parent);
    }

    internal void ThrowIfIncompatible()
    {
        if (!_complete) throw new InvalidOperationException("Native CLR admission has not completed source emission.");
        if (_failures.Count > 0)
        {
            var failure = _failures[0];
            throw new CompileException(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.NativeGuestClrEscape,
                "Experimental native guest CLR admission refused: " + failure.Reason + ". Use managed output for this CLR flow.", failure.Location));
        }
    }

    private void Fail(string reason, object? source = null)
    {
        Token? token = source switch
        {
            Token value => value,
            Expr.Variable value => value.Name,
            Expr.Get value => value.Name,
            Expr.Set value => value.Name,
            Expr.Call value => value.Paren,
            Expr.New { Callee: Expr.Variable value } => value.Name,
            Stmt.Const value => value.Name,
            Stmt.Return value => value.Keyword,
            Stmt.Throw value => value.Keyword,
            _ => null
        };
        _failures.Add((reason, token is null ? null : SourceLocation.FromLine(token.Line)));
    }

    private void EnsureOpen()
    {
        if (_complete) throw new InvalidOperationException("Native CLR admission is already complete.");
    }

    private sealed class Inventory(NativeClrAdmission analysis) : AstVisitorBase
    {
        private object? _parent;
        private object? _owner;
        public override void Visit(Expr expression)
        {
            if (!analysis._parents.TryAdd(expression, _parent) &&
                !ReferenceEquals(analysis._parents[expression], _parent)) analysis._sharedSourceUses.Add(expression);
            if (!analysis._owners.TryAdd(expression, _owner) &&
                !ReferenceEquals(analysis._owners[expression], _owner)) analysis._sharedSourceUses.Add(expression);
            var previous = _parent; _parent = expression;
            try { base.Visit(expression); } finally { _parent = previous; }
        }
        public override void Visit(Stmt statement)
        {
            analysis._parents.TryAdd(statement, _parent);
            analysis._owners.TryAdd(statement, _owner);
            var previous = _parent; _parent = statement;
            try { base.Visit(statement); } finally { _parent = previous; }
        }
        protected override void VisitConst(Stmt.Const statement)
        {
            if (analysis._types.GetValueBinding(statement.Name) is { } binding &&
                analysis._parents.GetValueOrDefault(statement) is not Stmt.Export)
                analysis._constants[binding] = statement;
            base.VisitConst(statement);
        }
        protected override void VisitExport(Stmt.Export statement)
        {
            foreach (var item in statement.NamedExports ?? [])
                if (!item.IsTypeOnly && analysis._types.GetValueBinding(item.LocalName) is { } binding)
                    analysis._exports.Add(binding);
            base.VisitExport(statement);
            // CommonJS export assignment is a runtime value, unlike named export tokens.
            // The shared visitor does not currently traverse this slot.
            if (statement.ExportAssignment is { } value) Visit(value);
        }
        protected override void VisitVariable(Expr.Variable expression)
        {
            if (analysis._types.GetValueBinding(expression.Name) is { } binding)
            {
                if (!analysis._reads.TryGetValue(binding, out var reads)) analysis._reads[binding] = reads = [];
                reads.Add(expression);
            }
            if (analysis._types.Get(expression) is TypeInfo.Class type && DotNetTypeSynthesizer.TryGetClrClassType(type, out _))
                analysis._opaque.Add(expression);
        }
        protected override void VisitNew(Expr.New expression) { Visit(expression.Callee); base.VisitNew(expression); }
        protected override void VisitVar(Stmt.Var statement)
        {
            base.VisitVar(statement);
            if (statement.HoistTypeInferenceInitializer is { } value) Visit(value);
        }
        private void Decorators(IEnumerable<Decorator>? decorators)
        {
            foreach (var decorator in decorators ?? []) Visit(decorator.Expression);
        }
        private void Parameters(IEnumerable<Stmt.Parameter> parameters)
        {
            foreach (var parameter in parameters)
            {
                Decorators(parameter.Decorators);
                if (parameter.DefaultValue is { } value) Visit(value);
                foreach (var property in parameter.DestructuredProperties ?? [])
                    if (property.DefaultValue is { } fallback) Visit(fallback);
            }
        }
        protected override void VisitFunction(Stmt.Function statement)
        {
            Decorators(statement.Decorators);
            if (statement.ComputedKey is { } key) Visit(key);
            if (statement.Body is not null && analysis._types.GetValueBinding(statement.Name) is { } binding)
                analysis._functions[binding] = statement;
            var previous = _owner; _owner = statement;
            try
            {
                Parameters(statement.Parameters);
                foreach (var child in statement.Body ?? []) Visit(child);
            }
            finally { _owner = previous; }
        }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            if (expression.Name is { } name && analysis._types.GetValueBinding(name) is { } binding)
                analysis._functions[binding] = expression;
            var previous = _owner; _owner = expression;
            try
            {
                Parameters(expression.Parameters);
                if (expression.ExpressionBody is { } value) Visit(value);
                foreach (var child in expression.BlockBody ?? []) Visit(child);
            }
            finally { _owner = previous; }
        }
        protected override void VisitAccessor(Stmt.Accessor statement)
        {
            Decorators(statement.Decorators);
            if (statement.ComputedKey is { } key) Visit(key);
            var previous = _owner; _owner = statement;
            try
            {
                if (statement.SetterParam is { } parameter) Parameters([parameter]);
                foreach (var child in statement.Body) Visit(child);
            }
            finally { _owner = previous; }
        }
        protected override void VisitField(Stmt.Field statement)
        {
            Decorators(statement.Decorators);
            if (statement.ComputedKey is { } key) Visit(key);
            var previous = _owner; _owner = statement;
            try { if (statement.Initializer is { } value) Visit(value); }
            finally { _owner = previous; }
        }
        protected override void VisitAutoAccessor(Stmt.AutoAccessor statement)
        {
            Decorators(statement.Decorators);
            var previous = _owner; _owner = statement;
            try { base.VisitAutoAccessor(statement); }
            finally { _owner = previous; }
        }
        protected override void VisitStaticBlock(Stmt.StaticBlock statement)
        {
            var previous = _owner; _owner = statement;
            try { base.VisitStaticBlock(statement); }
            finally { _owner = previous; }
        }
        protected override void VisitClass(Stmt.Class statement)
        {
            Decorators(statement.Decorators);
            Members(statement.SuperclassExpr, statement.Fields, statement.Methods, statement.Accessors,
                statement.AutoAccessors, statement.StaticInitializers);
        }
        protected override void VisitClassExpr(Expr.ClassExpr expression) =>
            Members(expression.SuperclassExpr, expression.Fields, expression.Methods, expression.Accessors,
                expression.AutoAccessors, expression.StaticInitializers);
        private void Members(Expr? parent, IEnumerable<Stmt.Field> fields, IEnumerable<Stmt.Function> methods,
            IEnumerable<Stmt.Accessor>? accessors, IEnumerable<Stmt.AutoAccessor>? autoAccessors, IEnumerable<Stmt>? initializers)
        {
            if (parent is not null) Visit(parent);
            foreach (var field in fields) Visit(field);
            foreach (var method in methods) Visit(method);
            foreach (var accessor in accessors ?? []) Visit(accessor);
            foreach (var accessor in autoAccessors ?? []) Visit(accessor);
            foreach (var initializer in initializers ?? []) Visit(initializer);
        }
        protected override void VisitObjectLiteral(Expr.ObjectLiteral expression)
        {
            foreach (var property in expression.Properties)
            {
                if (property.Key is Expr.ComputedKey key) Visit(key.Expression);
                var previous = _owner;
                if (property.Kind is not Expr.ObjectPropertyKind.Value) _owner = property;
                try
                {
                    if (property.SetterParam is { } parameter) Parameters([parameter]);
                    Visit(property.Value);
                }
                finally { _owner = previous; }
            }
        }
        protected override void VisitDestructuringAssign(Expr.DestructuringAssign expression)
        {
            base.VisitDestructuringAssign(expression);
            if (expression.RawTarget is { } target) Visit(target);
            if (expression.RawDefault is { } fallback) Visit(fallback);
        }
        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            analysis._tries.Add(statement); base.VisitTryCatch(statement);
        }
    }
}
