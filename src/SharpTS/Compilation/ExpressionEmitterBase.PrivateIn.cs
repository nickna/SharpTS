using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    protected virtual void EmitPrivateIn(Expr.PrivateIn expression)
    {
        string name = expression.Name.Lexeme.TrimStart('#');
        string current = Ctx.CurrentClassName ?? throw new InvalidOperationException("Private name has no lexical owner.");
        string owner = Ctx.TypeMap?.GetPrivateInOwner(expression.Name) is { } checkedOwner
            ? Ctx.ResolveClassName(checkedOwner, checkedOwner.Name)
            : new[] { current }.Concat(Ctx.EnclosingClassNames ?? [])
                .First(className => Ctx.ClassRegistry?.GetPrivateElements(className) is { } elements
                    && (elements.FieldNames.Contains(name) || elements.StaticFields.ContainsKey(name)
                        || elements.Methods.ContainsKey(name) || elements.StaticMethods.ContainsKey(name)));
        var declaration = Ctx.ClassRegistry!.GetPrivateElements(owner)!;
        var receiver = SpillGenericPrivateOperand(expression.Object);
        var invalid = IL.DefineLabel();
        var absent = IL.DefineLabel();
        var done = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Brfalse, invalid);
        foreach (var primitive in new Type[] { Types.String, Types.Double, Types.Boolean, Types.BigInteger,
                     Ctx.Runtime!.Sentinels.UndefinedType, Ctx.Runtime.Symbols.Type })
        {
            IL.Emit(OpCodes.Ldloc, receiver);
            IL.Emit(OpCodes.Isinst, primitive);
            IL.Emit(OpCodes.Brtrue, invalid);
        }

        bool isStatic = declaration.StaticFields.ContainsKey(name) || declaration.StaticMethods.ContainsKey(name);
        if (TryEmitPrivateInOwnerDefinition(owner))
        {
            var definition = IL.DeclareLocal(Ctx.Runtime!.ClassDefinitions.Type);
            IL.Emit(OpCodes.Stloc, definition);
            if (isStatic) IL.Emit(OpCodes.Ldloc, receiver);
            else
            {
                var ownerExpression = Ctx.ClassExprBuilders!.First(entry => entry.Value.FullName == owner).Key;
                var template = Ctx.ClassExprFactories![ownerExpression].Template;
                var field = Ctx.ClassExprDefinitionFields![ownerExpression];
                var reference = template.IsGenericType ? EmitterTypeHelpers.ResolveField(template, field) : field;
                IL.Emit(OpCodes.Ldloc, receiver);
                IL.Emit(OpCodes.Isinst, reference.DeclaringType!);
                IL.Emit(OpCodes.Brfalse, absent);
                IL.Emit(OpCodes.Ldloc, receiver);
                IL.Emit(OpCodes.Castclass, reference.DeclaringType!);
                IL.Emit(OpCodes.Ldfld, reference);
            }
            IL.Emit(OpCodes.Ldloc, definition);
            IL.Emit(OpCodes.Ceq);
            IL.Emit(OpCodes.Brfalse, absent);
            if (isStatic)
            {
                if (declaration.StaticMethods.ContainsKey(name)) IL.Emit(OpCodes.Ldc_I4_1);
                else
                {
                    IL.Emit(OpCodes.Ldloc, definition);
                    IL.Emit(OpCodes.Ldfld, Ctx.Runtime.ClassDefinitions.PrivateMembers);
                    IL.Emit(OpCodes.Ldstr, $"field:{name}");
                    IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "ContainsKey", Types.String));
                }
                IL.Emit(OpCodes.Br, done);
            }
            else EmitInstancePresence();
        }
        else if (isStatic)
        {
            Ctx.ClassRegistry.TryGetClass(owner, out var builder);
            if (builder!.IsGenericTypeDefinition)
            {
                var type = IL.DeclareLocal(Types.Type);
                IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Isinst, Types.Type); IL.Emit(OpCodes.Stloc, type);
                IL.Emit(OpCodes.Ldloc, type); IL.Emit(OpCodes.Brfalse, absent);
                IL.Emit(OpCodes.Ldloc, type); IL.Emit(OpCodes.Callvirt, Types.GetProperty(Types.Type, "IsGenericType").GetGetMethod()!);
                IL.Emit(OpCodes.Brfalse, absent);
                IL.Emit(OpCodes.Ldloc, type); IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.Type, "GetGenericTypeDefinition"));
            }
            else IL.Emit(OpCodes.Ldloc, receiver);
            IL.Emit(OpCodes.Ldtoken, builder); IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
            IL.Emit(OpCodes.Ceq); IL.Emit(OpCodes.Brfalse, absent);
            if (declaration.StaticMethods.ContainsKey(name)) IL.Emit(OpCodes.Ldc_I4_1);
            else
            {
                Ctx.ClassRegistry.TryGetCallableStaticPrivateField(owner, name, out var ownerField);
                var presence = ownerField!.DeclaringType!.IsConstructedGenericType
                    ? EmitterTypeHelpers.ResolveField(ownerField.DeclaringType, declaration.StaticPresence!)
                    : declaration.StaticPresence!;
                IL.Emit(OpCodes.Ldsfld, presence);
                IL.Emit(OpCodes.Ldstr, name);
                IL.Emit(OpCodes.Callvirt, Types.GetMethod(typeof(HashSet<string>), "Contains", Types.String));
            }
            IL.Emit(OpCodes.Br, done);
        }
        else EmitInstancePresence();

        IL.MarkLabel(invalid);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, "Right-hand side of private 'in' is not an object");
        IL.MarkLabel(absent); IL.Emit(OpCodes.Ldc_I4_0);
        IL.MarkLabel(done);
        SetStackType(StackType.Boolean);

        void EmitInstancePresence()
        {
            var slots = IL.DeclareLocal(Types.DictionaryStringObject);
            if (declaration.InstanceBridge is { } bridge)
            {
                IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Isinst, bridge.InterfaceType); IL.Emit(OpCodes.Brfalse, absent);
                IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Castclass, bridge.InterfaceType);
                IL.Emit(OpCodes.Callvirt, bridge.StorageGetter);
            }
            else IL.Emit(OpCodes.Ldsfld, declaration.Storage!);
            IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Ldloca, slots);
            IL.Emit(OpCodes.Callvirt, Types.GetMethod(declaration.Storage!.FieldType, "TryGetValue", Types.Object, Types.DictionaryStringObject.MakeByRefType()));
            IL.Emit(OpCodes.Brfalse, absent);
            if (declaration.Methods.ContainsKey(name)) IL.Emit(OpCodes.Ldc_I4_1);
            else
            {
                IL.Emit(OpCodes.Ldloc, slots); IL.Emit(OpCodes.Ldstr, name);
                IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "ContainsKey", Types.String));
            }
            IL.Emit(OpCodes.Br, done);
        }

    }

    private bool TryEmitPrivateInOwnerDefinition(string owner)
    {
        var ownerExpression = Ctx.ClassExprBuilders?.FirstOrDefault(entry => entry.Value.FullName == owner).Key;
        if (ownerExpression is null)
            return false;
        var ownerBuilder = Ctx.ClassExprBuilders![ownerExpression];
        if (!TryEmitOwnedClassDefinition())
            throw new InvalidOperationException("Private name has no class evaluation owner.");
        var expression = Ctx.CurrentClassExpr ?? Ctx.ClassExprBuilders!
            .First(entry => ReferenceEquals(entry.Value, Ctx.CurrentClassBuilder)).Key;
        while (!ReferenceEquals(Ctx.ClassExprBuilders![expression], ownerBuilder))
        {
            int slot = Ctx.ClassExprCaptureSlots![expression][CompilationContext.PrivateOwnerCaptureName];
            IL.Emit(OpCodes.Ldfld, Ctx.Runtime!.ClassDefinitions.Captures);
            IL.Emit(OpCodes.Ldc_I4, slot);
            IL.Emit(OpCodes.Ldelem_Ref);
            IL.Emit(OpCodes.Castclass, Ctx.Runtime.ClassDefinitions.Type);
            string parent = Ctx.ClassExprEnclosingClasses![expression];
            expression = Ctx.ClassExprBuilders.First(entry => entry.Value.FullName == parent).Key;
        }
        return true;
    }
}
