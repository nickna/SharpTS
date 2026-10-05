using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private (TypeBuilder Type, ConstructorBuilder Constructor, MethodBuilder Method) DefineDefinitionMethodValue(
        Expr.ClassExpr expression, MethodBuilder original, IReadOnlyList<Stmt.Parameter> parameters)
    {
        var type = EmitTypeDefinitions.DefineType(_moduleBuilder,
            $"$DefinitionMember_{_classExprs.Names[expression]}_{original.Name}", TypeAttributes.Public | TypeAttributes.Sealed, _types.Object);
        var field = type.DefineField("Definition", _runtime.ClassDefinitions.Type, FieldAttributes.Assembly | FieldAttributes.InitOnly);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_runtime.ClassDefinitions.Type]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, field);
        il.Emit(OpCodes.Ret);
        var method = type.DefineMethod("Invoke", MethodAttributes.Public, original.ReturnType,
            [_types.Object, .. parameters.Select(_ => _types.Object)]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        MarkExpectsThis(method);
        MarkNonConstructible(method);
        MarkPadsUndefined(method);
        MarkFunctionLength(method, parameters);
        MarkFunctionName(method, original.Name);
        _classExprs.DefinitionMethods.Add(method, (expression, field));

        Stmt.Function? source = expression.Methods.FirstOrDefault(source =>
            source.ComputedKey == null && source.Name.Lexeme == original.Name);
        source ??= _classes.ComputedMembers.GetMethods(_classExprs.Builders[expression])
            .FirstOrDefault(entry => ReferenceEquals(entry.Builder, original)).Method;
        if (source == null)
        {
            var accessor = _classes.ComputedMembers.GetAccessors(_classExprs.Builders[expression])
                .FirstOrDefault(entry => ReferenceEquals(entry.Method, original)).Accessor;
            accessor ??= (expression.Accessors ?? []).First(accessor =>
                original.Name == $"{(accessor.Kind.Type == TokenType.GET ? "get" : "set")}_{NamingConventions.ToPascalCase(accessor.Name.Lexeme)}");
            source = new Stmt.Function(accessor.Name, null, null, parameters.ToList(), accessor.Body, null);
        }
        var guestThis = new Stmt.Parameter(new Token(TokenType.IDENTIFIER, "__this", null, 0), "any");
        var lowered = source with { Parameters = [guestThis, .. source.Parameters] };
        if (_asyncMethodFunctionDCKeys.TryGetValue(source, out var asyncKey)) _asyncMethodFunctionDCKeys[lowered] = asyncKey;
        if (_generatorMethodFunctionDCKeys.TryGetValue(source, out var generatorKey)) _generatorMethodFunctionDCKeys[lowered] = generatorKey;
        if (_asyncGeneratorMethodFunctionDCKeys.TryGetValue(source, out var asyncGeneratorKey)) _asyncGeneratorMethodFunctionDCKeys[lowered] = asyncGeneratorKey;
        if (source.IsAsync && source.IsGenerator) EmitAsyncGeneratorMethodBody(method, lowered, null, currentClassName: _classExprs.Names[expression]);
        else if (source.IsAsync) EmitAsyncMethodBody(method, lowered, null, currentClassName: _classExprs.Names[expression]);
        else if (source.IsGenerator) EmitGeneratorMethodBody(method, lowered, null, currentClassName: _classExprs.Names[expression]);
        else
        {
            il = method.GetILGenerator();
            var ctx = CreateClassExpressionContext(il, expression, _classExprs.Builders[expression], null, method);
            ctx.EmittingTypeBuilder = type;
            ctx.IsInstanceMethod = true;
            ctx.ClassDefinitionOwnerField = field;
            ctx.GuestThisVariableName = "__this";
            foreach (var parameter in expression.TypeParams ?? []) ctx.GenericTypeParameters[parameter.Name.Lexeme] = _types.Object;
            ctx.DefineParameter("__this", 1, _types.Object);
            SetupSyncMethodFunctionDisplayClass(ctx, il, source);
            for (int i = 0; i < parameters.Count; i++) ctx.DefineParameter(parameters[i].Name.Lexeme, i + 2, _types.Object);
            var emitter = new ILEmitter(ctx);
            EmitFunctionEnvironmentPrologue(il, ctx, emitter, parameters.ToList(), source.Body,
                parameters.Select(_ => _types.Object).ToArray(), argumentOffset: 2);
            InitializeSyncMethodCapturedParameters(ctx, il, source, method, argumentOffset: 2);
            foreach (var statement in source.Body ?? []) emitter.EmitStatement(statement);
            if (emitter.HasDeferredReturns) emitter.FinalizeReturns();
            else { EmitDefaultReturnValue(il, method.ReturnType); il.Emit(OpCodes.Ret); }
        }
        type.CreateType();
        return (type, constructor, method);
    }

}
