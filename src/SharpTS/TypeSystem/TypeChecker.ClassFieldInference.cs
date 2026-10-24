using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record ClassFieldConstWidening(TypeInfo Type);
    private readonly ConditionalWeakTable<BindingSymbol, ClassFieldConstWidening> _classFieldConstWidening = new();
    private readonly ConditionalWeakTable<TypeInfo, ClassFieldConstWidening> _classFieldReadonlyWidening = new();

    private sealed class FieldGetterDependency(Stmt.Accessor accessor)
    {
        public Stmt.Accessor Accessor { get; } = accessor;
        public bool HasUnresolvedDependency { get; set; }
    }

    private sealed class FieldGetterScope(
        TypeInfo.MutableClass owner, TypeEnvironment environment,
        TypeInfo.Class?[] enclosingClasses, FieldGetterScope? previous)
    {
        public TypeInfo.MutableClass Owner { get; } = owner;
        public TypeEnvironment Environment { get; } = environment;
        public TypeInfo.Class?[] EnclosingClasses { get; } = enclosingClasses;
        public FieldGetterScope? Previous { get; } = previous;
        public Dictionary<(bool IsStatic, string Name), Stmt.Accessor> Getters { get; } = [];
        public Dictionary<Stmt.Accessor, TypeInfo> Resolved { get; } = new(ReferenceEqualityComparer.Instance);
        public List<FieldGetterDependency> Active { get; } = [];
        public HashSet<(bool IsStatic, string Name)> PendingFields { get; } = [];
        public Dictionary<(bool IsStatic, string Name), Stmt.Field> Fields { get; } = [];
        public HashSet<Stmt.Field> ActiveFields { get; } = new(ReferenceEqualityComparer.Instance);
        public int DeferredReadDepth { get; set; }
    }

    private FieldGetterScope? _fieldGetterScope;
    private int _classFieldDeferredReadDepth;
    // Early inference is provisional: the ordinary body pass must see completed
    // fields and self bindings, rather than treating its result as an annotation.
    private readonly ConditionalWeakTable<TypeInfo.MutableClass, HashSet<Stmt.Accessor>>
        _fieldStageInferredGetters = new();

    private FieldGetterScope CreateFieldGetterScope(
        object sourceOwner, IReadOnlyList<Stmt.Field> fields,
        TypeInfo.MutableClass owner, TypeEnvironment environment)
    {
        var scope = new FieldGetterScope(owner, environment,
            new[] { _currentClass }.Concat(_privateNameEnclosingClasses).ToArray(), _fieldGetterScope)
            { DeferredReadDepth = _classFieldDeferredReadDepth };
        var accessors = sourceOwner switch
        {
            Stmt.Class declaration => declaration.Accessors,
            Expr.ClassExpr expression => expression.Accessors,
            _ => null
        };
        if (accessors is not null)
            foreach (var accessor in accessors)
            {
                if (accessor.Kind.Type != TokenType.GET ||
                    accessor.ReturnType is not null || accessor.ReturnTypeNode is not null)
                    continue;
                string? name = ClassAccessorName(accessor);
                if (name is null) continue;
                var getters = accessor.IsStatic ? owner.StaticGetters : owner.Getters;
                if (!getters.TryGetValue(name, out var type)) continue;
                // Expression instance getters have a temporary permissive view
                // until the normal body pass; an explicit setter annotation is
                // already a real contextual type and retains its old policy.
                bool pendingExpressionGetter = sourceOwner is Expr.ClassExpr && !accessor.IsStatic &&
                    type is TypeInfo.Any && !accessors.Any(candidate =>
                        candidate.Kind.Type == TokenType.SET && !candidate.IsStatic &&
                        ClassAccessorName(candidate) == name && candidate.SetterParam?.Type is not null);
                if (type is TypeInfo.Inferred || pendingExpressionGetter)
                    scope.Getters[(accessor.IsStatic, name)] = accessor;
            }
        foreach (var field in fields)
            if (field.Initializer is not null && !field.IsDeclare &&
                field.TypeAnnotation is null && field.TypeAnnotationNode is null)
            {
                var key = (field.IsStatic, GetFieldMemberName(field));
                scope.PendingFields.Add(key);
                scope.Fields[key] = field;
            }
        return scope;
    }

    private string? ClassAccessorName(Stmt.Accessor accessor) => accessor.ComputedKey is not null
        ? TryGetWellKnownSymbolMemberName(accessor.ComputedKey) : accessor.Name.Lexeme;

    private FieldGetterScope? FindFieldGetterScope(TypeInfo owner)
    {
        int ownerId = SelectedSourceClassId(owner);
        if (ownerId == 0) return null;
        for (var scope = _fieldGetterScope; scope is not null; scope = scope.Previous)
            if (scope.Owner.DeclarationId == ownerId) return scope;
        return null;
    }

    private void ObservePendingGetterFieldRead(TypeInfo owner, bool isStatic, string name)
    {
        if (FindFieldGetterScope(owner) is not { } scope ||
            !scope.PendingFields.Contains((isStatic, name))) return;
        if (_classFieldDeferredReadDepth > scope.DeferredReadDepth && scope.Fields.TryGetValue((isStatic, name), out var field))
        {
            InferDeferredClassFieldDependency(scope, field);
            return;
        }
        MarkFieldGetterDependenciesUnresolved();
    }

    private TypeInfo CompletedFieldDependencyType(TypeInfo owner, bool isStatic, string name, TypeInfo fallback)
    {
        if (FindFieldGetterScope(owner) is not { } scope) return fallback;
        var members = isStatic ? scope.Owner.StaticProperties : scope.Owner.FieldTypes;
        if (members.TryGetValue(name, out var type)) return type;
        return (isStatic ? scope.Owner.StaticPrivateFields : scope.Owner.PrivateFields).GetValueOrDefault(name) ?? fallback;
    }

    private void InferDeferredClassFieldDependency(FieldGetterScope scope, Stmt.Field field)
    {
        // A recursive read keeps the current placeholder until the active initializer
        // publishes its result. An annotated recursive callback can still complete precisely.
        if (!scope.ActiveFields.Add(field)) return;
        var previousEnvironment = _environment;
        var previousClass = _currentClass;
        var previousThis = _currentFunctionThisType;
        bool previousStatic = _inStaticMethod;
        bool previousStaticBlock = _inStaticBlock;
        var assignmentState = SnapshotDefiniteAssignmentState();
        var aliases = new Dictionary<string, string>(_variableAliases, StringComparer.Ordinal);
        var environments = new Dictionary<TypeEnvironment, Dictionary<string, TypeInfo>?>(ReferenceEqualityComparer.Instance);
        for (TypeEnvironment? environment = scope.Environment; environment is not null; environment = environment.Enclosing)
            environments[environment] = environment.SnapshotLocalValueTypes();
        PushNarrowingScope();
        _privateNameEnclosingClasses.Push(previousClass);
        _environment = new TypeEnvironment(scope.Environment);
        _currentClass = scope.Owner.Freeze();
        _currentFunctionThisType = field.IsStatic ? _currentClass : new TypeInfo.Instance(scope.Owner);
        _environment.Define("this", _currentFunctionThisType);
        _inStaticMethod = field.IsStatic;
        _inStaticBlock = false;
        try
        {
            // Only a field requested by a deferred callback is inferred early. Restore
            // flow effects so its ordinary initializer pass retains source-order behavior.
            CheckAndInferClassFieldInitializer(field, scope.Owner);
            scope.PendingFields.Remove((field.IsStatic, GetFieldMemberName(field)));
        }
        finally
        {
            _privateNameEnclosingClasses.Pop();
            PopNarrowingScope();
            RestoreDefiniteAssignmentState(assignmentState);
            _variableAliases.Clear();
            foreach (var (name, alias) in aliases) _variableAliases.Add(name, alias);
            foreach (var (environment, values) in environments) environment.RestoreLocalValueTypes(values);
            _environment = previousEnvironment;
            _currentClass = previousClass;
            _currentFunctionThisType = previousThis;
            _inStaticMethod = previousStatic;
            _inStaticBlock = previousStaticBlock;
            scope.ActiveFields.Remove(field);
        }
    }

    private void MarkFieldGetterDependenciesUnresolved()
    {
        for (var scope = _fieldGetterScope; scope is not null; scope = scope.Previous)
            foreach (var dependency in scope.Active) dependency.HasUnresolvedDependency = true;
    }

    private void ObservePendingGetterIndexRead(TypeInfo receiver, string name)
    {
        if (_fieldGetterScope is null) return;
        bool isStatic = receiver is not TypeInfo.Instance;
        TypeInfo? current = receiver is TypeInfo.Instance instance ? instance.ResolvedClassType : receiver;
        HashSet<TypeInfo> visited = new(ReferenceEqualityComparer.Instance);
        for (; current is not null && visited.Add(current); current = GetSuperclass(current))
        {
            if ((isStatic ? GetStaticMethods(current) : GetMethods(current))?.ContainsKey(name) == true ||
                (isStatic ? TryGetAccessorSetterAccess(current, isStatic: true, name, out _)
                    : GetGetters(current)?.ContainsKey(name) == true ||
                        GetSetters(current)?.ContainsKey(name) == true))
                return;
            if ((isStatic ? GetStaticProperties(current) : GetFieldTypes(current))?.ContainsKey(name) == true)
            {
                ObservePendingGetterFieldRead(current, isStatic, name);
                return;
            }
        }
    }

    private TypeInfo ResolveFieldStageGetter(TypeInfo owner, bool isStatic, string name, TypeInfo type)
    {
        if (FindFieldGetterScope(owner) is not { } scope ||
            !scope.Getters.TryGetValue((isStatic, name), out var accessor)) return type;
        if (scope.Resolved.TryGetValue(accessor, out var resolved))
        {
            if (resolved is TypeInfo.Inferred) MarkFieldGetterDependenciesUnresolved();
            return resolved;
        }
        if (scope.Active.Any(dependency => ReferenceEquals(dependency.Accessor, accessor)))
        {
            MarkFieldGetterDependenciesUnresolved();
            return TypeInfo.Inferred.Shared;
        }

        var getters = isStatic ? scope.Owner.StaticGetters : scope.Owner.Getters;
        var setters = isStatic ? scope.Owner.StaticSetters : scope.Owner.Setters;
        TypeInfo originalGetter = getters[name];
        bool hadSetter = setters.TryGetValue(name, out var originalSetter);
        var dependency = new FieldGetterDependency(accessor);
        var previousEnvironment = _environment;
        var previousClass = _currentClass;
        var previousEnclosingClasses = _privateNameEnclosingClasses.ToArray();
        bool previousStaticBlock = _inStaticBlock;
        bool previousGlobalThis = _currentArrowCapturesScriptGlobalThis;
        scope.Active.Add(dependency);
        getters[name] = TypeInfo.Inferred.Shared;
        scope.Owner.ResetFrozenCache();
        _environment = scope.Environment;
        _currentClass = scope.Owner.Freeze();
        _privateNameEnclosingClasses.Clear();
        foreach (var enclosingClass in scope.EnclosingClasses.Reverse())
            _privateNameEnclosingClasses.Push(enclosingClass);
        _inStaticBlock = false;
        _currentArrowCapturesScriptGlobalThis = false;
        try
        {
            CheckClassAccessorBodies([accessor], scope.Owner, recoverComputedSymbolDiagnostics: false);
            if (dependency.HasUnresolvedDependency)
            {
                // Do not turn an unchecked field's permissive placeholder into
                // a callable/assignable getter result. Forward fields are not
                // rechecked or evaluated by this dependency resolver.
                getters[name] = originalGetter;
                if (hadSetter) setters[name] = originalSetter!;
                PublishStaticAccessorTypes(scope.Owner);
                resolved = TypeInfo.Inferred.Shared;
            }
            else
            {
                resolved = getters[name];
                _fieldStageInferredGetters.GetOrCreateValue(scope.Owner).Add(accessor);
            }
            scope.Resolved[accessor] = resolved;
            return resolved;
        }
        catch
        {
            getters[name] = originalGetter;
            if (hadSetter) setters[name] = originalSetter!;
            PublishStaticAccessorTypes(scope.Owner);
            throw;
        }
        finally
        {
            scope.Active.RemoveAt(scope.Active.Count - 1);
            scope.Owner.ResetFrozenCache();
            _environment = previousEnvironment;
            _currentClass = previousClass;
            _privateNameEnclosingClasses.Clear();
            foreach (var enclosingClass in previousEnclosingClasses.Reverse())
                _privateNameEnclosingClasses.Push(enclosingClass);
            _inStaticBlock = previousStaticBlock;
            _currentArrowCapturesScriptGlobalThis = previousGlobalThis;
        }
    }

    // A const's literal type alone cannot distinguish `const n = 1` from
    // `const n: 1 = 1`. Retain the mutable-slot view under the actual source binding,
    // including aliases/imports, without rechecking its initializer later.
    private void RecordClassFieldConstWidening(Stmt.Const declaration, TypeInfo type)
    {
        if (_environment.GetValueBinding(declaration.Name.Lexeme) is not { } binding) return;
        _classFieldConstWidening.Remove(binding);
        if (declaration.TypeAnnotation != null || declaration.TypeAnnotationNode != null) return;
        _classFieldConstWidening.Add(binding, new ClassFieldConstWidening(
            WidenClassFieldInitializerType(declaration.Initializer, type)));
    }

    /// <summary>
    /// Checks a field once in the caller's initializer context and publishes its inferred member
    /// type. The caller refreshes the class bindings before checking subsequent members.
    /// </summary>
    private bool CheckAndInferClassFieldInitializer(Stmt.Field field, TypeInfo.MutableClass owner)
    {
        if (field.Initializer == null || field.IsDeclare)
            return false;

        TypeInfo initializerType = CheckExpr(field.Initializer);
        Dictionary<string, TypeInfo> members = (field.IsStatic, field.IsPrivate) switch
        {
            (true, true) => owner.StaticPrivateFields,
            (true, false) => owner.StaticProperties,
            (false, true) => owner.PrivateFields,
            _ => owner.FieldTypes
        };
        string memberName = GetFieldMemberName(field);

        // Explicit any and node-only annotations are declarations, not inference placeholders.
        if (field.TypeAnnotation != null || field.TypeAnnotationNode != null)
        {
            if (members.TryGetValue(memberName, out var declaredType)
                && declaredType is not TypeInfo.Inferred
                && !IsCompatible(declaredType, initializerType))
            {
                string memberKind = field.IsStatic ? "static property" : "field";
                throw new TypeCheckException(
                    $" Cannot assign type '{initializerType}' to {memberKind} '{field.Name.Lexeme}' of type '{declaredType}'.",
                    line: field.Name.Line, tsCode: "TS2322");
            }
            return false;
        }

        TypeInfo inferredType = WidenClassFieldInitializerType(
            field.Initializer, initializerType, preserveLiterals: field.IsReadonly);
        if (field.IsReadonly && inferredType is TypeInfo.NumberLiteral or TypeInfo.StringLiteral
            or TypeInfo.BooleanLiteral or TypeInfo.BigIntLiteral or TypeInfo.Union)
        {
            // An inferred readonly literal is fresh when copied to a mutable slot;
            // annotated/asserted readonly literals retain their already-fixed types.
            TypeInfo widened = WidenClassFieldInitializerType(field.Initializer, initializerType);
            if (!TypeInfoEqualityComparer.Instance.Equals(inferredType, widened))
                _classFieldReadonlyWidening.AddOrUpdate(inferredType, new ClassFieldConstWidening(widened));
        }
        if (members.TryGetValue(memberName, out var previousType)
            && ReferenceEquals(previousType, inferredType))
            return false;

        members[memberName] = inferredType;
        owner.ResetFrozenCache();
        return true;
    }

    /// <summary>
    /// Widens fresh literal expressions, retaining already-fixed types contributed by calls,
    /// assertions, and aliases. All child types come from the initializer's completed check.
    /// </summary>
    private TypeInfo WidenClassFieldInitializerType(Expr expression, TypeInfo type, bool preserveLiterals = false)
    {
        Expr source = UnwrapTransparentForWidening(expression);
        return source switch
        {
            Expr.TypeAssertion => type,
            Expr.Variable variable when !preserveLiterals
                && GetClassFieldWideningBinding(variable.Name) is { } binding
                && _classFieldConstWidening.TryGetValue(binding, out var widening) => widening.Type,
            Expr.Variable => WidenClassFieldNullishType(type),
            Expr.Literal => WidenClassFieldLiteralType(type, preserveLiterals),
            Expr.ObjectLiteral literal when type is TypeInfo.Record record =>
                WidenClassFieldObjectLiteral(literal, record),
            Expr.ArrayLiteral literal when type is TypeInfo.Array array =>
                WidenClassFieldArrayLiteral(literal, array),
            Expr.ArrowFunction arrow when arrow.ReturnType == null && arrow.ReturnTypeNode == null && !arrow.IsGenerator =>
                WidenClassFieldArrowReturn(arrow, type),
            Expr.Call call when call.TypeArgs is not { Count: > 0 }
                && _typeMap.Get(call.Callee) is TypeInfo.GenericFunction { ReturnType: TypeInfo.TypeParameter } =>
                WidenClassFieldGenericCall(call, type, preserveLiterals),
            Expr.Ternary conditional => CreateUnion(
                WidenClassFieldInitializerType(conditional.ThenBranch, _typeMap.Get(conditional.ThenBranch) ?? type, preserveLiterals),
                WidenClassFieldInitializerType(conditional.ElseBranch, _typeMap.Get(conditional.ElseBranch) ?? type, preserveLiterals)),
            Expr.Comma comma => WidenClassFieldInitializerType(comma.Right, type, preserveLiterals),
            Expr.Assign assignment => WidenClassFieldInitializerType(assignment.Value, type, preserveLiterals),
            Expr.Get or Expr.GetPrivate or Expr.GetIndex when !preserveLiterals
                && _classFieldReadonlyWidening.TryGetValue(type, out var widening) => widening.Type,
            _ => WidenClassFieldNullishType(type)
        };
    }

    private BindingSymbol? GetClassFieldWideningBinding(Token name) =>
        Bindings.GetSymbol(name) ?? (name.Start < 0 ? _environment.GetValueBinding(name.Lexeme) : null);

    private TypeInfo WidenClassFieldGenericCall(Expr.Call call, TypeInfo type, bool preserveLiterals)
    {
        // A naked inferred T return retains its argument's literal freshness. A declared
        // literal return or explicit type argument instead supplies an already-fixed type.
        foreach (var argument in call.Arguments)
            if (ReferenceEquals(_typeMap.Get(argument), type))
                return WidenClassFieldInitializerType(argument, type, preserveLiterals);
        return WidenClassFieldNullishType(type);
    }

    private TypeInfo WidenClassFieldLiteralType(TypeInfo type, bool preserveLiterals)
        => type switch
        {
            TypeInfo.NumberLiteral => preserveLiterals ? type : TypeInfo.Primitive.Number,
            TypeInfo.StringLiteral => preserveLiterals ? type : TypeInfo.String.Shared,
            TypeInfo.BooleanLiteral => preserveLiterals ? type : TypeInfo.Primitive.Boolean,
            TypeInfo.BigIntLiteral => preserveLiterals ? type : TypeInfo.BigInt.Shared,
            _ => WidenClassFieldNullishType(type)
        };

    private TypeInfo WidenClassFieldNullishType(TypeInfo type) =>
        !_strictNullChecks && type is TypeInfo.Null or TypeInfo.Undefined ? TypeInfo.Any.Shared : type;

    private TypeInfo.Record WidenClassFieldObjectLiteral(Expr.ObjectLiteral literal, TypeInfo.Record record)
    {
        if (record.IsReadonly) return record;
        var fields = record.Fields.ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach (var property in literal.Properties)
        {
            if (property.IsSpread)
            {
                if (_typeMap.Get(property.Value) is not { } spreadType) continue;
                TypeInfo widened = WidenClassFieldInitializerType(property.Value, spreadType);
                foreach (string name in EnumerateRecordLikeFieldNames(widened))
                    if (record.Fields.ContainsKey(name))
                    {
                        if (widened is TypeInfo.Record spreadRecord && spreadRecord.Fields.TryGetValue(name, out var member))
                            fields[name] = member;
                        else
                            fields[name] = record.Fields[name];
                    }
            }
            else if (property.Kind == Expr.ObjectPropertyKind.Value
                && TryGetClassFieldLiteralMemberName(property.Key, out string name)
                && fields.TryGetValue(name, out var memberType))
            {
                fields[name] = WidenClassFieldInitializerType(
                    property.Value, _typeMap.Get(property.Value) ?? memberType);
            }
        }
        // Keep call/construct signatures, optional/getter-only members, and index metadata.
        return record with { Fields = fields.ToFrozenDictionary() };
    }

    private bool TryGetClassFieldLiteralMemberName(Expr.PropertyKey? key, out string name)
    {
        if (TryGetStaticFieldNameForWidening(key, out name)) return true;
        name = key switch
        {
            Expr.ComputedKey computed when TryGetWellKnownSymbolMemberName(computed.Expression) is { } symbol => symbol,
            Expr.ComputedKey computed when _typeMap.Get(computed.Expression) is TypeInfo.StringLiteral text => text.Value,
            Expr.ComputedKey computed when _typeMap.Get(computed.Expression) is TypeInfo.NumberLiteral number =>
                number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Expr.LiteralKey { Literal.Literal: double number } => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null!
        };
        return name != null;
    }

    private TypeInfo WidenClassFieldArrayLiteral(Expr.ArrayLiteral literal, TypeInfo.Array array)
    {
        if (array.IsReadonly) return array;
        List<TypeInfo> elements = [];
        foreach (var element in literal.Elements)
        {
            if (element is Expr.Spread spread)
            {
                if (_typeMap.Get(spread.Expression) is not { } sourceType) return array;
                TypeInfo widened = WidenClassFieldInitializerType(spread.Expression, sourceType);
                if (widened is TypeInfo.Tuple tuple)
                {
                    elements.AddRange(tuple.ElementTypes);
                    if (tuple.RestElementType != null) elements.Add(tuple.RestElementType);
                }
                else if (TryGetSpreadElementType(widened, out var spreadElement)) elements.Add(spreadElement);
                else return array;
            }
            else
            {
                if (_typeMap.Get(element) is not { } elementType) return array;
                elements.Add(WidenClassFieldInitializerType(element, elementType));
            }
        }
        if (elements.Count == 0) return array;
        TypeInfo commonType = elements[0];
        foreach (var element in elements.Skip(1))
        {
            if (IsCompatible(commonType, element)) continue;
            commonType = IsCompatible(element, commonType) ? element : CreateUnion(commonType, element);
        }
        return array with { ElementType = commonType };
    }

    private TypeInfo WidenClassFieldArrowReturn(Expr.ArrowFunction arrow, TypeInfo type)
    {
        TypeInfo? returnType = type switch
        {
            TypeInfo.Function function => function.ReturnType,
            TypeInfo.GenericFunction function => function.ReturnType,
            _ => null
        };
        if (returnType == null) return type;
        List<Expr> returns = [];
        if (arrow.ExpressionBody != null) returns.Add(arrow.ExpressionBody);
        else if (arrow.BlockBody != null)
        {
            var collector = new ClassMemberReturnCollector(returns);
            foreach (var statement in arrow.BlockBody) collector.Visit(statement);
        }
        if (returns.Count == 0) return type;
        TypeInfo? widened = null;
        foreach (var expression in returns)
        {
            if (_typeMap.Get(expression) is not { } expressionType) return type;
            if (arrow.IsAsync) expressionType = ResolveAwaitedType(expressionType);
            TypeInfo member = WidenClassFieldInitializerType(expression, expressionType);
            widened = widened == null ? member : CreateUnion(widened, member);
        }
        TypeInfo originalReturn = arrow.IsAsync ? ResolveAwaitedType(returnType) : returnType;
        IEnumerable<TypeInfo> originalMembers = originalReturn is TypeInfo.Union union
            ? union.FlattenedTypes : [originalReturn];
        foreach (var member in originalMembers)
            if (member is TypeInfo.Undefined or TypeInfo.Void or TypeInfo.Null)
                widened = CreateUnion(widened!, member);
        if (arrow.IsAsync) widened = new TypeInfo.Promise(widened!);
        return type switch
        {
            TypeInfo.Function function => function with { ReturnType = widened! },
            TypeInfo.GenericFunction function => function with { ReturnType = widened! },
            _ => type
        };
    }

    private sealed class ClassMemberReturnCollector(List<Expr> returns) : AstVisitorBase
    {
        protected override void VisitReturn(Stmt.Return statement)
        {
            if (statement.Value != null) returns.Add(statement.Value);
        }
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
    }
}
