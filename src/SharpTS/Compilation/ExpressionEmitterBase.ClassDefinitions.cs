using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    protected bool TryEmitClassDefinitionSelf(string name)
    {
        if (!Ctx.IsInstanceMethod) return false;
        var expression = Ctx.CurrentClassExpr ?? Ctx.ClassExprBuilders?
            .FirstOrDefault(entry => ReferenceEquals(entry.Value, Ctx.CurrentClassBuilder)).Key;
        if (expression?.Name?.Lexeme != name) return false;
        EmitThis();
        EnsureBoxed();
        IL.Emit(OpCodes.Castclass, Ctx.Runtime!.ClassDefinitions.InstanceInterface);
        IL.Emit(OpCodes.Callvirt, Ctx.Runtime.ClassDefinitions.GetDefinition);
        SetStackUnknown();
        return true;
    }

    protected void EmitGuestClassDefinition(Expr.ClassExpr expression)
    {
        if (Ctx.ClassExprFactories?.TryGetValue(expression, out var factory) != true)
            throw new InvalidOperationException("Class expression factory has not been declared.");
        var parent = IL.DeclareLocal(Types.Object);
        if (expression.SuperclassExpr is { } heritage)
        {
            EmitClassHeritageValue(heritage, expression.Name?.Lexeme);
            EnsureBoxed();
        }
        else IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Stloc, parent);
        if (Ctx.ClassExprCaptureFields?.TryGetValue(expression, out var captures) == true)
        {
            foreach (var (name, field) in captures)
            {
                EmitVariable(new Expr.Variable(new Token(TokenType.IDENTIFIER, name, null, 0)));
                EnsureBoxed();
                IL.Emit(OpCodes.Stsfld, field);
            }
        }
        IL.Emit(OpCodes.Ldtoken, factory!.Template);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ClassInitialization.RunDefinition);
        if (Ctx.DeferredClassDefinitions?.TryGet(expression, out var deferred) == true)
            EmitDefinitionComputedKeys(deferred.Registrar, deferred.Keys);
        IL.Emit(OpCodes.Ldtoken, factory.Template);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Ldftn, factory.Method);
        IL.Emit(OpCodes.Newobj, Types.GetConstructor(typeof(Func<object[], object, object>), Types.Object, Types.IntPtr));
        IL.Emit(OpCodes.Ldstr, factory.Name);
        IL.Emit(OpCodes.Ldc_R8, (double)factory.Length);
        IL.Emit(OpCodes.Ldloc, parent);
        IL.Emit(OpCodes.Call, Ctx.Runtime.ClassDefinitions.Create);
        SetStackUnknown();
    }

    protected void EmitDefinitionComputedKeys(MethodBuilder method, IReadOnlyList<Expr> keys)
    {
        var values = new List<LocalBuilder>(keys.Count);
        foreach (var key in keys)
        {
            EmitExpression(key);
            EnsureBoxed();
            var isSymbol = IL.DefineLabel();
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Isinst, Ctx.Runtime!.Symbols.Type);
            IL.Emit(OpCodes.Brtrue, isSymbol);
            IL.Emit(OpCodes.Call, Ctx.Runtime.StringCoercion.ToJsString);
            IL.MarkLabel(isSymbol);
            values.Add(_helpers.SpillStoreObject());
        }
        IL.Emit(OpCodes.Ldc_I4, values.Count);
        IL.Emit(OpCodes.Newarr, Types.Object);
        for (int i = 0; i < values.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, values[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }
        IL.Emit(OpCodes.Call, method);
    }
}
