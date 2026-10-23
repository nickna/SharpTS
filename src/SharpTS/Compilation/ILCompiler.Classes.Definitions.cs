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
        ConstructorInfo target = NewTargetConstructor(constructor);
        if (builder.IsGenericTypeDefinition)
        {
            var erased = expression.TypeParams!.Select(parameter => parameter.Constraint is { } constraint
                ? ResolveConstraintType(constraint) : _types.Object).ToArray();
            template = EmitGenerics.MakeGenericType(builder, erased);
            target = EmitterTypeHelpers.ResolveConstructor(template, NewTargetConstructor(constructor));
        }
        var method = _programType.DefineMethod("$Create_" + _classExprs.Names[expression],
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.ObjectArray, _types.Object, _types.Object]);
        var il = method.GetILGenerator();
        var sourceParameters = ResolveClassExpressionConstructorSourceParameters(expression);
        var emitter = new ILEmitter(CreateModuleMemberContext(il, method));
        if (UsesLiveSuperConstruction(ClassExpressionDeclaration(expression)) && !builder.IsGenericTypeDefinition
            && !_classes.ErrorSubclasses.Contains(_classExprs.Names[expression])
            && !_classes.PromiseSubclasses.Contains(_classExprs.Names[expression]))
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, _runtime.ClassDefinitions.InitializeReceiver);
            il.Emit(OpCodes.Ret);
        }
        else if (HasRuntimeParent(expression))
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
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, _runtime.ClassDefinitions.InitializeReceiver);
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
                var slot = ResolveInheritedConstructorSlot(parameterTypes[i], template);
                if (slot == _types.ObjectArray)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    continue;
                }
                if (sourceParameters?.ElementAtOrDefault(i)?.IsRest == true)
                {
                    // Ordinary expression constructors use object slots, while
                    // declaration adapters retain List<object> rest markers.
                    // Source rest parameters receive the whole tail as a fresh
                    // guest Array through either CLR representation.
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Call, _types.GetMethod(typeof(Math), "Min", _types.Int32, _types.Int32));
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Sub); il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Call, _types.GetMethod(typeof(Math), "Max", _types.Int32, _types.Int32));
                    il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "GetRange", _types.Int32, _types.Int32));
                    il.Emit(OpCodes.Newobj, _runtime.ArrayStorage.Ctor);
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
                if (slot.IsValueType || slot.IsGenericParameter)
                {
                    var supplied = il.DefineLabel();
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Brtrue, supplied);
                    il.Emit(OpCodes.Pop);
                    EmitConstructorFactoryDefault(il, emitter, slot);
                    il.Emit(OpCodes.Br, ready);
                    il.MarkLabel(supplied);
                }
                if (slot == _types.Double) EmitConstructorFactoryDoubleConversion(il);
                else if (slot != _types.Object)
                    il.Emit(slot.IsValueType || slot.IsGenericParameter ? OpCodes.Unbox_Any : OpCodes.Castclass, slot);
                il.Emit(OpCodes.Br, ready);
                il.MarkLabel(missing);
                EmitConstructorFactoryDefault(il, emitter, slot);
                il.MarkLabel(ready);
            }
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _runtime.ClassDefinitions.Type);
            if (NewTargetConstructor(constructor) != constructor) il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Newobj, target);
            il.Emit(OpCodes.Call, _runtime.DynamicConstruction.ResolveClassReturn);
            il.Emit(OpCodes.Ret);
        }
        string name = expression.Name?.Lexeme
            ?? _classExprs.InferredNames.GetValueOrDefault(expression)
            ?? _classExprs.VarToClassExpr.FirstOrDefault(entry => ReferenceEquals(entry.Value, expression)).Key
            ?? "";
        var initializer = _programType.DefineMethod("$Initialize_" + _classExprs.Names[expression],
            MethodAttributes.Assembly | MethodAttributes.Static, _types.Void, [_runtime.ClassDefinitions.Type]);
        _classExprs.Factories[expression] = new(method, template, name, GetClassConstructorLength(expression.Methods), initializer, []);
    }

    private IReadOnlyList<Stmt.Parameter>? ResolveClassExpressionConstructorSourceParameters(Expr.ClassExpr expression)
    {
        var visited = new HashSet<Expr.ClassExpr>(ReferenceEqualityComparer.Instance);
        while (visited.Add(expression))
        {
            var constructor = expression.Methods.FirstOrDefault(method =>
                method.Name.Lexeme == "constructor" && !method.IsStatic && method.Body != null);
            if (constructor != null) return constructor.Parameters;
            var parentType = _classExprs.Builders[expression].BaseType;
            if (parentType?.IsConstructedGenericType == true) parentType = parentType.GetGenericTypeDefinition();
            var parentExpression = _classExprs.Builders.FirstOrDefault(entry => ReferenceEquals(entry.Value, parentType)).Key;
            if (parentExpression != null) { expression = parentExpression; continue; }
            var declaration = _classes.Declarations.FirstOrDefault(candidate =>
                _classes.DeclarationNames.TryGetValue(candidate, out var name)
                && _classes.Builders.TryGetValue(name, out var builder) && ReferenceEquals(builder, parentType));
            return declaration != null && parentType is TypeBuilder parentBuilder
                ? ResolveConstructorFactorySourceParameters(parentBuilder, declaration) : null;
        }
        return null;
    }
}
