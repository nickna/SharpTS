using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Constructor emission for class compilation.
/// </summary>
public partial class ILCompiler
{
    private void EmitConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt, FieldInfo fieldsField)
    {
        var defCtx = GetDefinitionContext();
        // Use qualified class name to match DefineClass/EmitClassMethods
        string className = defCtx.GetQualifiedClassName(classStmt.Name.Lexeme);

        // Find constructor implementation (with body), not overload signatures
        var constructor = classStmt.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);

        // Reuse pre-defined constructor if available (from DefineClassMethodsOnly)
        ConstructorBuilder ctorBuilder;
        if (_classes.Constructors.TryGetValue(className, out var existingCtor))
        {
            ctorBuilder = existingCtor;
        }
        else
        {
            // Fallback: resolve typed parameters
            var paramTypes = constructor != null
                ? ParameterTypeResolver.ResolveConstructorParameters(className, constructor.Parameters, _typeMapper, _typeMap)
                : [];
            ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                paramTypes
            );
            _classes.Constructors[className] = ctorBuilder;
        }

        var il = ctorBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            StaticFields = _classes.StaticFields,
            StaticMethods = _classes.StaticMethods,
            ClassConstructors = _classes.Constructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            CurrentSuperclassName = classStmt.Superclass?.Lexeme,
            ClassGenericParams = _classes.GenericParams,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _classes.InstanceMethods,
            InstanceGetters = _classes.InstanceGetters,
            InstanceSetters = _classes.InstanceSetters,
            ClassSuperclass = _classes.Superclass,
            AsyncMethods = null,
            // Typed interop support
            PropertyBackingFields = _typedInterop.PropertyBackingFields,
            ClassProperties = _typedInterop.ClassProperties,
            DeclaredPropertyNames = _typedInterop.DeclaredPropertyNames,
            ReadonlyPropertyNames = _typedInterop.ReadonlyPropertyNames,
            PropertyTypes = _typedInterop.PropertyTypes,
            ExtrasFields = _typedInterop.ExtrasFields,
            UnionGenerator = _unionGenerator,
            // Module support for multi-module compilation
            CurrentModulePath = _modules.CurrentPath,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            // .NET namespace support
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            ClassExprBuilders = _classExprs.Builders,
            IsStrictMode = _isStrictMode
        };

        // Add class generic type parameters to context
        if (_classes.GenericParams.TryGetValue(className, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Initialize _extras dictionary FIRST (before calling parent constructor)
        // This allows parent constructor to access fields via SetFieldsProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);
        il.Emit(OpCodes.Stfld, fieldsField);

        // Initialize @lock decorator fields if present
        if (_locks.SyncLockFields.TryGetValue(className, out var syncLockField))
        {
            // this._syncLock = new object();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, typeof(object).GetConstructor([])!);
            il.Emit(OpCodes.Stfld, syncLockField);
        }

        if (_locks.AsyncLockFields.TryGetValue(className, out var asyncLockField))
        {
            // this._asyncLock = new SemaphoreSlim(1, 1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);  // initialCount = 1
            il.Emit(OpCodes.Ldc_I4_1);  // maxCount = 1
            il.Emit(OpCodes.Newobj, typeof(SemaphoreSlim).GetConstructor([typeof(int), typeof(int)])!);
            il.Emit(OpCodes.Stfld, asyncLockField);
        }

        if (_locks.ReentrancyFields.TryGetValue(className, out var reentrancyField))
        {
            // this._lockReentrancy = new AsyncLocal<int>();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, typeof(AsyncLocal<int>).GetConstructor([])!);
            il.Emit(OpCodes.Stfld, reentrancyField);
        }

        // Call parent constructor
        // If the class has an explicit constructor with super(), the super() in body will handle it.
        // If the class has no explicit constructor but has a superclass, we must call the parent constructor.
        // If the class has no superclass, we call Object constructor.
        string? qualifiedSuperclass = classStmt.Superclass != null ? defCtx.ResolveClassName(classStmt.Superclass.Lexeme) : null;
        if (constructor == null && qualifiedSuperclass != null && _classes.Constructors.TryGetValue(qualifiedSuperclass, out var parentCtor))
        {
            // No explicit constructor but has superclass - call parent's parameterless constructor
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, parentCtor);
        }
        else
        {
            // Has explicit constructor (which should have super() call) or no superclass
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor([])!);
        }

        // Emit instance field initializers to backing fields (before constructor body)
        var instanceFieldsWithInit = classStmt.Fields.Where(f => !f.IsStatic && f.Initializer != null).ToList();
        if (instanceFieldsWithInit.Count > 0)
        {
            ctx.FieldsField = fieldsField;
            ctx.IsInstanceMethod = true;
            var initEmitter = new ILEmitter(ctx);

            foreach (var field in instanceFieldsWithInit)
            {
                string fieldName = field.Name.Lexeme;
                string pascalName = NamingConventions.ToPascalCase(fieldName);

                // Check if this is a declared property with a backing field (using PascalCase key)
                if (_typedInterop.PropertyBackingFields.TryGetValue(className, out var backingFields) &&
                    backingFields.TryGetValue(pascalName, out var backingField))
                {
                    // Store directly in backing field
                    il.Emit(OpCodes.Ldarg_0);  // this

                    // Emit initializer expression
                    initEmitter.EmitExpression(field.Initializer!);

                    // Convert to proper type if needed
                    Type targetType = _typedInterop.PropertyTypes[className][pascalName];
                    EmitTypeConversion(il, initEmitter, field.Initializer!, targetType);

                    il.Emit(OpCodes.Stfld, backingField);
                }
                else
                {
                    // Fallback: store in _extras dictionary (for fields without backing fields)
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, fieldsField);
                    il.Emit(OpCodes.Ldstr, fieldName);
                    initEmitter.EmitExpression(field.Initializer!);
                    initEmitter.EmitBoxIfNeeded(field.Initializer!);
                    il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
                }
            }
        }

        // Emit constructor body
        if (constructor != null)
        {
            ctx.FieldsField = fieldsField;
            ctx.IsInstanceMethod = true;

            // Define parameters with types
            var ctorParams = ctorBuilder.GetParameters();
            for (int i = 0; i < constructor.Parameters.Count; i++)
            {
                Type paramType = i < ctorParams.Length ? ctorParams[i].ParameterType : typeof(object);
                ctx.DefineParameter(constructor.Parameters[i].Name.Lexeme, i + 1, paramType);
            }

            var emitter = new ILEmitter(ctx);

            // No runtime default parameter checks needed - overloads handle this

            if (constructor.Body != null)
            {
                foreach (var stmt in constructor.Body)
                {
                    emitter.EmitStatement(stmt);
                }
            }
        }

        il.Emit(OpCodes.Ret);
    }
}
