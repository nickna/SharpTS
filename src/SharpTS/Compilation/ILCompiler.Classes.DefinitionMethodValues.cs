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
            $"$DefinitionMember_{_classExprs.Names[expression]}_{original.Name}_{(original.IsStatic ? "Static" : "Instance")}", TypeAttributes.Public | TypeAttributes.Sealed, _types.Object);
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
            [_types.Object, .. parameters.Select(parameter => parameter.IsRest ? _types.ListOfObject : _types.Object)]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        MarkExpectsThis(method);
        MarkNonConstructible(method);
        MarkPadsUndefined(method);
        MarkFunctionLength(method, parameters);
        MarkFunctionName(method, original.Name);
        _classExprs.DefinitionMethods.Add(method, (expression, field));

        Stmt.Function? source = expression.Methods.FirstOrDefault(source =>
            source.ComputedKey == null && source.IsStatic == original.IsStatic && source.Name.Lexeme == original.Name);
        source ??= _classes.ComputedMembers.GetMethods(_classExprs.Builders[expression])
            .FirstOrDefault(entry => ReferenceEquals(entry.Builder, original)).Method;
        if (source == null)
        {
            var accessor = _classes.ComputedMembers.GetAccessors(_classExprs.Builders[expression])
                .FirstOrDefault(entry => ReferenceEquals(entry.Method, original)).Accessor;
            accessor ??= (expression.Accessors ?? []).First(accessor =>
                original.Name == $"{(accessor.Kind.Type == TokenType.GET ? "get" : "set")}_{NamingConventions.ToPascalCase(accessor.Name.Lexeme)}");
            source = new Stmt.Function(accessor.Name, null, null, parameters.ToList(), accessor.Body, null, IsStatic: accessor.IsStatic);
        }
        EmitGuestMethodValueBody(method, source, () =>
        {
            var ctx = CreateClassExpressionContext(method.GetILGenerator(), expression, _classExprs.Builders[expression], null, method);
            ctx.EmittingTypeBuilder = type;
            ctx.IsInstanceMethod = !source.IsStatic;
            ctx.ClassDefinitionOwnerField = field;
            ctx.GuestThisVariableName = "__this";
            ctx.HasGuestReceiver = true;
            foreach (var parameter in expression.TypeParams ?? []) ctx.GenericTypeParameters[parameter.Name.Lexeme] = _types.Object;
            return ctx;
        }, _classExprs.Names[expression]);
        type.CreateType();
        return (type, constructor, method);
    }

}
