using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private int _loopBodyCallDepth;

    private TypeInfo EvaluateLoopBodyImmediateCall(Expr.Call call, Expr.ArrowFunction callable, LoopBodyEvaluation evaluation)
    {
        if (callable.IsAsync || callable.IsGenerator || callable.TypeParams is { Count: > 0 } || call.Arguments.Any(argument => argument is Expr.Spread))
            throw new UnprovedLoopBodyFlow();
        if (++_loopBodyCallDepth > 32)
        {
            _loopBodyCallDepth--;
            throw new UnprovedLoopBodyFlow();
        }
        try
        {
            var arguments = call.Arguments.Select(argument => EvaluateLoopBodyValue(argument, evaluation)).ToArray();
            var callerKeys = evaluation.Values.Keys.ToHashSet(ReferenceEqualityComparer.Instance);
            var scope = new LoopBodyScope(evaluation.Scope, evaluation.Scope.Environment);
            foreach (var parameter in callable.Parameters)
            {
                scope.Locals[parameter.Name.Lexeme] = parameter.Name;
                evaluation.Values.Remove(parameter.Name);
            }
            if (callable.Name is { } name) scope.Locals.TryAdd(name.Lexeme, name);
            if (callable.BlockBody is { } statements)
            {
                var variables = new LoopBodyFunctionVars(scope);
                foreach (var statement in statements) variables.Visit(statement);
            }
            var graph = BuildLoopBodyGraph(null, callable, scope, evaluation.Graph);
            if (callable.HasOwnThis)
                graph.ThisType = LoopBodyAnnotation(callable.ThisTypeNode, callable.ThisType, scope) ?? TypeInfo.Any.Shared;
            var body = new LoopBodyEvaluation(graph, scope, evaluation.Values);
            for (int i = 0; i < callable.Parameters.Count; i++)
            {
                var parameter = callable.Parameters[i];
                TypeInfo value;
                if (parameter.IsRest)
                    value = new TypeInfo.Array(UnionLoopBodyValues(arguments.Skip(i)));
                else
                {
                    value = i < arguments.Length ? arguments[i] : TypeInfo.Undefined.Shared;
                    if (value is TypeInfo.Undefined && parameter.DefaultValue is { } initializer)
                        value = EvaluateLoopBodyValue(initializer, body);
                }
                if (parameter.Type is not null || parameter.TypeAnnotationNode is not null)
                    value = LoopBodyAnnotation(parameter.TypeAnnotationNode, parameter.Type, scope) ?? throw new UnprovedLoopBodyFlow();
                body.Values[parameter.Name] = value;
            }
            var flow = EvaluateLoopBodyGraph(graph, body.Values);
            evaluation.Throws.AddRange(body.Throws.Select(ProjectCaller));
            evaluation.Writes.UnionWith(body.Writes.Where(Visible));
            if (flow.Thrown is { } thrown) evaluation.Throws.Add(ProjectCaller(thrown));
            if (flow.Exit is not { } completed) throw new UnprovedLoopBodyFlow();
            var returned = completed.GetValueOrDefault(graph.ReturnSlot!) ?? TypeInfo.Undefined.Shared;
            var caller = ProjectCaller(completed);
            evaluation.Values.Clear();
            foreach (var pair in caller) evaluation.Values[pair.Key] = pair.Value;
            evaluation.Writes.UnionWith(flow.Writes.Where(Visible));
            return returned;

            bool Visible(object slot) => slot is BindingSymbol || callerKeys.Contains(slot) ||
                graph.MemberPaths.TryGetValue(slot, out var member) && (member.Root is BindingSymbol || callerKeys.Contains(member.Root));
            Dictionary<object, TypeInfo> ProjectCaller(Dictionary<object, TypeInfo> state) =>
                state.Where(pair => Visible(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, ReferenceEqualityComparer.Instance);
        }
        finally { _loopBodyCallDepth--; }
    }

    private sealed class LoopBodyFunctionVars(LoopBodyScope scope) : AstVisitorBase
    {
        protected override void VisitVar(Stmt.Var declaration)
        {
            if (declaration.IsVar) scope.Locals.TryAdd(declaration.Name.Lexeme, declaration.Name);
            base.VisitVar(declaration);
        }
        protected override void VisitForOf(Stmt.ForOf statement)
        {
            if (statement.IsDeclaration && statement.IsVar) scope.Locals.TryAdd(statement.Variable.Lexeme, statement.Variable);
            base.VisitForOf(statement);
        }
        protected override void VisitForIn(Stmt.ForIn statement)
        {
            if (statement.IsDeclaration && statement.IsVar) scope.Locals.TryAdd(statement.Variable.Lexeme, statement.Variable);
            base.VisitForIn(statement);
        }
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
        protected override void VisitStaticBlock(Stmt.StaticBlock statement) { }
    }

    private List<TypeInfo> EvaluateLoopBodyArguments(IReadOnlyList<Expr> arguments, LoopBodyEvaluation evaluation)
    {
        List<TypeInfo> types = [];
        foreach (var argument in arguments)
        {
            var value = EvaluateLoopBodyValue(argument is Expr.Spread spread ? spread.Expression : argument, evaluation);
            if (argument is not Expr.Spread) types.Add(value);
            else if (value is TypeInfo.Tuple tuple)
            {
                types.AddRange(tuple.Elements.Select(element => element.IsOptional
                    ? CreateUnion(element.Type, TypeInfo.Undefined.Shared) : element.Type));
                if (tuple.RestElementType is { } rest) types.Add(rest);
            }
            else types.Add(TryGetSpreadElementType(value, out var element) ? element : UnprovedLoopBodyType);
        }
        return types;
    }

    private TypeInfo EvaluateLoopBodyExplicitCall(TypeInfo callee, Expr.Call call, LoopBodyScope scope)
    {
        try { return Project(); }
        catch (TypeCheckException) { return UnprovedLoopBodyType; }

        TypeInfo Project()
        {
            List<TypeInfo> results = [];
            foreach (var type in LoopBodyAtoms(callee))
            {
                if (LoopCallableDomainSignature(type) is not TypeInfo.GenericFunction generic) return UnprovedLoopBodyType;
                var arguments = call.TypeArgs!.Select((text, index) => LoopBodyAnnotation(
                    call.TypeArgNodes is { } nodes && index < nodes.Count ? nodes[index] : null, text, scope)).ToArray();
                if (arguments.Any(argument => argument is null)) return UnprovedLoopBodyType;
                var resolved = ResolveTypeArgumentsWithDefaults(generic.TypeParams, arguments.OfType<TypeInfo>().ToList(), "function");
                var substitutions = generic.TypeParams.Select((parameter, index) => (parameter.Name, Type: resolved[index]))
                    .ToDictionary(pair => pair.Name, pair => pair.Type, StringComparer.Ordinal);
                results.Add(SubstitutePreservingSignatures(generic.ReturnType, substitutions));
            }
            return UnionLoopBodyValues(results);
        }
    }

    private TypeInfo? LoopBodyAnnotation(TypeNode? node, string? text, LoopBodyScope scope)
    {
        node ??= text is null ? null : Parser.TryParseTypeFragment(text, _cancellationToken);
        switch (node)
        {
            case NamedTypeNode { TypeArguments: null } named:
            {
                for (var local = scope; local is not null; local = local.Parent)
                    if (local.TypeNames.Contains(named.Name)) return null;
                var existing = scope.Environment.GetTypeBinding(named.Name);
                if (existing is TypeInfo.Class or TypeInfo.MutableClass) return new TypeInfo.Instance(existing);
                if (existing is not null) return existing;
                if (scope.Environment.GetTypeAlias(named.Name) is { } alias &&
                    _expandedTypeAliasCache?.GetValueOrDefault($"{named.Name}={alias.Definition}") is { } cached) return cached;
                return named.Name switch
                {
                    "string" => TypeInfo.String.Shared, "number" => TypeInfo.Primitive.Number,
                    "boolean" => TypeInfo.Primitive.Boolean, "bigint" => TypeInfo.BigInt.Shared,
                    "undefined" => TypeInfo.Undefined.Shared, "null" => TypeInfo.Null.Shared,
                    "any" => TypeInfo.Any.Shared, "unknown" => TypeInfo.Unknown.Shared,
                    "never" => TypeInfo.Never.Shared, "void" => TypeInfo.Void.Shared,
                    _ => null
                };
            }
            case LiteralTypeNode literal: return GetLiteralType(literal.Value);
            case ArrayTypeNode array when LoopBodyAnnotation(array.ElementType, null, scope) is { } element:
                return new TypeInfo.Array(element);
            case UnionTypeNode union:
            {
                var members = union.Members.Select(member => LoopBodyAnnotation(member, null, scope)).ToArray();
                return members.Any(member => member is null) ? null : UnionLoopBodyValues(members.OfType<TypeInfo>());
            }
            default: return null;
        }
    }
}
