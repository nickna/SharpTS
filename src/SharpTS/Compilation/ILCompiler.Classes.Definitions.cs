using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void DefineClassExpressionFactory(Expr.ClassExpr expression, TypeBuilder builder,
        ConstructorBuilder constructor, Type[] parameterTypes)
    {
        Type template = builder;
        ConstructorInfo target = constructor;
        if (builder.IsGenericTypeDefinition)
        {
            var erased = expression.TypeParams!.Select(parameter => parameter.Constraint is { } constraint
                ? ResolveConstraintType(constraint) : _types.Object).ToArray();
            template = EmitGenerics.MakeGenericType(builder, erased);
            target = EmitterTypeHelpers.ResolveConstructor(template, constructor);
        }
        var method = _programType.DefineMethod("$Create_" + _classExprs.Names[expression],
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.ObjectArray, _types.Object]);
        var il = method.GetILGenerator();
        if (HasRuntimeParent(expression))
        {
            var receiver = il.DeclareLocal(template);
            ConstructorInfo allocation = _classes.PrototypeConstructors[builder];
            if (template != builder) allocation = EmitterTypeHelpers.ResolveConstructor(template, allocation);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Newobj, allocation);
            il.Emit(OpCodes.Stloc, receiver);
            il.Emit(OpCodes.Ldloc, receiver);
            il.Emit(OpCodes.Ldarg_1);
            FieldInfo definitionField = _classExprs.DefinitionFields[expression];
            if (template != builder) definitionField = EmitterTypeHelpers.ResolveField(template, definitionField);
            il.Emit(OpCodes.Stfld, definitionField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, receiver);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _runtime.ClassDefinitions.InitializeReceiver);
            il.Emit(OpCodes.Ldloc, receiver);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            if (_functions.MethodsCapturingArguments.Contains(constructor))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stsfld, _runtime.Arguments.CurrentField);
            }
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                if (parameterTypes[i] == _types.ObjectArray)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    continue;
                }
                var missing = il.DefineLabel();
                var ready = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ble, missing);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Br, ready);
                il.MarkLabel(missing);
                il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
                il.MarkLabel(ready);
            }
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _runtime.ClassDefinitions.Type);
            il.Emit(OpCodes.Newobj, target);
            il.Emit(OpCodes.Ret);
        }
        string name = expression.Name?.Lexeme
            ?? _classExprs.VarToClassExpr.FirstOrDefault(entry => ReferenceEquals(entry.Value, expression)).Key
            ?? "";
        var initializer = _programType.DefineMethod("$Initialize_" + _classExprs.Names[expression],
            MethodAttributes.Assembly | MethodAttributes.Static, _types.Void, [_runtime.ClassDefinitions.Type]);
        _classExprs.Factories[expression] = new(method, template, name, GetClassConstructorLength(expression.Methods), initializer, []);
    }
}
