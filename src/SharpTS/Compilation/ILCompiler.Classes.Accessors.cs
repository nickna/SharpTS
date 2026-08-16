using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Accessor (getter/setter) emission for class compilation.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Pre-defines a synthetic method for a symbol-keyed computed accessor (#266),
    /// e.g. <c>static get [Symbol.species]()</c>. The method has no spec-visible
    /// name; it is invoked reflectively via the runtime symbol-accessor registry.
    /// Recorded in <see cref="ClassState.SymbolAccessors"/> for body emission and
    /// for registration in the class .cctor.
    /// </summary>
    private void DefineSymbolAccessorMethod(TypeBuilder typeBuilder, Stmt.Accessor accessor)
    {
        string className = typeBuilder.Name;
        if (!_classes.SymbolAccessors.TryGetValue(className, out var list))
        {
            list = [];
            _classes.SymbolAccessors[className] = list;
        }

        bool isGetter = accessor.Kind.Type == TokenType.GET;
        // Non-virtual instance methods so MethodBase.Invoke targets exactly the
        // registered method; inheritance is handled by the registry's base-chain walk.
        MethodAttributes attrs = MethodAttributes.Public | MethodAttributes.HideBySig;
        if (accessor.IsStatic) attrs |= MethodAttributes.Static;
        Type[] paramTypes = isGetter ? [] : [typeof(object)];

        var methodBuilder = typeBuilder.DefineMethod(
            $"$sym_{(isGetter ? "get" : "set")}_{list.Count}",
            attrs,
            typeof(object),
            paramTypes);

        list.Add((accessor, methodBuilder));
    }

    /// <summary>
    /// Emits bodies for the symbol-keyed accessors recorded by
    /// <see cref="DefineSymbolAccessorMethod"/> for this class.
    /// </summary>
    private void EmitSymbolAccessors(TypeBuilder typeBuilder, FieldInfo fieldsField)
    {
        if (!_classes.SymbolAccessors.TryGetValue(typeBuilder.Name, out var list))
            return;
        foreach (var (accessor, methodBuilder) in list)
        {
            EmitAccessorBody(typeBuilder, accessor, methodBuilder, fieldsField);
        }
    }

    private void EmitAccessor(TypeBuilder typeBuilder, Stmt.Accessor accessor, FieldInfo fieldsField)
    {
        // Use PascalCase naming convention: get_<PascalPropertyName> or set_<PascalPropertyName>
        string pascalName = NamingConventions.ToPascalCase(accessor.Name.Lexeme);
        string methodName = accessor.Kind.Type == TokenType.GET
            ? $"get_{pascalName}"
            : $"set_{pascalName}";

        string className = typeBuilder.Name;
        MethodBuilder methodBuilder;

        // Check if accessor was pre-defined in DefineClassMethodsOnly
        if (_classes.PreDefinedAccessors.TryGetValue(className, out var preDefinedAcc) &&
            preDefinedAcc.TryGetValue(methodName, out var existingAccessor))
        {
            methodBuilder = existingAccessor;
        }
        else
        {
            // Define the accessor (fallback for when DefineClassMethodsOnly wasn't called)
            Type[] paramTypes = accessor.Kind.Type == TokenType.SET
                ? [typeof(object)]
                : [];

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
            methodAttrs |= accessor.IsStatic ? MethodAttributes.Static : MethodAttributes.Virtual;
            if (accessor.IsAbstract)
            {
                methodAttrs |= MethodAttributes.Abstract;
            }

            methodBuilder = typeBuilder.DefineMethod(
                methodName,
                methodAttrs,
                typeof(object),
                paramTypes
            );

            // Static accessors register in StaticGetters/StaticSetters keyed by the original
            // (camelCase) name so ClassRegistry lookups match. Instance accessors register in
            // InstanceGetters/Setters keyed by PascalCase, preserving existing convention.
            if (accessor.Kind.Type == TokenType.GET)
            {
                if (accessor.IsStatic)
                {
                    if (!_classes.StaticGetters.TryGetValue(className, out var classStaticGetters))
                    {
                        classStaticGetters = [];
                        _classes.StaticGetters[className] = classStaticGetters;
                    }
                    classStaticGetters[accessor.Name.Lexeme] = methodBuilder;
                }
                else
                {
                    if (!_classes.InstanceGetters.TryGetValue(className, out var classGetters))
                    {
                        classGetters = [];
                        _classes.InstanceGetters[className] = classGetters;
                    }
                    classGetters[pascalName] = methodBuilder;
                }
            }
            else
            {
                if (accessor.IsStatic)
                {
                    if (!_classes.StaticSetters.TryGetValue(className, out var classStaticSetters))
                    {
                        classStaticSetters = [];
                        _classes.StaticSetters[className] = classStaticSetters;
                    }
                    classStaticSetters[accessor.Name.Lexeme] = methodBuilder;
                }
                else
                {
                    if (!_classes.InstanceSetters.TryGetValue(className, out var classSetters))
                    {
                        classSetters = [];
                        _classes.InstanceSetters[className] = classSetters;
                    }
                    classSetters[pascalName] = methodBuilder;
                }
            }
        }

        EmitAccessorBody(typeBuilder, accessor, methodBuilder, fieldsField);
    }

    /// <summary>
    /// Emits decorators (if any) and the IL body for a resolved accessor method.
    /// Shared by the string-named accessor path (<see cref="EmitAccessor"/>) and the
    /// symbol-keyed computed accessor path (<see cref="EmitSymbolAccessors"/>).
    /// </summary>
    private void EmitAccessorBody(TypeBuilder typeBuilder, Stmt.Accessor accessor, MethodBuilder methodBuilder, FieldInfo fieldsField)
    {
        string className = typeBuilder.Name;

        // Apply accessor-level decorators as .NET attributes
        if (_decoratorMode != DecoratorMode.None)
        {
            ApplyAccessorDecorators(accessor, methodBuilder);
        }

        // Abstract accessors have no body
        if (accessor.IsAbstract)
        {
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, methodBuilder);
        ctx.FieldsField = fieldsField;
        ctx.IsInstanceMethod = !accessor.IsStatic;
        ctx.CurrentClassName = className;
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.EmittingTypeBuilder = typeBuilder;
        // Module-level / captured top-level variable access. Without these an
        // accessor body that references a top-level binding (a captured `let`,
        // a same-module export, an import) throws ReferenceError at runtime —
        // every other body-emission context (methods, ctors, functions, …)
        // sets them; the accessor path was the lone omission. (#300)
        ApplyCapturedTopLevelVariableAccess(ctx, memberBodyExports: true);

        // Add class generic type parameters to context
        if (_classes.GenericParams.TryGetValue(typeBuilder.Name, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define setter parameter if applicable with typed parameter type.
        // For instance setters, arg0 is `this` and the value is at index 1.
        // For static setters, the value is at index 0 (no implicit receiver).
        if (accessor.Kind.Type == TokenType.SET && accessor.SetterParam != null)
        {
            var accessorParams = methodBuilder.GetParameters();
            Type? paramType = accessorParams.Length > 0 ? accessorParams[0].ParameterType : null;
            int paramIndex = accessor.IsStatic ? 0 : 1;
            ctx.DefineParameter(accessor.SetterParam.Name.Lexeme, paramIndex, paramType);
        }

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in accessor.Body)
        {
            emitter.EmitStatement(stmt);
        }

        // Finalize any deferred returns from exception blocks
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // A getter that falls off the end completes with `undefined` (ECMA-262).
            // Route through EmitDefaultReturnValue so the `object` slot materializes the
            // `$Undefined` sentinel instead of CLR null. Setters also return `object`
            // here (their value is discarded by callers), so this is correct for both. (#588)
            EmitDefaultReturnValue(il, methodBuilder.ReturnType);
            il.Emit(OpCodes.Ret);
        }
    }
}
