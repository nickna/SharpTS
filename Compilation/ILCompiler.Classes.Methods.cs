using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Instance method definition and emission for class compilation.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Gets typed parameter types for a method from the TypeMap.
    /// Falls back to object[] if type info is not available.
    /// </summary>
    private Type[] GetTypedMethodParameters(string className, string methodName, int paramCount)
    {
        var classType = _typeMap.GetClassType(className);
        if (classType == null)
            return Enumerable.Repeat(typeof(object), paramCount).ToArray();

        // Check instance methods
        if (classType.Methods.TryGetValue(methodName, out var methodType))
        {
            if (methodType is TSTypeInfo.Function func)
            {
                return func.ParamTypes.Select(pt => _typeMapper.MapTypeInfoStrict(pt)).ToArray();
            }
            if (methodType is TSTypeInfo.OverloadedFunction of)
            {
                return of.Implementation.ParamTypes.Select(pt => _typeMapper.MapTypeInfoStrict(pt)).ToArray();
            }
        }

        // Check static methods
        if (classType.StaticMethods.TryGetValue(methodName, out var staticMethodType))
        {
            if (staticMethodType is TSTypeInfo.Function func)
            {
                return func.ParamTypes.Select(pt => _typeMapper.MapTypeInfoStrict(pt)).ToArray();
            }
            if (staticMethodType is TSTypeInfo.OverloadedFunction of)
            {
                return of.Implementation.ParamTypes.Select(pt => _typeMapper.MapTypeInfoStrict(pt)).ToArray();
            }
        }

        return Enumerable.Repeat(typeof(object), paramCount).ToArray();
    }

    /// <summary>
    /// Gets the typed return type for a method from the TypeMap.
    /// Falls back to object (or Task&lt;object&gt; for async) if type info is not available.
    /// </summary>
    private Type GetTypedMethodReturnType(string className, string methodName, bool isAsync)
    {
        var classType = _typeMap.GetClassType(className);
        if (classType == null)
            return isAsync ? typeof(Task<object>) : typeof(object);

        TSTypeInfo.Function? funcType = null;

        // Check instance methods
        if (classType.Methods.TryGetValue(methodName, out var methodType))
        {
            funcType = methodType switch
            {
                TSTypeInfo.Function f => f,
                TSTypeInfo.OverloadedFunction of => of.Implementation,
                _ => null
            };
        }
        // Check static methods
        else if (classType.StaticMethods.TryGetValue(methodName, out var staticMethodType))
        {
            funcType = staticMethodType switch
            {
                TSTypeInfo.Function f => f,
                TSTypeInfo.OverloadedFunction of => of.Implementation,
                _ => null
            };
        }

        if (funcType is null)
            return isAsync ? typeof(Task<object>) : typeof(object);

        Type returnType = _typeMapper.MapTypeInfoStrict(funcType.ReturnType);

        // For async methods, wrap in Task<T>
        if (isAsync)
        {
            // If return type is void, use Task (non-generic)
            if (returnType == typeof(void))
                return typeof(Task);
            return typeof(Task<>).MakeGenericType(returnType);
        }

        return returnType;
    }

    /// <summary>
    /// Defines all class methods (without emitting bodies) so they're available for
    /// direct dispatch in async state machines.
    /// </summary>
    private void DefineAllClassMethods(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Class classStmt)
            {
                DefineClassMethodsOnly(classStmt);
            }
        }
    }

    /// <summary>
    /// Defines method signatures and registers them in _instanceMethods without emitting bodies.
    /// Also pre-defines the constructor so it's available for EmitNew in async contexts.
    /// </summary>
    private void DefineClassMethodsOnly(Stmt.Class classStmt)
    {
        // Skip @DotNetType external type classes - they don't have TypeBuilders
        if (classStmt.IsDeclare)
            return;

        var ctx = GetDefinitionContext();
        string qualifiedClassName = ctx.ResolveClassName(classStmt.Name.Lexeme);

        // Also skip if this is an external type (registered via @DotNetType decorator)
        if (_externalTypes.ContainsKey(qualifiedClassName) ||
            _externalTypes.ContainsKey(classStmt.Name.Lexeme))
            return;

        if (!_classBuilders.TryGetValue(qualifiedClassName, out var typeBuilder))
            return;  // Skip if no TypeBuilder exists for this class

        // Pre-define constructor (if not already defined)
        if (!_classConstructors.ContainsKey(qualifiedClassName))
        {
            var constructor = classStmt.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);
            var ctorParamTypes = constructor?.Parameters.Select(_ => typeof(object)).ToArray() ?? [];

            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                ctorParamTypes
            );

            _classConstructors[qualifiedClassName] = ctorBuilder;
        }

        // Initialize static methods dictionary for this class
        if (!_staticMethods.ContainsKey(qualifiedClassName))
        {
            _staticMethods[qualifiedClassName] = [];
        }

        // Pre-define static methods (so they're available during async MoveNext emission)
        foreach (var method in classStmt.Methods.Where(m => m.Body != null && m.IsStatic && m.Name.Lexeme != "constructor"))
        {
            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();
            // Async methods return Task<object>, sync methods return object
            var returnType = method.IsAsync ? _types.TaskOfObject : typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                MethodAttributes.Public | MethodAttributes.Static,
                returnType,
                paramTypes
            );

            _staticMethods[qualifiedClassName][method.Name.Lexeme] = methodBuilder;
        }

        // Define instance methods (skip overload signatures with no body)
        foreach (var method in classStmt.Methods.Where(m => m.Body != null))
        {
            if (method.IsStatic || method.Name.Lexeme == "constructor")
                continue;

            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
            if (method.IsAbstract)
            {
                methodAttrs |= MethodAttributes.Abstract;
            }

            Type returnType = method.IsAsync ? typeof(Task<object>) : typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                methodAttrs,
                returnType,
                paramTypes
            );

            // Track instance method for direct dispatch
            if (!_instanceMethods.TryGetValue(typeBuilder.Name, out var classMethods))
            {
                classMethods = [];
                _instanceMethods[typeBuilder.Name] = classMethods;
            }
            classMethods[method.Name.Lexeme] = methodBuilder;

            // Store the method builder for body emission later
            if (!_preDefinedMethods.TryGetValue(classStmt.Name.Lexeme, out var preDefined))
            {
                preDefined = [];
                _preDefinedMethods[classStmt.Name.Lexeme] = preDefined;
            }
            preDefined[method.Name.Lexeme] = methodBuilder;
        }

        // Define accessors with PascalCase naming
        // Note: Explicit accessors keep object-typed signatures because their bodies
        // use dynamic field storage. Field-backed properties already have typed signatures.
        if (classStmt.Accessors != null)
        {
            string className = typeBuilder.Name;

            foreach (var accessor in classStmt.Accessors)
            {
                string accessorName = accessor.Name.Lexeme;
                string pascalName = NamingConventions.ToPascalCase(accessorName);
                string methodName = accessor.Kind.Type == TokenType.GET
                    ? $"get_{pascalName}"
                    : $"set_{pascalName}";

                // Explicit accessors use object types (their bodies work with dynamic field storage)
                Type[] paramTypes = accessor.Kind.Type == TokenType.SET
                    ? [typeof(object)]
                    : [];

                MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual;
                if (accessor.IsAbstract)
                {
                    methodAttrs |= MethodAttributes.Abstract;
                }

                var methodBuilder = typeBuilder.DefineMethod(
                    methodName,
                    methodAttrs,
                    typeof(object),  // Explicit accessors return object
                    paramTypes
                );

                // Track getter/setter using PascalCase key
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

                // Store for body emission
                if (!_preDefinedAccessors.TryGetValue(classStmt.Name.Lexeme, out var preDefinedAcc))
                {
                    preDefinedAcc = [];
                    _preDefinedAccessors[classStmt.Name.Lexeme] = preDefinedAcc;
                }
                preDefinedAcc[methodName] = methodBuilder;

                // Track for PropertyBuilder creation
                if (!_explicitAccessors.TryGetValue(className, out var accessors))
                {
                    accessors = [];
                    _explicitAccessors[className] = accessors;
                }

                if (!accessors.TryGetValue(pascalName, out var accessorInfo))
                {
                    accessorInfo = (null, null, typeof(object));
                }

                if (accessor.Kind.Type == TokenType.GET)
                {
                    accessors[pascalName] = (methodBuilder, accessorInfo.Setter, typeof(object));
                }
                else
                {
                    accessors[pascalName] = (accessorInfo.Getter, methodBuilder, typeof(object));
                }
            }

            // Create PropertyBuilders for explicit accessors
            CreateExplicitAccessorProperties(typeBuilder, className);
        }
    }

    /// <summary>
    /// Creates PropertyBuilders for explicit accessors after all getter/setter methods are defined.
    /// </summary>
    private void CreateExplicitAccessorProperties(TypeBuilder typeBuilder, string className)
    {
        if (!_explicitAccessors.TryGetValue(className, out var accessors))
            return;

        foreach (var (pascalName, (getter, setter, propertyType)) in accessors)
        {
            if (getter == null && setter == null)
                continue;

            // Determine property type: prefer getter return type, then setter param, then fallback
            Type propType = propertyType;
            if (getter != null && getter.ReturnType != typeof(void))
            {
                propType = getter.ReturnType;
            }
            else if (setter != null)
            {
                var setterParams = setter.GetParameters();
                if (setterParams.Length > 0)
                {
                    propType = setterParams[0].ParameterType;
                }
            }

            var property = typeBuilder.DefineProperty(
                pascalName,
                PropertyAttributes.None,
                propType,
                null
            );

            if (getter != null)
                property.SetGetMethod(getter);
            if (setter != null)
                property.SetSetMethod(setter);

            // Track the property
            if (!_classProperties.TryGetValue(className, out var classProps))
            {
                classProps = [];
                _classProperties[className] = classProps;
            }
            classProps[pascalName] = property;
        }
    }

    private void EmitClassMethods(Stmt.Class classStmt)
    {
        // Skip @DotNetType external type classes - they don't have TypeBuilders
        if (classStmt.IsDeclare)
            return;

        // Get qualified class name (must match what DefineClass used)
        string qualifiedClassName = GetDefinitionContext().GetQualifiedClassName(classStmt.Name.Lexeme);

        // Also skip if this is an external type (registered via @DotNetType decorator)
        if (_externalTypes.ContainsKey(qualifiedClassName) ||
            _externalTypes.ContainsKey(classStmt.Name.Lexeme))
            return;

        if (!_classBuilders.TryGetValue(qualifiedClassName, out var typeBuilder))
            return;  // Skip if no TypeBuilder exists for this class
        var fieldsField = _instanceFieldsField[qualifiedClassName];

        // Initialize static methods dictionary for this class
        if (!_staticMethods.ContainsKey(qualifiedClassName))
        {
            _staticMethods[qualifiedClassName] = [];
        }

        // Define static methods first (so we can reference them in the static constructor)
        // Skip overload signatures (no body)
        foreach (var method in classStmt.Methods.Where(m => m.Body != null))
        {
            if (method.IsStatic && method.Name.Lexeme != "constructor")
            {
                DefineStaticMethod(typeBuilder, qualifiedClassName, method);
            }
        }

        // Emit static constructor for static property initializers
        EmitStaticConstructor(typeBuilder, classStmt, qualifiedClassName);

        // Emit constructor
        EmitConstructor(typeBuilder, classStmt, fieldsField);

        // Emit method bodies (skip overload signatures with no body)
        foreach (var method in classStmt.Methods.Where(m => m.Body != null))
        {
            if (method.Name.Lexeme != "constructor")
            {
                if (method.IsStatic)
                {
                    EmitStaticMethodBody(qualifiedClassName, method);
                }
                else
                {
                    EmitMethod(typeBuilder, method, fieldsField);
                }
            }
        }

        // Emit accessor methods
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                EmitAccessor(typeBuilder, accessor, fieldsField);
            }
        }
    }

    private void EmitMethod(TypeBuilder typeBuilder, Stmt.Function method, FieldInfo fieldsField)
    {
        MethodBuilder methodBuilder;

        // Check if method was pre-defined in DefineClassMethodsOnly
        if (_preDefinedMethods.TryGetValue(typeBuilder.Name, out var preDefined) &&
            preDefined.TryGetValue(method.Name.Lexeme, out var existingMethod))
        {
            methodBuilder = existingMethod;
        }
        else
        {
            // Define the method (fallback for when DefineClassMethodsOnly wasn't called)
            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
            if (method.IsAbstract)
            {
                methodAttrs |= MethodAttributes.Abstract;
            }

            Type returnType = method.IsAsync ? typeof(Task<object>) : typeof(object);

            methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                methodAttrs,
                returnType,
                paramTypes
            );

            // Track instance method for direct dispatch
            if (!_instanceMethods.TryGetValue(typeBuilder.Name, out var classMethods))
            {
                classMethods = [];
                _instanceMethods[typeBuilder.Name] = classMethods;
            }
            classMethods[method.Name.Lexeme] = methodBuilder;
        }

        // Apply method-level decorators as .NET attributes
        if (_decoratorMode != DecoratorMode.None)
        {
            ApplyMethodDecorators(method, methodBuilder);
        }

        // Abstract methods have no body
        if (method.IsAbstract)
        {
            return;
        }

        // Async methods use state machine generation
        if (method.IsAsync)
        {
            EmitAsyncMethodBody(methodBuilder, method, fieldsField);
            return;
        }

        // Check if method has @lock decorator
        bool hasLock = HasLockDecorator(method);

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
            EnumToModule = _enumToModule,
            DotNetNamespace = _currentDotNetNamespace,
            // @lock decorator support
            SyncLockFields = _syncLockFields,
            AsyncLockFields = _asyncLockFields,
            LockReentrancyFields = _lockReentrancyFields,
            StaticSyncLockFields = _staticSyncLockFields,
            StaticAsyncLockFields = _staticAsyncLockFields,
            StaticLockReentrancyFields = _staticLockReentrancyFields,
            TypeEmitterRegistry = _typeEmitterRegistry
        };

        // Add class generic type parameters to context
        if (_classGenericParams.TryGetValue(typeBuilder.Name, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define parameters
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i + 1);
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks (instance method)
        emitter.EmitDefaultParameters(method.Parameters, true);

        // Variables for @lock decorator support
        LocalBuilder? prevReentrancyLocal = null;
        LocalBuilder? lockTakenLocal = null;
        FieldBuilder? syncLockField = null;
        FieldBuilder? reentrancyField = null;

        // Set up @lock decorator - reentrancy-aware Monitor pattern
        if (hasLock && _syncLockFields.TryGetValue(typeBuilder.Name, out syncLockField) &&
            _lockReentrancyFields.TryGetValue(typeBuilder.Name, out reentrancyField))
        {
            prevReentrancyLocal = il.DeclareLocal(typeof(int));     // int __prevReentrancy
            lockTakenLocal = il.DeclareLocal(typeof(bool));         // bool __lockTaken

            // Set up deferred return handling for the lock's exception block
            ctx.ReturnValueLocal = il.DeclareLocal(typeof(object));
            ctx.ReturnLabel = il.DefineLabel();
            ctx.ExceptionBlockDepth++;

            // int __prevReentrancy = _lockReentrancy.Value;
            il.Emit(OpCodes.Ldarg_0);                               // this
            il.Emit(OpCodes.Ldfld, reentrancyField);                // this._lockReentrancy
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.GetMethod!);
            il.Emit(OpCodes.Stloc, prevReentrancyLocal);

            // _lockReentrancy.Value = __prevReentrancy + 1;
            il.Emit(OpCodes.Ldarg_0);                               // this
            il.Emit(OpCodes.Ldfld, reentrancyField);                // this._lockReentrancy
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.SetMethod!);

            // bool __lockTaken = false;
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, lockTakenLocal);

            // if (__prevReentrancy == 0) { Monitor.Enter(_syncLock, ref __lockTaken); }
            var skipEnterLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bne_Un, skipEnterLabel);

            // Monitor.Enter(this._syncLock, ref __lockTaken);
            il.Emit(OpCodes.Ldarg_0);                               // this
            il.Emit(OpCodes.Ldfld, syncLockField);                  // this._syncLock
            il.Emit(OpCodes.Ldloca, lockTakenLocal);                // ref __lockTaken
            il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Enter", [typeof(object), typeof(bool).MakeByRefType()])!);

            il.MarkLabel(skipEnterLabel);

            // Begin try block
            il.BeginExceptionBlock();
        }

        // Abstract methods have no body to emit
        if (method.Body != null)
        {
            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Close @lock decorator - finally block
        if (hasLock && prevReentrancyLocal != null && lockTakenLocal != null &&
            syncLockField != null && reentrancyField != null)
        {
            // Store default return value if no explicit return was emitted
            // ReturnValueLocal is guaranteed non-null here (set up earlier in hasLock block)
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, ctx.ReturnValueLocal!);
            il.Emit(OpCodes.Leave, ctx.ReturnLabel);

            // Begin finally block
            il.BeginFinallyBlock();

            // _lockReentrancy.Value = __prevReentrancy;
            il.Emit(OpCodes.Ldarg_0);                               // this
            il.Emit(OpCodes.Ldfld, reentrancyField);                // this._lockReentrancy
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.SetMethod!);

            // if (__lockTaken) { Monitor.Exit(_syncLock); }
            var skipExitLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, lockTakenLocal);
            il.Emit(OpCodes.Brfalse, skipExitLabel);

            // Monitor.Exit(this._syncLock);
            il.Emit(OpCodes.Ldarg_0);                               // this
            il.Emit(OpCodes.Ldfld, syncLockField);                  // this._syncLock
            il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Exit", [typeof(object)])!);

            il.MarkLabel(skipExitLabel);

            // End try/finally block
            il.EndExceptionBlock();

            ctx.ExceptionBlockDepth--;

            // Mark return label and emit actual return
            il.MarkLabel(ctx.ReturnLabel);
            il.Emit(OpCodes.Ldloc, ctx.ReturnValueLocal!);  // Non-null in hasLock path
            il.Emit(OpCodes.Ret);
        }
        // Finalize any deferred returns from exception blocks (non-@lock path)
        else if (emitter.HasDeferredReturns)
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
