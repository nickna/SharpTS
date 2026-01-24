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
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            CurrentSuperclassName = classStmt.Superclass?.Lexeme,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
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
            IsStrictMode = _isStrictMode,
            // ES2022 Private Class Elements support
            CurrentClassName = className,
            CurrentClassBuilder = typeBuilder,
            // Registry services
            ClassRegistry = GetClassRegistry()
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
            // No explicit constructor but has superclass - forward all arguments to parent constructor
            il.Emit(OpCodes.Ldarg_0);
            var parentParams = parentCtor.GetParameters();
            for (int i = 0; i < parentParams.Length; i++)
            {
                il.Emit(OpCodes.Ldarg, i + 1);  // +1 because arg 0 is 'this'
            }

            // Handle generic superclass with type arguments (e.g., extends Box<string>)
            // We need to call the constructor on the closed generic type, not the open generic
            ConstructorInfo ctorToCall = parentCtor;
            Type? baseType = typeBuilder.BaseType;
            if (baseType != null && baseType.IsGenericType && baseType.IsConstructedGenericType)
            {
                // Get the constructor for the closed generic type
                ctorToCall = TypeBuilder.GetConstructor(baseType, parentCtor);
            }

            il.Emit(OpCodes.Call, ctorToCall);
        }
        else
        {
            // Has explicit constructor (which should have super() call) or no superclass
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor([])!);
        }

        // Emit instance field initializers to backing fields (before constructor body)
        // Note: Declare fields are excluded - they have no initialization
        var instanceFieldsWithInit = classStmt.Fields.Where(f => !f.IsStatic && !f.IsPrivate && !f.IsDeclare && f.Initializer != null).ToList();
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

        // ES2022: Initialize instance private fields
        // Private fields use a ConditionalWeakTable for GC-friendly per-instance storage
        EmitPrivateFieldInitialization(il, className, classStmt, ctx);

        // TypeScript 4.9+: Initialize instance auto-accessor backing fields
        if (classStmt.AutoAccessors != null)
        {
            var instanceAutoAccessors = classStmt.AutoAccessors.Where(a => !a.IsStatic && a.Initializer != null).ToList();
            if (instanceAutoAccessors.Count > 0)
            {
                ctx.FieldsField = fieldsField;
                ctx.IsInstanceMethod = true;
                var autoAccessorEmitter = new ILEmitter(ctx);

                foreach (var autoAccessor in instanceAutoAccessors)
                {
                    EmitAutoAccessorInitializer(autoAccessorEmitter, autoAccessor, className, isStatic: false);
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

    /// <summary>
    /// Emits IL to initialize ES2022 private fields for a new instance.
    /// Creates a Dictionary with initial values and adds it to the ConditionalWeakTable.
    /// </summary>
    private void EmitPrivateFieldInitialization(
        ILGenerator il,
        string className,
        Stmt.Class classStmt,
        CompilationContext ctx)
    {
        // Check if this class has instance private fields
        if (!_classes.PrivateFieldStorage.TryGetValue(className, out var storageField))
            return;

        // Get the list of private field names
        if (!_classes.PrivateFieldNames.TryGetValue(className, out var fieldNames) || fieldNames.Count == 0)
            return;

        var instancePrivateFields = classStmt.Fields
            .Where(f => f.IsPrivate && !f.IsStatic)
            .ToList();

        ctx.FieldsField = null; // Not using _fields for private field init
        ctx.IsInstanceMethod = true;
        var initEmitter = new ILEmitter(ctx);

        // Create local for the fields dictionary
        var dictType = typeof(Dictionary<string, object?>);
        var dictLocal = il.DeclareLocal(dictType);

        // Dictionary<string, object?> __fields = new Dictionary<string, object?>(capacity)
        il.Emit(OpCodes.Ldc_I4, fieldNames.Count);
        il.Emit(OpCodes.Newobj, dictType.GetConstructor([typeof(int)])!);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Add each private field with its initializer value (or null)
        foreach (var field in instancePrivateFields)
        {
            string fieldName = field.Name.Lexeme;
            if (fieldName.StartsWith('#'))
                fieldName = fieldName[1..];

            // __fields[fieldName] = initializer ?? null
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, fieldName);

            if (field.Initializer != null)
            {
                initEmitter.EmitExpression(field.Initializer);
                initEmitter.EmitBoxIfNeeded(field.Initializer);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }

            il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        }

        // __privateFields.Add(this, __fields)
        var cwtType = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>)
            .MakeGenericType(typeof(object), typeof(Dictionary<string, object?>));
        var addMethod = cwtType.GetMethod("Add", [typeof(object), typeof(Dictionary<string, object?>)])!;

        il.Emit(OpCodes.Ldsfld, storageField);       // __privateFields
        il.Emit(OpCodes.Ldarg_0);                    // this
        il.Emit(OpCodes.Ldloc, dictLocal);           // __fields
        il.Emit(OpCodes.Callvirt, addMethod);
    }
}
