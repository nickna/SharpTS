using SharpTS.Parsing.Visitors;

namespace SharpTS.Parsing;

/// <summary>
/// Filters speculative/abandoned parse objects using the final AST. Runtime dispatch comes from
/// the existing AST visitor; auxiliary/type syntax is explicit because it is outside that catalog.
/// No reflection or second syntax tree is needed in the editor or Native AOT builds.
/// </summary>
internal sealed class EditorSyntaxReachability(CancellationToken cancellationToken,
    IReadOnlyDictionary<object, List<TypeNode>>? attachedTypes) : AstVisitorBase
{
    private readonly HashSet<object> _reachable = new(ReferenceEqualityComparer.Instance);

    public static HashSet<object> Collect(IReadOnlyList<Stmt> roots, CancellationToken ct,
        IReadOnlyDictionary<object, List<TypeNode>>? attachedTypes = null)
    {
        var visitor = new EditorSyntaxReachability(ct, attachedTypes);
        foreach (Stmt root in roots) visitor.Visit(root);
        return visitor._reachable;
    }

    private bool Add(object node)
    {
        if (!_reachable.Add(node)) return false;
        if (attachedTypes is not null && attachedTypes.TryGetValue(node, out var types))
            foreach (TypeNode type in types) Type(type);
        return true;
    }

    public override void Visit(Expr expr)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Add(expr)) return;
        switch (expr)
        {
            case Expr.Assign assign: Type(assign.RedeclarationTypeAnnotationNode); break;
            case Expr.Call call:
                Types(call.TypeArgNodes); Types(call.JsxOrigin?.TypeArgumentNodes); break;
            case Expr.New construct: Types(construct.TypeArgNodes); Visit(construct.Callee); break;
            case Expr.ArrowFunction arrow:
                TypeParameters(arrow.TypeParams); Parameters(arrow.Parameters);
                Type(arrow.ThisTypeNode); Type(arrow.ReturnTypeNode); break;
            case Expr.TypeAssertion assertion: Type(assertion.TargetTypeNode); break;
            case Expr.Satisfies satisfies: Type(satisfies.ConstraintTypeNode); break;
            case Expr.ObjectLiteral literal:
                foreach (var property in literal.Properties)
                {
                    Add(property);
                    if (property.Key is not null) Add(property.Key);
                    if (property.SetterParam is not null) Parameters([property.SetterParam]);
                }
                break;
            case Expr.DestructuringAssign destructuring:
                if (destructuring.RawTarget is not null) Visit(destructuring.RawTarget);
                if (destructuring.RawDefault is not null) Visit(destructuring.RawDefault);
                break;
        }
        base.Visit(expr);
    }

    public override void Visit(Stmt stmt)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Add(stmt)) return;
        switch (stmt)
        {
            case Stmt.Var variable:
                Type(variable.TypeAnnotationNode);
                if (variable.HoistTypeInferenceInitializer is not null) Visit(variable.HoistTypeInferenceInitializer);
                break;
            case Stmt.Const constant: Type(constant.TypeAnnotationNode); break;
            case Stmt.Function function:
                TypeParameters(function.TypeParams); Parameters(function.Parameters);
                Type(function.ThisTypeNode); Type(function.ReturnTypeNode); Decorators(function.Decorators);
                if (function.ComputedKey is not null) Visit(function.ComputedKey);
                break;
            case Stmt.Field field: Type(field.TypeAnnotationNode); Decorators(field.Decorators); break;
            case Stmt.AutoAccessor accessor: Type(accessor.TypeAnnotationNode); Decorators(accessor.Decorators); break;
            case Stmt.Accessor accessor:
                Type(accessor.ReturnTypeNode); Decorators(accessor.Decorators);
                if (accessor.SetterParam is not null) Parameters([accessor.SetterParam]);
                break;
            case Stmt.TypeAlias alias: TypeParameters(alias.TypeParameters); Type(alias.TypeDefinitionNode); break;
            case Stmt.Interface declaration:
                TypeParameters(declaration.TypeParams); Types(declaration.ExtendsNodes);
                foreach (var member in declaration.Members) { Add(member); Type(member.TypeAnnotationNode); }
                IndexSignatures(declaration.IndexSignatures);
                foreach (var signature in declaration.CallSignatures ?? [])
                {
                    Add(signature); TypeParameters(signature.TypeParams);
                    Parameters(signature.Parameters); Type(signature.ReturnTypeNode);
                }
                foreach (var signature in declaration.ConstructorSignatures ?? [])
                {
                    Add(signature); TypeParameters(signature.TypeParams);
                    Parameters(signature.Parameters); Type(signature.ReturnTypeNode);
                }
                break;
            case Stmt.TryCatch statement: Type(statement.CatchParamTypeNode); break;
            case Stmt.Import import:
                foreach (var specifier in import.NamedImports ?? []) Add(specifier);
                break;
            case Stmt.Export export:
                foreach (var specifier in export.NamedExports ?? []) Add(specifier);
                break;
            case Stmt.Enum declaration:
                foreach (var member in declaration.Members) Add(member);
                break;
            case Stmt.FileDirective directive: Decorators(directive.Decorators); break;
        }
        base.Visit(stmt);
    }

    protected override void VisitClass(Stmt.Class stmt)
    {
        TypeParameters(stmt.TypeParams); Types(stmt.SuperclassTypeArgNodes);
        foreach (var types in stmt.InterfaceTypeArgNodes ?? []) Types(types);
        IndexSignatures(stmt.IndexSignatures); Decorators(stmt.Decorators);
        if (stmt.SuperclassExpr is not null) Visit(stmt.SuperclassExpr);
        foreach (var field in stmt.Fields) Visit(field);
        foreach (var method in stmt.Methods) Visit(method);
        foreach (var accessor in stmt.Accessors ?? []) Visit(accessor);
        foreach (var accessor in stmt.AutoAccessors ?? []) Visit(accessor);
        foreach (var initializer in stmt.StaticInitializers ?? []) Visit(initializer);
    }

    protected override void VisitClassExpr(Expr.ClassExpr expr)
    {
        TypeParameters(expr.TypeParams); Types(expr.SuperclassTypeArgNodes);
        foreach (var types in expr.InterfaceTypeArgNodes ?? []) Types(types);
        if (expr.SuperclassExpr is not null) Visit(expr.SuperclassExpr);
        foreach (var field in expr.Fields) Visit(field);
        foreach (var method in expr.Methods) Visit(method);
        foreach (var accessor in expr.Accessors ?? []) Visit(accessor);
        foreach (var accessor in expr.AutoAccessors ?? []) Visit(accessor);
        foreach (var initializer in expr.StaticInitializers ?? []) Visit(initializer);
    }

    private void Parameters(IEnumerable<Stmt.Parameter> parameters)
    {
        foreach (var parameter in parameters)
        {
            Add(parameter); Type(parameter.TypeAnnotationNode); Decorators(parameter.Decorators);
            if (parameter.DefaultValue is not null) Visit(parameter.DefaultValue);
            foreach (var property in parameter.DestructuredProperties ?? [])
            {
                Add(property);
                if (property.DefaultValue is not null) Visit(property.DefaultValue);
            }
        }
    }

    private void TypeParameters(IEnumerable<TypeParam>? parameters)
    {
        foreach (var parameter in parameters ?? [])
        {
            Add(parameter); Type(parameter.ConstraintNode); Type(parameter.DefaultNode);
        }
    }

    private void IndexSignatures(IEnumerable<Stmt.IndexSignature>? signatures)
    {
        foreach (var signature in signatures ?? []) { Add(signature); Type(signature.ValueTypeNode); }
    }

    private void Decorators(IEnumerable<Decorator>? decorators)
    {
        foreach (var decorator in decorators ?? []) { Add(decorator); Visit(decorator.Expression); }
    }

    private void Types(IEnumerable<TypeNode?>? types)
    {
        foreach (var type in types ?? []) Type(type);
    }

    private void Type(TypeNode? node)
    {
        if (node is null || !Add(node)) return;
        cancellationToken.ThrowIfCancellationRequested();
        switch (node)
        {
            case NamedTypeNode named: Types(named.TypeArguments); break;
            case ReadonlyTypeNode type: Type(type.Inner); break;
            case TypePredicateNode type: Type(type.PredicateType); break;
            case ArrayTypeNode type: Type(type.ElementType); break;
            case UnionTypeNode type: Types(type.Members); break;
            case IntersectionTypeNode type: Types(type.Members); break;
            case KeyofTypeNode type: Type(type.Operand); break;
            case IndexedAccessTypeNode type: Type(type.ObjectType); Type(type.IndexType); break;
            case ConditionalTypeNode type:
                Type(type.CheckType); Type(type.ExtendsType); Type(type.TrueType); Type(type.FalseType); break;
            case InferTypeNode type: Type(type.Constraint); break;
            case FunctionTypeNode type: Type(type.ThisType); TypeParameters(type.Parameters); Type(type.ReturnType); break;
            case ConstructorTypeNode type: Type(type.ThisType); TypeParameters(type.Parameters); Type(type.ReturnType); break;
            case GenericFunctionTypeNode type: TypeParameters(type.TypeParameters); Type(type.Body); break;
            case GenericConstructorTypeNode type: TypeParameters(type.TypeParameters); Type(type.Body); break;
            case TemplateLiteralTypeNode type: Types(type.InterpolatedTypes); break;
            case MappedTypeNode type: Type(type.Constraint); Type(type.ValueType); Type(type.AsClause); break;
            case TupleTypeNode type:
                foreach (var element in type.Elements) { Add(element); Type(element.Type); }
                break;
            case ObjectTypeNode type:
                foreach (var member in type.Members)
                {
                    Add(member);
                    switch (member)
                    {
                        case PropertyMemberNode property: Type(property.Type); break;
                        case IndexSignatureNode signature: Type(signature.ValueType); break;
                        case CallSignatureMemberNode signature: Type(signature.Signature); break;
                        case ConstructSignatureMemberNode signature: Type(signature.Signature); break;
                    }
                }
                break;
            case LiteralTypeNode or UniqueSymbolTypeNode or AssertsNonNullTypeNode or TypeQueryNode: break;
            default: throw new NotSupportedException($"Editor syntax reachability has no type case for {node.GetType().Name}.");
        }
    }

    private void TypeParameters(IEnumerable<ParameterTypeNode> parameters)
    {
        foreach (var parameter in parameters) { Add(parameter); Type(parameter.Type); }
    }
}
