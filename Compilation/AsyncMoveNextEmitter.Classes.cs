using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    private void EmitNew(Expr.New n)
    {
        // Resolve class name (may be qualified in multi-module compilation)
        string resolvedClassName = _ctx!.ResolveClassName(n.ClassName.Lexeme);

        if (_ctx.Classes.TryGetValue(resolvedClassName, out var typeBuilder) &&
            _ctx.ClassConstructors != null &&
            _ctx.ClassConstructors.TryGetValue(resolvedClassName, out var ctorBuilder))
        {
            Type targetType = typeBuilder;
            ConstructorInfo targetCtor = ctorBuilder;

            // Handle generic class instantiation (e.g., new Box<number>(42))
            if (n.TypeArgs != null && n.TypeArgs.Count > 0 &&
                _ctx.ClassGenericParams?.TryGetValue(resolvedClassName, out var _) == true)
            {
                // Resolve type arguments
                Type[] typeArgs = n.TypeArgs.Select(ResolveTypeArg).ToArray();

                // Create the constructed generic type
                targetType = typeBuilder.MakeGenericType(typeArgs);

                // Get the constructor on the constructed type
                targetCtor = TypeBuilder.GetConstructor(targetType, ctorBuilder);
            }

            // Get expected parameter count from constructor definition
            int expectedParamCount = ctorBuilder.GetParameters().Length;

            // IMPORTANT: In async, await can happen in arguments
            // Emit all arguments first and store to temps
            var argTemps = new List<LocalBuilder>();
            foreach (var arg in n.Arguments)
            {
                EmitExpression(arg);
                EnsureBoxed();
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                argTemps.Add(temp);
            }

            // Now load all arguments onto stack
            foreach (var temp in argTemps)
            {
                _il.Emit(OpCodes.Ldloc, temp);
            }

            // Pad missing optional arguments with null
            for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            {
                _il.Emit(OpCodes.Ldnull);
            }

            // Call the constructor directly using newobj
            _il.Emit(OpCodes.Newobj, targetCtor);
            SetStackUnknown();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }

    private Type ResolveTypeArg(string typeArg)
    {
        // Simple type argument resolution - similar to ILEmitter
        return typeArg switch
        {
            "number" => typeof(object),
            "string" => typeof(object),
            "boolean" => typeof(object),
            "any" => typeof(object),
            _ => typeof(object)
        };
    }

    private void EmitThis()
    {
        // 'this' in async methods - load from hoisted field if available
        if (_builder.ThisField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);  // Load state machine ref
            _il.Emit(OpCodes.Ldfld, _builder.ThisField);
            SetStackUnknown();
        }
        else
        {
            // Not an instance method or 'this' not hoisted - emit null
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }
}
