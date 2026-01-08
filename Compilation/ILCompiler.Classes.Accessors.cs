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
        if (_preDefinedAccessors.TryGetValue(className, out var preDefinedAcc) &&
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
                if (!_instanceGetters.TryGetValue(className, out var classGetters))
                {
                    classGetters = [];
                    _instanceGetters[className] = classGetters;
                }
                classGetters[pascalName] = methodBuilder;
            }
            else
            {
                if (!_instanceSetters.TryGetValue(className, out var classSetters))
                {
                    classSetters = [];
                    _instanceSetters[className] = classSetters;
                }
                classSetters[pascalName] = methodBuilder;
            }
        }

        // Abstract accessors have no body
        if (accessor.IsAbstract)
        {
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders, _types)
        {
            FieldsField = fieldsField,
            IsInstanceMethod = true,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _currentModulePath,
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule
        };

        // Add class generic type parameters to context
        if (_classGenericParams.TryGetValue(typeBuilder.Name, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define setter parameter if applicable
        if (accessor.Kind.Type == TokenType.SET && accessor.SetterParam != null)
        {
            ctx.DefineParameter(accessor.SetterParam.Name.Lexeme, 1);
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
