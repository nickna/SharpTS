using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Accessor (getter/setter) emission for class compilation.
/// </summary>
public partial class ILCompiler
{
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

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual;
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

            // Track getter/setter for direct dispatch (using PascalCase key)
            if (accessor.Kind.Type == TokenType.GET)
            {
                if (!_classes.InstanceGetters.TryGetValue(className, out var classGetters))
                {
                    classGetters = [];
                    _classes.InstanceGetters[className] = classGetters;
                }
                classGetters[pascalName] = methodBuilder;
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
        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            FieldsField = fieldsField,
            IsInstanceMethod = true,
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _modules.CurrentPath,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            BuiltInModuleMethodBindings = _builtInModuleMethodBindings,
            ClassExprBuilders = _classExprs.Builders,
            IsStrictMode = _isStrictMode,
            // Registry services
            ClassRegistry = GetClassRegistry()
        };

        // Add class generic type parameters to context
        if (_classes.GenericParams.TryGetValue(typeBuilder.Name, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define setter parameter if applicable with typed parameter type
        if (accessor.Kind.Type == TokenType.SET && accessor.SetterParam != null)
        {
            var accessorParams = methodBuilder.GetParameters();
            Type? paramType = accessorParams.Length > 0 ? accessorParams[0].ParameterType : null;
            ctx.DefineParameter(accessor.SetterParam.Name.Lexeme, 1, paramType);
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
            // Default return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }
}
