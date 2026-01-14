using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override void EmitNew(Expr.New n)
    {
        // Resolve class name (may be qualified for namespace classes or multi-module compilation)
        string resolvedClassName;
        if (n.NamespacePath != null && n.NamespacePath.Count > 0)
        {
            // Build qualified name for namespace classes: Namespace_SubNs_ClassName
            string nsPath = string.Join("_", n.NamespacePath.Select(t => t.Lexeme));
            resolvedClassName = $"{nsPath}_{n.ClassName.Lexeme}";
        }
        else
        {
            resolvedClassName = _ctx!.ResolveClassName(n.ClassName.Lexeme);
        }

        if (_ctx!.Classes.TryGetValue(resolvedClassName, out var typeBuilder) &&
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

            // Get constructor parameters for typed emission
            var ctorParams = ctorBuilder.GetParameters();
            int expectedParamCount = ctorParams.Length;

            // IMPORTANT: In async, await can happen in arguments
            // Emit all arguments first and store to temps
            List<LocalBuilder> argTemps = [];
            foreach (var arg in n.Arguments)
            {
                EmitExpression(arg);
                EnsureBoxed();
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                argTemps.Add(temp);
            }

            // Now load all arguments onto stack with proper type conversions
            for (int i = 0; i < argTemps.Count; i++)
            {
                _il.Emit(OpCodes.Ldloc, argTemps[i]);
                if (i < ctorParams.Length)
                {
                    var targetParamType = ctorParams[i].ParameterType;
                    if (targetParamType.IsValueType && targetParamType != typeof(object))
                    {
                        _il.Emit(OpCodes.Unbox_Any, targetParamType);
                    }
                }
            }

            // Pad missing optional arguments with appropriate default values
            for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            {
                EmitDefaultForType(ctorParams[i].ParameterType);
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

    protected override void EmitThis()
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
