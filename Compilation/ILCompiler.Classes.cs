using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Class definition and method emission for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    private void DefineClass(Stmt.Class classStmt)
    {
        Type? baseType = null;
        if (classStmt.Superclass != null && _classBuilders.TryGetValue(classStmt.Superclass.Lexeme, out var superBuilder))
        {
            baseType = superBuilder;
        }

        // Set TypeAttributes.Abstract if the class is abstract
        TypeAttributes typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (classStmt.IsAbstract)
        {
            typeAttrs |= TypeAttributes.Abstract;
        }

        var typeBuilder = _moduleBuilder.DefineType(
            classStmt.Name.Lexeme,
            typeAttrs,
            baseType
        );

        // Track superclass for inheritance-aware method resolution
        _classSuperclass[classStmt.Name.Lexeme] = classStmt.Superclass?.Lexeme;

        // Handle generic type parameters
        if (classStmt.TypeParams != null && classStmt.TypeParams.Count > 0)
        {
            string[] typeParamNames = classStmt.TypeParams.Select(tp => tp.Name.Lexeme).ToArray();
            var genericParams = typeBuilder.DefineGenericParameters(typeParamNames);

            // Apply constraints
            for (int i = 0; i < classStmt.TypeParams.Count; i++)
            {
                var constraint = classStmt.TypeParams[i].Constraint;
                if (constraint != null)
                {
                    Type constraintType = ResolveConstraintType(constraint);
                    if (constraintType.IsInterface)
                        genericParams[i].SetInterfaceConstraints(constraintType);
                    else
                        genericParams[i].SetBaseTypeConstraint(constraintType);
                }
            }

            _classGenericParams[classStmt.Name.Lexeme] = genericParams;
        }

        // Add _fields dictionary for dynamic property storage
        var fieldsField = typeBuilder.DefineField(
            "_fields",
            typeof(Dictionary<string, object>),
            FieldAttributes.Private
        );
        _instanceFieldsField[classStmt.Name.Lexeme] = fieldsField;

        // Add static fields for static properties
        var staticFieldBuilders = new Dictionary<string, FieldBuilder>();
        foreach (var field in classStmt.Fields)
        {
            if (field.IsStatic)
            {
                var fieldBuilder = typeBuilder.DefineField(
                    field.Name.Lexeme,
                    typeof(object),
                    FieldAttributes.Public | FieldAttributes.Static
                );
                staticFieldBuilders[field.Name.Lexeme] = fieldBuilder;
            }
        }

        _classBuilders[classStmt.Name.Lexeme] = typeBuilder;
        _staticFields[classStmt.Name.Lexeme] = staticFieldBuilders;
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
        var typeBuilder = _classBuilders[classStmt.Name.Lexeme];

        // Pre-define constructor (if not already defined)
        if (!_classConstructors.ContainsKey(classStmt.Name.Lexeme))
        {
            var constructor = classStmt.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);
            var ctorParamTypes = constructor?.Parameters.Select(_ => typeof(object)).ToArray() ?? [];

            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                ctorParamTypes
            );

            _classConstructors[classStmt.Name.Lexeme] = ctorBuilder;
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

        // Define accessors
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                string methodName = accessor.Kind.Type == TokenType.GET
                    ? $"get_{accessor.Name.Lexeme}"
                    : $"set_{accessor.Name.Lexeme}";

                Type[] paramTypes = accessor.Kind.Type == TokenType.SET
                    ? [typeof(object)]
                    : [];

                MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
                if (accessor.IsAbstract)
                {
                    methodAttrs |= MethodAttributes.Abstract;
                }

                var methodBuilder = typeBuilder.DefineMethod(
                    methodName,
                    methodAttrs,
                    typeof(object),
                    paramTypes
                );

                // Track getter/setter
                string className = typeBuilder.Name;
                if (accessor.Kind.Type == TokenType.GET)
                {
                    if (!_instanceGetters.TryGetValue(className, out var classGetters))
                    {
                        classGetters = [];
                        _instanceGetters[className] = classGetters;
                    }
                    classGetters[accessor.Name.Lexeme] = methodBuilder;
                }
                else
                {
                    if (!_instanceSetters.TryGetValue(className, out var classSetters))
                    {
                        classSetters = [];
                        _instanceSetters[className] = classSetters;
                    }
                    classSetters[accessor.Name.Lexeme] = methodBuilder;
                }

                // Store for body emission
                if (!_preDefinedAccessors.TryGetValue(classStmt.Name.Lexeme, out var preDefinedAcc))
                {
                    preDefinedAcc = [];
                    _preDefinedAccessors[classStmt.Name.Lexeme] = preDefinedAcc;
                }
                preDefinedAcc[methodName] = methodBuilder;
            }
        }
    }

    private void EmitClassMethods(Stmt.Class classStmt)
    {
        var typeBuilder = _classBuilders[classStmt.Name.Lexeme];
        var fieldsField = _instanceFieldsField[classStmt.Name.Lexeme];

        // Initialize static methods dictionary for this class
        if (!_staticMethods.ContainsKey(classStmt.Name.Lexeme))
        {
            _staticMethods[classStmt.Name.Lexeme] = new Dictionary<string, MethodBuilder>();
        }

        // Define static methods first (so we can reference them in the static constructor)
        // Skip overload signatures (no body)
        foreach (var method in classStmt.Methods.Where(m => m.Body != null))
        {
            if (method.IsStatic && method.Name.Lexeme != "constructor")
            {
                DefineStaticMethod(typeBuilder, classStmt.Name.Lexeme, method);
            }
        }

        // Emit static constructor for static property initializers
        EmitStaticConstructor(typeBuilder, classStmt);

        // Emit constructor
        EmitConstructor(typeBuilder, classStmt, fieldsField);

        // Emit method bodies (skip overload signatures with no body)
        foreach (var method in classStmt.Methods.Where(m => m.Body != null))
        {
            if (method.Name.Lexeme != "constructor")
            {
                if (method.IsStatic)
                {
                    EmitStaticMethodBody(classStmt.Name.Lexeme, method);
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

    private void EmitAccessor(TypeBuilder typeBuilder, Stmt.Accessor accessor, FieldInfo fieldsField)
    {
        // Use naming convention: get_<propertyName> or set_<propertyName>
        string methodName = accessor.Kind.Type == TokenType.GET
            ? $"get_{accessor.Name.Lexeme}"
            : $"set_{accessor.Name.Lexeme}";

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

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
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

            // Track getter/setter for direct dispatch
            if (accessor.Kind.Type == TokenType.GET)
            {
                if (!_instanceGetters.TryGetValue(className, out var classGetters))
                {
                    classGetters = [];
                    _instanceGetters[className] = classGetters;
                }
                classGetters[accessor.Name.Lexeme] = methodBuilder;
            }
            else
            {
                if (!_instanceSetters.TryGetValue(className, out var classSetters))
                {
                    classSetters = [];
                    _instanceSetters[className] = classSetters;
                }
                classSetters[accessor.Name.Lexeme] = methodBuilder;
            }
        }

        // Abstract accessors have no body
        if (accessor.IsAbstract)
        {
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
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
            AsyncMethods = null
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

    private void DefineStaticMethod(TypeBuilder typeBuilder, string className, Stmt.Function method)
    {
        var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();
        var methodBuilder = typeBuilder.DefineMethod(
            method.Name.Lexeme,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            paramTypes
        );

        _staticMethods[className][method.Name.Lexeme] = methodBuilder;
    }

    private void EmitStaticConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt)
    {
        // Only emit if there are static fields with initializers
        var staticFieldsWithInit = classStmt.Fields.Where(f => f.IsStatic && f.Initializer != null).ToList();
        if (staticFieldsWithInit.Count == 0) return;

        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _staticFields,
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
            AsyncMethods = null
        };

        var emitter = new ILEmitter(ctx);

        var classStaticFields = _staticFields[classStmt.Name.Lexeme];
        foreach (var field in staticFieldsWithInit)
        {
            // Emit the initializer expression
            emitter.EmitExpression(field.Initializer!);
            emitter.EmitBoxIfNeeded(field.Initializer!);

            // Store in static field using the stored FieldBuilder
            var staticField = classStaticFields[field.Name.Lexeme];
            il.Emit(OpCodes.Stsfld, staticField);
        }

        il.Emit(OpCodes.Ret);
    }

    private void EmitStaticMethodBody(string className, Stmt.Function method)
    {
        var typeBuilder = _classBuilders[className];
        var methodBuilder = _staticMethods[className][method.Name.Lexeme];

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            IsInstanceMethod = false,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            CurrentClassBuilder = typeBuilder,
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
            AsyncMethods = null
        };

        // Define parameters (starting at index 0, not 1 since no 'this')
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i);
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks (static method)
        emitter.EmitDefaultParameters(method.Parameters, false);

        // Abstract methods have no body to emit
        if (method.Body != null)
        {
            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
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

    private void EmitConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt, FieldInfo fieldsField)
    {
        // Find constructor implementation (with body), not overload signatures
        var constructor = classStmt.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);

        // Reuse pre-defined constructor if available (from DefineClassMethodsOnly)
        ConstructorBuilder ctorBuilder;
        if (_classConstructors.TryGetValue(classStmt.Name.Lexeme, out var existingCtor))
        {
            ctorBuilder = existingCtor;
        }
        else
        {
            var paramTypes = constructor?.Parameters.Select(_ => typeof(object)).ToArray() ?? [];
            ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                paramTypes
            );
            _classConstructors[classStmt.Name.Lexeme] = ctorBuilder;
        }

        var il = ctorBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
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
            CurrentSuperclassName = classStmt.Superclass?.Lexeme,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };

        // Add class generic type parameters to context
        if (_classGenericParams.TryGetValue(classStmt.Name.Lexeme, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Initialize _fields dictionary FIRST (before calling parent constructor)
        // This allows parent constructor to access fields via SetFieldsProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);
        il.Emit(OpCodes.Stfld, fieldsField);

        // Call parent constructor
        // If the class has an explicit constructor with super(), the super() in body will handle it.
        // If the class has no explicit constructor but has a superclass, we must call the parent constructor.
        // If the class has no superclass, we call Object constructor.
        if (constructor == null && classStmt.Superclass != null && _classConstructors.TryGetValue(classStmt.Superclass.Lexeme, out var parentCtor))
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

        // Emit instance field initializers (before constructor body)
        var instanceFieldsWithInit = classStmt.Fields.Where(f => !f.IsStatic && f.Initializer != null).ToList();
        if (instanceFieldsWithInit.Count > 0)
        {
            ctx.FieldsField = fieldsField;
            ctx.IsInstanceMethod = true;
            var initEmitter = new ILEmitter(ctx);

            foreach (var field in instanceFieldsWithInit)
            {
                // Load this._fields dictionary
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fieldsField);

                // Load field name
                il.Emit(OpCodes.Ldstr, field.Name.Lexeme);

                // Emit initializer expression
                initEmitter.EmitExpression(field.Initializer!);
                initEmitter.EmitBoxIfNeeded(field.Initializer!);

                // Store in dictionary
                il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
            }
        }

        // Emit constructor body
        if (constructor != null)
        {
            ctx.FieldsField = fieldsField;
            ctx.IsInstanceMethod = true;

            // Define parameters
            for (int i = 0; i < constructor.Parameters.Count; i++)
            {
                ctx.DefineParameter(constructor.Parameters[i].Name.Lexeme, i + 1);
            }

            var emitter = new ILEmitter(ctx);

            // Emit default parameter checks (instance method)
            emitter.EmitDefaultParameters(constructor.Parameters, true);

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

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
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
            AsyncMethods = null
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

        // Abstract methods have no body to emit
        if (method.Body != null)
        {
            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
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
