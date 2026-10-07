using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void EmitClassDefinitionInitializer(Expr.ClassExpr expression, TypeBuilder template)
    {
        var factory = _classExprs.Factories[expression];
        var il = factory.Initializer.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, expression, template, null, factory.Initializer);
        ctx.EmittingTypeBuilder = _programType;
        ctx.ClassDefinitionParameterIndex = 0;
        ctx.ClassDefinitionThisParameterIndex = 0;
        var emitter = new ILEmitter(ctx);
        var storage = _runtime.DescriptorStorage;
        foreach (var value in _classes.PrivateElements.Require(_classExprs.Names[expression]).MethodValues.Values)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _runtime.ClassDefinitions.PrivateMembers);
            il.Emit(OpCodes.Ldstr, $"method:{value.Source.Name.Lexeme.TrimStart('#')}");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, value.Constructor);
            EmitMethodInfoLiteral(il, value.Method, value.Type);
            il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor);
            il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
        }

        var members = new List<(int Position, bool Static, Expr Key, MethodBuilder Method, bool? Getter, IReadOnlyList<Stmt.Parameter> Parameters)>();
        foreach (var method in expression.Methods.Where(method => method.Body != null &&
                     !method.IsPrivate && method.Name.Lexeme != "constructor" && method.ComputedKey == null))
        {
            var builder = method.IsStatic ? _classExprs.StaticMethods[expression][method.Name.Lexeme]
                : _classExprs.InstanceMethods[expression][method.Name.Lexeme];
            members.Add((method.Name.Start, method.IsStatic, new Expr.Literal(method.Name.Lexeme), builder, null, method.Parameters));
        }
        foreach (var (method, key, builder) in _classes.ComputedMembers.GetMethods(template))
            members.Add((method.Name.Start, method.IsStatic, key, builder, null, method.Parameters));
        foreach (var (accessor, builder) in _classes.ComputedMembers.GetAccessors(template))
            members.Add((accessor.Name.Start, accessor.IsStatic, accessor.ComputedKey!, builder, accessor.Kind.Type == TokenType.GET,
                accessor.SetterParam == null ? [] : [accessor.SetterParam]));
        foreach (var accessor in (expression.Accessors ?? []).Where(accessor => !accessor.IsStatic && accessor.ComputedKey == null))
        {
            var name = NamingConventions.ToPascalCase(accessor.Name.Lexeme);
            var builder = accessor.Kind.Type == TokenType.GET ? _classExprs.Getters[expression][name] : _classExprs.Setters[expression][name];
            members.Add((accessor.Name.Start, false, new Expr.Literal(accessor.Name.Lexeme), builder, accessor.Kind.Type == TokenType.GET,
                accessor.SetterParam == null ? [] : [accessor.SetterParam]));
        }
        foreach (var member in members.OrderBy(member => member.Position))
            Register(member.Static, member.Key, member.Method, member.Getter, member.Parameters);

        foreach (var initializer in expression.StaticInitializers ?? expression.Fields.Where(field => field.IsStatic).Cast<Stmt>().ToList())
        {
            if (initializer is Stmt.Field field && field.IsStatic && !field.IsDeclare)
            {
                if (field.IsPrivate)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, _runtime.ClassDefinitions.PrivateMembers);
                    il.Emit(OpCodes.Ldstr, $"field:{field.Name.Lexeme.TrimStart('#')}");
                    if (field.Initializer != null) { emitter.EmitExpression(field.Initializer); emitter.EmitBoxIfNeeded(field.Initializer); }
                    else il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
                    il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
                    continue;
                }
                il.Emit(OpCodes.Ldarg_0);
                EmitKey(field.ComputedKey ?? new Expr.Literal(field.Name.Lexeme));
                if (field.Initializer != null)
                {
                    emitter.EmitExpression(field.Initializer);
                    emitter.EmitBoxIfNeeded(field.Initializer);
                }
                else il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
                var value = il.DeclareLocal(_types.Object);
                il.Emit(OpCodes.Stloc, value);
                var key = il.DeclareLocal(_types.Object);
                il.Emit(OpCodes.Stloc, key);
                var owner = il.DeclareLocal(_types.Object);
                il.Emit(OpCodes.Stloc, owner);
                var descriptor = NewDescriptor();
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Callvirt, storage.DescriptorValue.GetSetMethod()!);
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Callvirt, storage.DescriptorEnumerable.GetSetMethod()!);
                StoreDescriptor(owner, key, descriptor);
            }
            else if (initializer is Stmt.StaticBlock block)
                foreach (var statement in block.Body) emitter.EmitStatement(statement);
        }
        il.Emit(OpCodes.Ret);

        void EmitKey(Expr key)
        {
            int index = Enumerable.Range(0, factory.Keys.Count).FirstOrDefault(index => ReferenceEquals(factory.Keys[index], key), -1);
            if (index >= 0)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, _runtime.ClassDefinitions.Keys);
                il.Emit(OpCodes.Ldc_I4, index);
                il.Emit(OpCodes.Ldelem_Ref);
            }
            else if (key is Expr.Literal { Value: string name }) il.Emit(OpCodes.Ldstr, name);
            else throw new InvalidOperationException("A computed definition key was not declared.");
        }

        LocalBuilder NewDescriptor()
        {
            var result = il.DeclareLocal(storage.DescriptorType);
            il.Emit(OpCodes.Newobj, storage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, result);
            foreach (var property in new[] { storage.DescriptorWritable, storage.DescriptorConfigurable })
            {
                il.Emit(OpCodes.Ldloc, result);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Callvirt, property.GetSetMethod()!);
            }
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, storage.DescriptorEnumerable.GetSetMethod()!);
            return result;
        }

        void StoreDescriptor(LocalBuilder owner, LocalBuilder key, LocalBuilder descriptor)
        {
            var stringKey = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brtrue, stringKey);
            il.Emit(OpCodes.Ldloc, owner);
            il.Emit(OpCodes.Call, _runtime.Symbols.GetStorage);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(stringKey);
            il.Emit(OpCodes.Ldloc, owner);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Call, storage.DefineProperty);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(done);
        }

        void Register(bool isStatic, Expr keyExpression, MethodBuilder method, bool? getter, IReadOnlyList<Stmt.Parameter> parameters)
        {
            var owner = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            if (!isStatic) il.Emit(OpCodes.Ldfld, _runtime.ClassDefinitions.Prototype);
            il.Emit(OpCodes.Stloc, owner);
            var key = il.DeclareLocal(_types.Object);
            EmitKey(keyExpression);
            il.Emit(OpCodes.Stloc, key);
            var descriptor = NewDescriptor();
            if (getter.HasValue)
            {
                // Preserve the other half when getter and setter share a key.
                var noExisting = il.DefineLabel();
                var haveExisting = il.DefineLabel();
                var existing = il.DeclareLocal(storage.DescriptorType);
                il.Emit(OpCodes.Ldloc, key);
                il.Emit(OpCodes.Isinst, _types.String);
                il.Emit(OpCodes.Brfalse, noExisting);
                il.Emit(OpCodes.Ldloc, owner);
                il.Emit(OpCodes.Ldloc, key);
                il.Emit(OpCodes.Castclass, _types.String);
                il.Emit(OpCodes.Call, storage.GetPropertyDescriptor);
                il.Emit(OpCodes.Stloc, existing);
                il.Emit(OpCodes.Br, haveExisting);
                il.MarkLabel(noExisting);
                il.Emit(OpCodes.Ldloc, owner);
                il.Emit(OpCodes.Call, _runtime.Symbols.GetStorage);
                il.Emit(OpCodes.Ldloc, key);
                var found = il.DeclareLocal(_types.Object);
                il.Emit(OpCodes.Ldloca, found);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldloc, found);
                il.Emit(OpCodes.Isinst, storage.DescriptorType);
                il.Emit(OpCodes.Stloc, existing);
                il.MarkLabel(haveExisting);
                var fresh = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, existing);
                il.Emit(OpCodes.Brfalse, fresh);
                il.Emit(OpCodes.Ldloc, existing);
                il.Emit(OpCodes.Stloc, descriptor);
                il.MarkLabel(fresh);
            }
            il.Emit(OpCodes.Ldloc, descriptor);
            if (isStatic)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldtoken, factory.Template.IsGenericType ? EmitterTypeHelpers.ResolveMethod(factory.Template, method) : method);
                il.Emit(OpCodes.Ldtoken, factory.Template);
                il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
                il.Emit(OpCodes.Castclass, _types.MethodInfo);
            }
            else
            {
                var value = DefineDefinitionMethodValue(expression, method, parameters);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Newobj, value.Constructor);
                EmitMethodInfoLiteral(il, value.Method, value.Type);
            }
            il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor);
            il.Emit(OpCodes.Callvirt, (getter == true ? storage.DescriptorGetter : getter == false ? storage.DescriptorSetter : storage.DescriptorValue).GetSetMethod()!);
            StoreDescriptor(owner, key, descriptor);
        }
    }

}
