using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Class definition and method emission for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    private void DefineClass(Stmt.Class classStmt)
    {
        var ctx = GetDefinitionContext();

        // Get qualified class name (module-prefixed in multi-module compilation)
        string qualifiedClassName = ctx.GetQualifiedClassName(classStmt.Name.Lexeme);

        // Track simple name -> module mapping for later lookups
        if (_currentModulePath != null)
        {
            _classToModule[classStmt.Name.Lexeme] = _currentModulePath;
        }

        Type? baseType = null;
        if (classStmt.Superclass != null)
        {
            // Resolve superclass name (may need to be qualified)
            string qualifiedSuperclass = ctx.ResolveClassName(classStmt.Superclass.Lexeme);
            if (_classBuilders.TryGetValue(qualifiedSuperclass, out var superBuilder))
            {
                baseType = superBuilder;
            }
        }

        // Set TypeAttributes.Abstract if the class is abstract
        TypeAttributes typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (classStmt.IsAbstract)
        {
            typeAttrs |= TypeAttributes.Abstract;
        }

        var typeBuilder = _moduleBuilder.DefineType(
            qualifiedClassName,
            typeAttrs,
            baseType
        );

        // Track superclass for inheritance-aware method resolution
        string? qualifiedSuperclassName = classStmt.Superclass != null ? ctx.ResolveClassName(classStmt.Superclass.Lexeme) : null;
        _classSuperclass[qualifiedClassName] = qualifiedSuperclassName;

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

            _classGenericParams[qualifiedClassName] = genericParams;
        }

        string className = qualifiedClassName;

        // Initialize property tracking dictionaries for this class
        _propertyBackingFields[className] = [];
        _classProperties[className] = [];
        _declaredPropertyNames[className] = [];
        _readonlyPropertyNames[className] = [];
        _propertyTypes[className] = [];

        // Add _fields dictionary for dynamic property storage
        // Note: We keep this as _fields for now to maintain compatibility with RuntimeEmitter.Objects.cs
        // In Phase 4, both this and the runtime will be updated to use _extras
        var fieldsField = typeBuilder.DefineField(
            "_fields",
            typeof(Dictionary<string, object>),
            FieldAttributes.Private
        );
        _extrasFields[className] = fieldsField;
        _instanceFieldsField[className] = fieldsField;

        // Get class generic params if any
        _classGenericParams.TryGetValue(className, out var classGenericParams);

        // Define real .NET properties with typed backing fields for instance fields
        // Skip fields with generic type parameters - they'll use _extras dictionary instead
        foreach (var field in classStmt.Fields)
        {
            if (!field.IsStatic)
            {
                // Check if field type is a generic parameter
                bool isGenericField = classGenericParams != null &&
                    field.TypeAnnotation != null &&
                    classGenericParams.Any(p => p.Name == field.TypeAnnotation);

                if (!isGenericField)
                {
                    DefineInstanceProperty(typeBuilder, className, field, classGenericParams);
                }
            }
        }

        // Add static fields for static properties (use object type for backward compatibility)
        var staticFieldBuilders = new Dictionary<string, FieldBuilder>();
        foreach (var field in classStmt.Fields)
        {
            if (field.IsStatic)
            {
                // Keep as object type for now to maintain compatibility with existing emission code
                var fieldBuilder = typeBuilder.DefineField(
                    field.Name.Lexeme,
                    typeof(object),
                    FieldAttributes.Public | FieldAttributes.Static
                );
                staticFieldBuilders[field.Name.Lexeme] = fieldBuilder;
            }
        }

        _classBuilders[className] = typeBuilder;
        _staticFields[className] = staticFieldBuilders;

        // Apply class-level decorators as .NET attributes
        if (_decoratorMode != DecoratorMode.None)
        {
            ApplyClassDecorators(classStmt, typeBuilder);
        }
    }

    /// <summary>
    /// Gets the .NET type for a field based on its TypeScript type annotation.
    /// Uses TypeInfo from TypeMap for accurate type resolution (including typed arrays).
    /// </summary>
    /// <param name="field">The field statement</param>
    /// <param name="className">The class name to look up field types from TypeMap</param>
    /// <param name="classGenericParams">Optional generic type parameters for the class</param>
    private Type GetFieldType(Stmt.Field field, string? className = null, GenericTypeParameterBuilder[]? classGenericParams = null)
    {
        if (field.TypeAnnotation == null)
            return typeof(object);

        // Check if the type annotation is a generic type parameter
        if (classGenericParams != null)
        {
            var param = classGenericParams.FirstOrDefault(p => p.Name == field.TypeAnnotation);
            if (param != null)
            {
                return param; // Return the GenericTypeParameterBuilder as the type
            }
        }

        // Try to get typed field info from TypeMap for accurate typing
        if (className != null)
        {
            var classType = _typeMap.GetClassType(className);
            if (classType != null && classType.DeclaredFieldTypes.TryGetValue(field.Name.Lexeme, out var fieldTypeInfo))
            {
                // Skip typed arrays for now - runtime creates List<object> which can't be cast to List<T>
                // Union types, primitives, and classes are safe to type
                if (fieldTypeInfo is not TypeSystem.TypeInfo.Array)
                {
                    return _typeMapper.MapTypeInfoStrict(fieldTypeInfo);
                }
            }
        }

        return TypeMapper.GetClrType(field.TypeAnnotation);
    }

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
    /// Defines a real .NET property with backing field for an instance field.
    /// The property uses typed backing field internally but exposes object-typed
    /// getter/setter for compatibility with existing emission code.
    /// </summary>
    /// <param name="typeBuilder">The type builder for the class</param>
    /// <param name="className">The class name</param>
    /// <param name="field">The field statement</param>
    /// <param name="classGenericParams">Optional generic type parameters for the class</param>
    private void DefineInstanceProperty(TypeBuilder typeBuilder, string className, Stmt.Field field, GenericTypeParameterBuilder[]? classGenericParams = null)
    {
        string fieldName = field.Name.Lexeme;
        Type propertyType = GetFieldType(field, className, classGenericParams);

        // Track as declared property
        _declaredPropertyNames[className].Add(fieldName);
        _propertyTypes[className][fieldName] = propertyType;

        if (field.IsReadonly)
        {
            _readonlyPropertyNames[className].Add(fieldName);
        }

        // Define private backing field with __ prefix to avoid conflicts
        // Uses the actual typed field for efficiency
        var backingField = typeBuilder.DefineField(
            $"__{fieldName}",
            propertyType,
            FieldAttributes.Private
        );
        _propertyBackingFields[className][fieldName] = backingField;

        // Define the property with the actual type (for C# interop)
        var property = typeBuilder.DefineProperty(
            fieldName,
            PropertyAttributes.None,
            propertyType,
            null
        );
        _classProperties[className][fieldName] = property;

        // Define getter method - returns object for compatibility but boxes internally
        // This allows existing code to call the getter and get a boxed value
        var getter = typeBuilder.DefineMethod(
            $"get_{fieldName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            typeof(object),  // Return object for compatibility
            Type.EmptyTypes
        );

        var getterIL = getter.GetILGenerator();
        getterIL.Emit(OpCodes.Ldarg_0);             // this
        getterIL.Emit(OpCodes.Ldfld, backingField); // this.__fieldName (typed value)
        // Box value types and generic type parameters so return is always object
        if (propertyType.IsValueType || propertyType.IsGenericParameter)
        {
            getterIL.Emit(OpCodes.Box, propertyType);
        }
        getterIL.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);

        // Track getter for direct dispatch
        if (!_instanceGetters.TryGetValue(className, out var classGetters))
        {
            classGetters = [];
            _instanceGetters[className] = classGetters;
        }
        classGetters[fieldName] = getter;

        // Define setter method - always create for runtime SetFieldsProperty to find
        // Returns object (the value set) for consistency with existing code
        // For readonly fields: define the setter but don't register it for direct dispatch,
        // so type-checked code won't allow setting, but constructor can via runtime reflection
        {
            var setter = typeBuilder.DefineMethod(
                $"set_{fieldName}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
                typeof(object),  // Return object for consistency (returns the value set)
                [typeof(object)]  // Accept object for compatibility
            );

            var setterIL = setter.GetILGenerator();

            // Store the original value argument for returning later
            setterIL.Emit(OpCodes.Ldarg_1);  // value (object)
            var returnValue = setterIL.DeclareLocal(typeof(object));
            setterIL.Emit(OpCodes.Stloc, returnValue);

            setterIL.Emit(OpCodes.Ldarg_0);  // this
            setterIL.Emit(OpCodes.Ldarg_1);  // value (object)

            // Convert from object to target type
            if (propertyType == typeof(double))
            {
                setterIL.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            }
            else if (propertyType == typeof(bool))
            {
                setterIL.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToBoolean", [typeof(object)])!);
            }
            else if (propertyType == typeof(string))
            {
                setterIL.Emit(OpCodes.Castclass, typeof(string));
            }
            else if (propertyType.IsGenericParameter)
            {
                // For generic type parameters (T), use Unbox_Any which handles both value and reference types
                setterIL.Emit(OpCodes.Unbox_Any, propertyType);
            }
            else if (propertyType.IsValueType)
            {
                setterIL.Emit(OpCodes.Unbox_Any, propertyType);
            }
            else if (propertyType != typeof(object))
            {
                setterIL.Emit(OpCodes.Castclass, propertyType);
            }

            setterIL.Emit(OpCodes.Stfld, backingField);  // this.__fieldName = converted value

            // Return the original value
            setterIL.Emit(OpCodes.Ldloc, returnValue);
            setterIL.Emit(OpCodes.Ret);

            // Only link to PropertyBuilder for non-readonly (C# interop visibility)
            if (!field.IsReadonly)
            {
                property.SetSetMethod(setter);
            }

            // Track setter for direct dispatch ONLY for non-readonly fields
            // This enforces readonly semantics at compile-time while allowing
            // constructor assignment via runtime SetFieldsProperty
            if (!field.IsReadonly)
            {
                if (!_instanceSetters.TryGetValue(className, out var classSetters))
                {
                    classSetters = [];
                    _instanceSetters[className] = classSetters;
                }
                classSetters[fieldName] = setter;
            }
        }
    }

    /// <summary>
    /// Emits IL to convert the value on the stack to the target type.
    /// </summary>
    private static void EmitTypeConversion(ILGenerator il, ILEmitter emitter, Expr source, Type targetType)
    {
        if (targetType == typeof(object))
        {
            // Need to box value types
            emitter.EmitBoxIfNeeded(source);
        }
        else if (targetType == typeof(double))
        {
            // Ensure we have a double on the stack
            emitter.EnsureDouble();
        }
        else if (targetType == typeof(bool))
        {
            // Ensure we have a bool on the stack
            emitter.EnsureBoolean();
        }
        else if (targetType == typeof(string))
        {
            // Strings don't need conversion from string
            // But may need conversion from object
            emitter.EnsureString();
        }
        else if (targetType.IsValueType)
        {
            // For other value types, try to unbox
            emitter.EmitBoxIfNeeded(source);
            il.Emit(OpCodes.Unbox_Any, targetType);
        }
        else
        {
            // Reference types - box if needed then cast
            emitter.EmitBoxIfNeeded(source);
            if (targetType != typeof(object))
            {
                il.Emit(OpCodes.Castclass, targetType);
            }
        }
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
        // Get qualified class name (must match what DefineClass used)
        string qualifiedClassName = GetDefinitionContext().GetQualifiedClassName(classStmt.Name.Lexeme);

        var typeBuilder = _classBuilders[qualifiedClassName];
        var fieldsField = _instanceFieldsField[qualifiedClassName];

        // Initialize static methods dictionary for this class
        if (!_staticMethods.ContainsKey(qualifiedClassName))
        {
            _staticMethods[qualifiedClassName] = new Dictionary<string, MethodBuilder>();
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
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _currentModulePath,
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule
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
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _currentModulePath,
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule
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
        var defCtx = GetDefinitionContext();
        // Use qualified class name to match DefineClass/EmitClassMethods
        string className = defCtx.GetQualifiedClassName(classStmt.Name.Lexeme);

        // Find constructor implementation (with body), not overload signatures
        var constructor = classStmt.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);

        // Reuse pre-defined constructor if available (from DefineClassMethodsOnly)
        ConstructorBuilder ctorBuilder;
        if (_classConstructors.TryGetValue(className, out var existingCtor))
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
            _classConstructors[className] = ctorBuilder;
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
            AsyncMethods = null,
            // Typed interop support
            PropertyBackingFields = _propertyBackingFields,
            ClassProperties = _classProperties,
            DeclaredPropertyNames = _declaredPropertyNames,
            ReadonlyPropertyNames = _readonlyPropertyNames,
            PropertyTypes = _propertyTypes,
            ExtrasFields = _extrasFields,
            UnionGenerator = _unionGenerator,
            // Module support for multi-module compilation
            CurrentModulePath = _currentModulePath,
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule
        };

        // Add class generic type parameters to context
        if (_classGenericParams.TryGetValue(className, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Initialize _extras dictionary FIRST (before calling parent constructor)
        // This allows parent constructor to access fields via SetFieldsProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);
        il.Emit(OpCodes.Stfld, fieldsField);

        // Call parent constructor
        // If the class has an explicit constructor with super(), the super() in body will handle it.
        // If the class has no explicit constructor but has a superclass, we must call the parent constructor.
        // If the class has no superclass, we call Object constructor.
        string? qualifiedSuperclass = classStmt.Superclass != null ? defCtx.ResolveClassName(classStmt.Superclass.Lexeme) : null;
        if (constructor == null && qualifiedSuperclass != null && _classConstructors.TryGetValue(qualifiedSuperclass, out var parentCtor))
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

                // Check if this is a declared property with a backing field
                if (_propertyBackingFields.TryGetValue(className, out var backingFields) &&
                    backingFields.TryGetValue(fieldName, out var backingField))
                {
                    // Store directly in backing field
                    il.Emit(OpCodes.Ldarg_0);  // this

                    // Emit initializer expression
                    initEmitter.EmitExpression(field.Initializer!);

                    // Convert to proper type if needed
                    Type targetType = _propertyTypes[className][fieldName];
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
