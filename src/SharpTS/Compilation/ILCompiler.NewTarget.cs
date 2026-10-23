using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private readonly Dictionary<MethodBuilder, MethodBuilder> _newTargetBodies = new(ReferenceEqualityComparer.Instance);

    private void DefineNewTargetBody(MethodBuilder entry, IReadOnlyList<TypeParam>? sourceTypeParameters = null)
    {
        if (_features?.UsesNewTarget != true || _newTargetBodies.ContainsKey(entry)) return;
        var owner = (TypeBuilder)entry.DeclaringType!;
        var sourceParameters = entry.GetParameters();
        var body = owner.DefineMethod($"<>NewTargetBody{_newTargetBodies.Count}",
            MethodAttributes.Assembly | (entry.IsStatic ? MethodAttributes.Static : 0));
        var genericParameters = entry.GetGenericArguments() ?? [];
        Type[] bodyGenericParameters = [];
        if (genericParameters.Length > 0)
        {
            var parameters = body.DefineGenericParameters(genericParameters.Select(parameter => parameter.Name).ToArray());
            bodyGenericParameters = parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                parameters[i].SetGenericParameterAttributes(genericParameters[i].GenericParameterAttributes);
                var sourceParameter = sourceTypeParameters?.FirstOrDefault(parameter =>
                    parameter.Name.Lexeme == genericParameters[i].Name);
                if (sourceParameter?.Constraint is { } constraint)
                {
                    var type = ResolveConstraintType(constraint);
                    if (type.IsInterface) parameters[i].SetInterfaceConstraints(type);
                    else EmitTypeDefinitions.SetBaseTypeConstraint(parameters[i], type);
                }
            }
            _functions.GenericParameters.Declare(body, parameters);
        }
        else _functions.GenericParameters.Declare(body, []);
        Type Substitute(Type type)
        {
            int index = Array.IndexOf(genericParameters, type);
            if (index >= 0) return bodyGenericParameters[index];
            if (type.IsArray) return _types.MakeArrayType(Substitute(type.GetElementType()!));
            if (type.IsGenericType) return EmitGenerics.MakeGenericType(type.GetGenericTypeDefinition(), type.GetGenericArguments().Select(Substitute).ToArray());
            return type;
        }
        body.SetReturnType(Substitute(entry.ReturnType));
        body.SetParameters([.. sourceParameters.Select(parameter => Substitute(parameter.ParameterType)), _types.Object]);
        for (int i = 0; i < sourceParameters.Length; i++)
            body.DefineParameter(i + 1, ParameterAttributes.None, sourceParameters[i].Name);
        body.DefineParameter(sourceParameters.Length + 1, ParameterAttributes.None, "__newTarget");
        entry.SetCustomAttribute(_runtime.FunctionAttributes.NewTargetCoreCtor,
            CustomAttributeEncoder.Encode(_runtime.FunctionAttributes.NewTargetCoreCtor, body.Name));
        _newTargetBodies.Add(entry, body);
        var il = entry.GetILGenerator();
        int first = entry.IsStatic ? 0 : 1;
        if (!entry.IsStatic) il.Emit(OpCodes.Ldarg_0);
        for (int i = 0; i < sourceParameters.Length; i++) il.Emit(OpCodes.Ldarg, first + i);
        il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
        MethodInfo target = genericParameters.Length > 0
            ? EmitGenerics.MakeGenericMethod(body, genericParameters) : body;
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder NewTargetBody(MethodBuilder entry) => _newTargetBodies.GetValueOrDefault(entry) ?? entry;

    private void ConfigureNewTargetBody(CompilationContext context, MethodBuilder method)
    {
        if (_newTargetBodies.Values.Contains(method))
            context.NewTargetParameterIndex = method.GetParameters().Length - (method.IsStatic ? 1 : 0);
    }
    private readonly Dictionary<ConstructorBuilder, ConstructorBuilder> _newTargetConstructors =
        new(ReferenceEqualityComparer.Instance);

    private ConstructorBuilder DefineNewTargetConstructor(ConstructorBuilder entry, bool expression = false)
    {
        // Reflect.construct supplies a distinct allocation constructor even
        // when the source never reads the new.target meta-property.
        if (_features?.UsesNewTarget != true && _features?.UsesReflect != true
            && _features?.UsesClassPrototypeMutation != true) return entry;
        if (_newTargetConstructors.TryGetValue(entry, out var existing)) return existing;
        var owner = (TypeBuilder)entry.DeclaringType!;
        var parameters = entry.GetParameters();
        var body = owner.DefineConstructor(MethodAttributes.Assembly, CallingConventions.Standard,
            [.. parameters.Select(parameter => parameter.ParameterType), _types.Object]);
        _newTargetConstructors.Add(entry, body);
        var il = entry.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, i + 1);
        if (expression) il.Emit(OpCodes.Ldarg, parameters.Length);
        else
        {
            il.Emit(OpCodes.Ldtoken, owner);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        }
        ConstructorInfo target = owner.IsGenericTypeDefinition
            ? EmitterTypeHelpers.ResolveConstructor(EmitGenerics.MakeGenericType(owner, owner.GetGenericArguments()), body)
            : body;
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
        return body;
    }

    private ConstructorBuilder NewTargetConstructor(ConstructorBuilder entry) =>
        _newTargetConstructors.GetValueOrDefault(entry) ?? entry;

    private ConstructorInfo NewTargetConstructor(ConstructorInfo entry) =>
        entry is ConstructorBuilder builder ? NewTargetConstructor(builder) : entry;

    private void EmitForwardedNewTarget(CompilationContext context)
    {
        if (context.NewTargetParameterIndex is { } index) context.IL.Emit(OpCodes.Ldarg, index);
        else context.IL.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
    }

    private void EmitConstructedClassPrototype(CompilationContext context)
    {
        if ((_features?.UsesReflect != true && _features?.UsesClassPrototypeMutation != true)
            || context.NewTargetParameterIndex is not { } argument)
            return;

        var il = context.IL;
        var prototype = il.DeclareLocal(_types.Object);
        var kind = il.DeclareLocal(_types.String);
        var fallback = il.DefineLabel();
        var install = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, argument);
        il.Emit(OpCodes.Isinst, _runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brtrue, done);
        il.Emit(OpCodes.Ldarg, argument);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, _runtime.ObjectRead.Property);
        il.Emit(OpCodes.Stloc, prototype);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Brfalse, fallback);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Call, _runtime.Operators.TypeOf);
        il.Emit(OpCodes.Stloc, kind);
        foreach (var name in new[] { "object", "function" })
        {
            il.Emit(OpCodes.Ldloc, kind);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brtrue, install);
        }
        il.MarkLabel(fallback);
        il.Emit(OpCodes.Call, _runtime.ObjectPrototypes.Populate);
        il.Emit(OpCodes.Ldsfld, _runtime.ObjectPrototypes.Prototype);
        il.Emit(OpCodes.Stloc, prototype);
        il.MarkLabel(install);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Call, _runtime.DescriptorStorage.SetPrototype);
        il.MarkLabel(done);
    }

    private static bool ExpressionContainsLexicalNewTarget(Expr expression)
    {
        var visitor = new LexicalNewTargetPresenceVisitor();
        visitor.Visit(expression);
        return visitor.Found;
    }

    private sealed class LexicalNewTargetPresenceVisitor : Parsing.Visitors.AstVisitorBase
    {
        public bool Found { get; private set; }
        protected override void VisitNewTarget(Expr.NewTarget expression)
        {
            Found = true;
            ShouldContinue = false;
        }
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            if (!expression.HasOwnThis) base.VisitArrowFunction(expression);
        }
        protected override void VisitClass(Stmt.Class statement)
        {
            foreach (var expression in ClassDefinitionExpressions.Enumerate(statement)) Visit(expression);
        }
        protected override void VisitClassExpr(Expr.ClassExpr expression)
        {
            foreach (var value in ClassDefinitionExpressions.Enumerate(expression)) Visit(value);
        }
    }

}
