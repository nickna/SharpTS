using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    /// <summary>
    /// Defines types for all collected class expressions.
    /// Class expressions are collected during arrow function collection phase.
    /// </summary>
    private void DefineClassExpressionTypes()
    {
        foreach (var classExpr in _classExprs.ToDefine)
        {
            if (!_classExprs.Builders.ContainsKey(classExpr))
                DefineClassExpression(classExpr);
        }
    }

    /// <summary>
    /// Defines a single class expression type with typed properties, generics, and inheritance support.
    /// Method bodies are emitted later in EmitClassExpressionBodies.
    /// </summary>
    private void DefineClassExpression(Expr.ClassExpr classExpr)
    {
        string className = _classExprs.Names[classExpr];

        // Create TypeBuilder with appropriate attributes
        // Note: We create without parent initially, set it after defining generic params
        TypeAttributes typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (classExpr.IsAbstract)
        {
            typeAttrs |= TypeAttributes.Abstract;
        }

        var typeBuilder = _moduleBuilder.DefineType(
            className,
            typeAttrs
        );

        var lexicalCaptures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in classExpr.Methods)
            lexicalCaptures.UnionWith(_closures.Analyzer.GetCaptures(method));
        if (classExpr.Name != null)
            lexicalCaptures.Remove(classExpr.Name.Lexeme);
        var captureFields = new Dictionary<string, FieldBuilder>(StringComparer.Ordinal);
        foreach (var capture in lexicalCaptures)
        {
            captureFields[capture] = typeBuilder.DefineField(
                $"$lex_{capture}", _types.Object,
                FieldAttributes.Public | FieldAttributes.Static);
        }
        _classExprs.CaptureFields[classExpr] = captureFields;

        // Track superclass name for inheritance resolution
        string? superclassName = Expr.GetSuperclassLeafName(classExpr.SuperclassExpr);
        _classExprs.Superclass[classExpr] = superclassName;

        // Handle generic type parameters FIRST (before resolving superclass type args)
        GenericTypeParameterBuilder[]? classGenericParams = null;
        if (classExpr.TypeParams != null && classExpr.TypeParams.Count > 0)
        {
            string[] typeParamNames = classExpr.TypeParams.Select(tp => tp.Name.Lexeme).ToArray();
            classGenericParams = typeBuilder.DefineGenericParameters(typeParamNames);

            // Apply constraints
            for (int i = 0; i < classExpr.TypeParams.Count; i++)
            {
                var constraint = classExpr.TypeParams[i].Constraint;
                if (constraint != null)
                {
                    Type constraintType = ResolveConstraintType(constraint);
                    if (constraintType.IsInterface)
                        classGenericParams[i].SetInterfaceConstraints(constraintType);
                    else
                        EmitTypeDefinitions.SetBaseTypeConstraint(classGenericParams[i], constraintType);
                }
            }

            _classExprs.GenericParams[classExpr] = classGenericParams;
        }

        // NOW resolve superclass (may use our generic params for type arguments)
        Type? baseType = null;
        if (superclassName != null)
        {
            // Check class declarations first (with module resolution)
            var resolvedSuperName = GetDefinitionContext().ResolveClassName(superclassName);
            TypeBuilder? superTypeBuilder = null;

            if (_classes.Builders.TryGetValue(resolvedSuperName, out superTypeBuilder))
            {
                // Found in class declarations
            }
            else if (_classExprs.VarToClassExpr.TryGetValue(superclassName, out var parentClassExpr)
                     && _classExprs.Builders.TryGetValue(parentClassExpr, out var parentExprBuilder))
            {
                // Superclass is another class expression bound to a variable
                // (e.g. `const A = class {}; const B = class extends A {}`).
                // _classExprs.Names holds GENERATED names ($ClassExpr_N), so the
                // by-generated-name scan below never matches the source variable
                // name — without this, B's parent defaults to System.Object while
                // super() still chains A..ctor, tripping ILVerify CallCtor /
                // ThisUninitReturn (#287 family).
                superTypeBuilder = parentExprBuilder;
            }
            else
            {
                // Check other class expressions by their generated name
                foreach (var (expr, name) in _classExprs.Names)
                {
                    if (name == superclassName && _classExprs.Builders.TryGetValue(expr, out var superExprBuilder))
                    {
                        superTypeBuilder = superExprBuilder;
                        break;
                    }
                }
            }

            if (superTypeBuilder != null)
            {
                // Check for type arguments
                if (classExpr.SuperclassTypeArgs != null && classExpr.SuperclassTypeArgs.Count > 0)
                {
                    var typeArgs = ResolveSuperclassTypeArguments(
                        classExpr.SuperclassTypeArgs,
                        classGenericParams,
                        classExpr.TypeParams);
                    baseType = _types.MakeGenericType(superTypeBuilder, typeArgs);
                }
                else
                {
                    baseType = superTypeBuilder;
                }
            }
            else if (Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(superclassName))
            {
                baseType = GetEmittedErrorType(superclassName);
                _classes.ErrorSubclasses.Add(className);
            }
            else if (superclassName == "Array")
            {
                baseType = _runtime.TSArrayType;
            }
            else if (superclassName == "Promise")
            {
                baseType = _runtime.TSPromiseType;
            }
        }

        // Set the parent type
        if (baseType != null)
        {
            EmitTypeDefinitions.SetParent(typeBuilder, baseType);
        }

        // Implement $IHasFields interface for unified property access
        // All classes implement this interface (including derived classes)
        EmitTypeDefinitions.AddInterfaceImplementation(typeBuilder, _runtime.IHasFieldsInterface);

        // Initialize tracking dictionaries for this class expression
        _classExprs.BackingFields[classExpr] = [];
        _classExprs.Properties[classExpr] = [];
        _classExprs.PropertyTypes[classExpr] = [];
        _classExprs.DeclaredProperties[classExpr] = [];
        _classExprs.ReadonlyProperties[classExpr] = [];
        _classExprs.StaticFields[classExpr] = [];
        _classExprs.StaticMethods[classExpr] = [];
        _classExprs.InstanceMethods[classExpr] = [];
        _classExprs.Getters[classExpr] = [];
        _classExprs.Setters[classExpr] = [];

        // Add _fields dictionary for dynamic property storage (extras)
        var fieldsField = typeBuilder.DefineField(
            "_fields",
            typeof(Dictionary<string, object>),
            FieldAttributes.Private
        );
        _typedInterop.ExtrasFields[className] = fieldsField;
        _classes.InstanceFieldsField[className] = fieldsField;

        // Define typed instance properties
        // Skip declare fields - they use _fields dictionary to support TypeScript null semantics.
        // Skip generic-parameter-typed fields - a backing field of type `T` (an open generic
        // parameter) cannot be widened to / narrowed from the object-typed $IHasFields slots
        // without box/unbox of `T`, which yields unverifiable IL. Like class declarations
        // (ILCompiler.Classes.cs), these fields live in the `_fields` dictionary instead (#291).
        foreach (var field in classExpr.Fields.Where(f => !f.IsStatic && !f.IsDeclare))
        {
            bool isGenericField = classGenericParams != null &&
                field.TypeAnnotation != null &&
                classGenericParams.Any(p => p.Name == field.TypeAnnotation);
            if (isGenericField)
                continue;
            DefineClassExpressionProperty(typeBuilder, classExpr, field, classGenericParams);
        }

        // Define static fields (use object type for compatibility with existing emission code)
        foreach (var field in classExpr.Fields.Where(f => f.IsStatic))
        {
            var staticField = typeBuilder.DefineField(
                field.Name.Lexeme,
                typeof(object),  // Keep as object type like class declarations
                FieldAttributes.Public | FieldAttributes.Static
            );
            _classExprs.StaticFields[classExpr][field.Name.Lexeme] = staticField;
        }

        // Define $IHasFields interface method stubs
        // Bodies are emitted later in EmitClassExpressionBody after method definitions are available
        DefineHasFieldsInterfaceMethods(typeBuilder, className, fieldsField);

        // Store the type builder
        _classExprs.Builders[classExpr] = typeBuilder;
    }

    /// <summary>
    /// Defines a typed .NET property with backing field for a class expression field.
    /// </summary>
    private void DefineClassExpressionProperty(
        TypeBuilder typeBuilder,
        Expr.ClassExpr classExpr,
        Stmt.Field field,
        GenericTypeParameterBuilder[]? classGenericParams)
    {
        string fieldName = field.Name.Lexeme;
        string pascalName = NamingConventions.ToPascalCase(fieldName);
        Type propertyType = GetClassExprFieldType(classExpr, field, classGenericParams);

        // Track as declared property
        _classExprs.DeclaredProperties[classExpr].Add(pascalName);
        _classExprs.PropertyTypes[classExpr][pascalName] = propertyType;

        if (field.IsReadonly)
        {
            _classExprs.ReadonlyProperties[classExpr].Add(pascalName);
        }

        // Define private backing field
        var backingField = typeBuilder.DefineField(
            $"__{pascalName}",
            propertyType,
            FieldAttributes.Private
        );
        _classExprs.BackingFields[classExpr][pascalName] = backingField;

        // Define the property
        var property = typeBuilder.DefineProperty(
            pascalName,
            PropertyAttributes.None,
            propertyType,
            null
        );
        _classExprs.Properties[classExpr][pascalName] = property;

        // Define getter
        var getter = typeBuilder.DefineMethod(
            $"get_{pascalName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            propertyType,
            Type.EmptyTypes
        );
        var getterIL = getter.GetILGenerator();
        getterIL.Emit(OpCodes.Ldarg_0);
        getterIL.Emit(OpCodes.Ldfld, backingField);
        getterIL.Emit(OpCodes.Ret);
        property.SetGetMethod(getter);
        _classExprs.Getters[classExpr][pascalName] = getter;

        // Define setter (always needed for constructor initialization)
        var setter = typeBuilder.DefineMethod(
            $"set_{pascalName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            typeof(void),
            [propertyType]
        );
        var setterIL = setter.GetILGenerator();
        setterIL.Emit(OpCodes.Ldarg_0);
        setterIL.Emit(OpCodes.Ldarg_1);
        setterIL.Emit(OpCodes.Stfld, backingField);
        setterIL.Emit(OpCodes.Ret);

        // Only link setter to property for non-readonly
        if (!field.IsReadonly)
        {
            property.SetSetMethod(setter);
            _classExprs.Setters[classExpr][pascalName] = setter;
        }
    }

    /// <summary>
    /// Gets the .NET type for a class expression field.
    /// </summary>
    private Type GetClassExprFieldType(
        Expr.ClassExpr classExpr,
        Stmt.Field field,
        GenericTypeParameterBuilder[]? classGenericParams)
    {
        if (field.TypeAnnotation == null)
            return typeof(object);

        // Check generic type parameters
        if (classGenericParams != null)
        {
            var param = classGenericParams.FirstOrDefault(p => p.Name == field.TypeAnnotation);
            if (param != null)
                return param;
        }

        return TypeMapper.GetClrType(field.TypeAnnotation);
    }

    /// <summary>
    /// Defines method signatures for all class expressions.
    /// Called after DefineClassExpressionTypes.
    /// </summary>
    private void DefineClassExpressionMethods()
    {
        foreach (var classExpr in _classExprs.ToDefine)
        {
            DefineClassExpressionMethodSignatures(classExpr);
        }
    }

    /// <summary>
    /// Defines method and constructor signatures for a class expression.
    /// </summary>
    private void DefineClassExpressionMethodSignatures(Expr.ClassExpr classExpr)
    {
        if (!_classExprs.Builders.TryGetValue(classExpr, out var typeBuilder))
            return;

        string className = _classExprs.Names[classExpr];

        // Find user-defined constructor or use default
        var constructor = classExpr.Methods.FirstOrDefault(m => !m.IsStatic && m.Name.Lexeme == "constructor" && m.Body != null);
        var superclassName = _classExprs.Superclass.GetValueOrDefault(classExpr);
        var ctorParamTypes = constructor?.Parameters.Select(_ => typeof(object)).ToArray()
            ?? (superclassName == "Array"
                ? [typeof(object[])]
                : Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(superclassName ?? "")
                    ? superclassName == "AggregateError"
                        ? [typeof(object), typeof(object)]
                        : [typeof(object)]
                    : superclassName == "Promise"
                        ? [typeof(object)]
                        : []);

        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            ctorParamTypes
        );
        if (constructor == null && superclassName == "Array")
            MarkJsVariadicConstructor(ctorBuilder);
        _classExprs.Constructors[classExpr] = ctorBuilder;
        _classes.Constructors[className] = ctorBuilder;
        RegisterArgumentsCapturingMethod(ctorBuilder, constructor?.Body);
        DefineClassPrototypeConstructor(typeBuilder);

        // Define static methods (computed symbol-keyed methods are handled by
        // DefineClassExpressionSymbolMethods below, like the class-declaration path).
        foreach (var method in classExpr.Methods.Where(m =>
                     m.Body != null && m.IsStatic && !m.IsPrivate && m.ComputedKey == null))
        {
            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();
            // Match the method kind to its state machine's return type (#765), mirroring
            // DefineClassMethodsOnly. Async generator FIRST since it has both flags set.
            Type returnType = (method.IsAsync && method.IsGenerator) ? _types.IAsyncEnumerableOfObject :
                              method.IsAsync ? _types.TaskOfObject :
                              method.IsGenerator ? _types.IEnumerableOfObject :
                              typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                MethodAttributes.Public | MethodAttributes.Static,
                returnType,
                paramTypes
            );
            _classExprs.StaticMethods[classExpr][method.Name.Lexeme] = methodBuilder;
            RegisterArgumentsCapturingMethod(methodBuilder, method.Body);
        }

        // Define instance methods (computed symbol-keyed methods are handled by
        // DefineClassExpressionSymbolMethods below, like the class-declaration path).
        foreach (var method in classExpr.Methods.Where(m =>
                     m.Body != null && !m.IsStatic && !m.IsPrivate &&
                     m.Name.Lexeme != "constructor" && m.ComputedKey == null))
        {
            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
            if (method.IsAbstract)
                methodAttrs |= MethodAttributes.Abstract;

            // Match the method kind to its state machine's return type (#765), mirroring
            // DefineClassMethodsOnly. Async generator FIRST since it has both flags set.
            Type returnType = (method.IsAsync && method.IsGenerator) ? _types.IAsyncEnumerableOfObject :
                              method.IsAsync ? typeof(Task<object>) :
                              method.IsGenerator ? _types.IEnumerableOfObject :
                              typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                methodAttrs,
                returnType,
                paramTypes
            );
            _classExprs.InstanceMethods[classExpr][method.Name.Lexeme] = methodBuilder;
            RegisterArgumentsCapturingMethod(methodBuilder, method.Body);
        }

        // Computed symbol-keyed methods (`*[Symbol.iterator]()` etc.) get synthetic uniquely-named
        // builders plus runtime symbol-method registration, mirroring the class-declaration path (#755).
        DefineClassExpressionSymbolMethods(classExpr, typeBuilder);

        // Define user-defined accessors (overrides property accessors)
        if (classExpr.Accessors != null)
        {
            foreach (var accessor in classExpr.Accessors)
            {
                // Symbol-keyed computed accessor (#281): define a synthetic
                // $sym_get/set_N method recorded in _classes.SymbolAccessors keyed
                // by this class's generated name. It is registered in the class-
                // expression .cctor and dispatched through the $Runtime symbol-
                // accessor registry, mirroring the class-declaration path (#266).
                if (accessor.ComputedKey != null)
                {
                    DefineSymbolAccessorMethod(typeBuilder, accessor);
                    continue;
                }
                string accessorName = accessor.Name.Lexeme;
                string pascalName = NamingConventions.ToPascalCase(accessorName);
                string methodName = accessor.Kind.Type == TokenType.GET
                    ? $"get_{pascalName}"
                    : $"set_{pascalName}";

                Type[] paramTypes = accessor.Kind.Type == TokenType.SET
                    ? [typeof(object)]
                    : [];

                MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.SpecialName |
                                              MethodAttributes.HideBySig | MethodAttributes.Virtual;
                if (accessor.IsAbstract)
                    methodAttrs |= MethodAttributes.Abstract;

                var methodBuilder = typeBuilder.DefineMethod(
                    methodName,
                    methodAttrs,
                    typeof(object),
                    paramTypes
                );

                if (accessor.Kind.Type == TokenType.GET)
                    _classExprs.Getters[classExpr][pascalName] = methodBuilder;
                else
                    _classExprs.Setters[classExpr][pascalName] = methodBuilder;
            }
        }
    }

    /// <summary>
    /// Emits method bodies for all class expressions.
    /// Called after class declaration method emission.
    /// </summary>
    private void EmitClassExpressionBodies()
    {
        foreach (var classExpr in _classExprs.ToDefine)
        {
            EmitClassExpressionBody(classExpr);
        }
    }

    /// <summary>
    /// Emits all method bodies for a single class expression.
    /// </summary>
    private void EmitClassExpressionBody(Expr.ClassExpr classExpr)
    {
        if (!_classExprs.Builders.TryGetValue(classExpr, out var typeBuilder))
            return;

        string className = _classExprs.Names[classExpr];
        var fieldsField = _classes.InstanceFieldsField[className];

        // Emit the constructor-free prototype path before the .cctor that uses it.
        EmitClassPrototypeConstructor(typeBuilder);

        // Emit static constructor and prototype registration.
        EmitClassExpressionStaticConstructor(classExpr, typeBuilder);

        // Emit instance constructor
        EmitClassExpressionConstructor(classExpr, typeBuilder, fieldsField);

        // Emit instance method bodies (computed symbol-keyed methods are emitted below).
        foreach (var method in classExpr.Methods.Where(m =>
                     m.Body != null && !m.IsStatic && !m.IsPrivate &&
                     m.Name.Lexeme != "constructor" && m.ComputedKey == null))
        {
            EmitClassExpressionMethod(classExpr, typeBuilder, method, fieldsField);
        }

        // Emit static method bodies (computed symbol-keyed methods are emitted below).
        foreach (var method in classExpr.Methods.Where(m =>
                     m.Body != null && m.IsStatic && !m.IsPrivate && m.ComputedKey == null))
        {
            EmitClassExpressionStaticMethodBody(classExpr, method);
        }

        // Emit computed symbol-keyed method bodies (#755), then their runtime registration runs in
        // the class-expression .cctor (EmitClassExpressionStaticConstructor → EmitSymbolMethodRegistrations).
        EmitClassExpressionSymbolMethods(classExpr, typeBuilder, fieldsField);

        // Emit user-defined accessor bodies
        if (classExpr.Accessors != null)
        {
            foreach (var accessor in classExpr.Accessors)
            {
                // Symbol-keyed accessors are emitted below from _classes.SymbolAccessors
                // (their synthetic methods aren't in _classExprs.Getters/Setters).
                if (!accessor.IsAbstract && accessor.ComputedKey == null)
                {
                    EmitClassExpressionAccessor(classExpr, typeBuilder, accessor, fieldsField);
                }
            }
        }

        // Emit symbol-keyed computed accessor bodies (#281)
        EmitClassExpressionSymbolAccessors(classExpr, typeBuilder, fieldsField);

        // Emit $IHasFields interface method bodies (now that method builders are available)
        EmitHasFieldsInterfaceMethodBodies(className, classExpr);
    }

    /// <summary>
    /// Creates a CompilationContext for class expression method emission.
    /// </summary>
    private CompilationContext CreateClassExpressionContext(
        ILGenerator il,
        Expr.ClassExpr classExpr,
        TypeBuilder typeBuilder,
        FieldInfo? fieldsField,
        System.Reflection.MethodBase? method = null)
    {
        string className = _classExprs.Names[classExpr];

        var ctx = CreateModuleMemberContext(il, method);
        ctx.IsStrictMode = true;
        ctx.FieldsField = fieldsField;
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.EmittingTypeBuilder = typeBuilder;
        ctx.CurrentClassName = className;
        if (_classExprs.EnclosingClass.TryGetValue(classExpr, out var enclosingClassName))
            ctx.EnclosingClassNames = [enclosingClassName];
        ctx.CurrentSuperclassName = _classExprs.Superclass.GetValueOrDefault(classExpr);
        ctx.PropertyBackingFields = _typedInterop.PropertyBackingFields;
        ctx.ClassProperties = _typedInterop.ClassProperties;
        ctx.DeclaredPropertyNames = _typedInterop.DeclaredPropertyNames;
        ctx.ReadonlyPropertyNames = _typedInterop.ReadonlyPropertyNames;
        ctx.PropertyTypes = _typedInterop.PropertyTypes;
        ctx.ExtrasFields = _typedInterop.ExtrasFields;
        ctx.UnionGenerator = _unionGenerator;
        ctx.ClassExprBackingFields = _classExprs.BackingFields;
        ctx.ClassExprProperties = _classExprs.Properties;
        ctx.ClassExprPropertyTypes = _classExprs.PropertyTypes;
        ctx.ClassExprDeclaredProperties = _classExprs.DeclaredProperties;
        ctx.ClassExprReadonlyProperties = _classExprs.ReadonlyProperties;
        ctx.ClassExprStaticFields = _classExprs.StaticFields;
        ctx.ClassExprStaticMethods = _classExprs.StaticMethods;
        ctx.ClassExprInstanceMethods = _classExprs.InstanceMethods;
        ctx.ClassExprGetters = _classExprs.Getters;
        ctx.ClassExprSetters = _classExprs.Setters;
        ctx.ClassExprConstructors = _classExprs.Constructors;
        ctx.ClassExprGenericParams = _classExprs.GenericParams;
        ctx.ClassExprSuperclass = _classExprs.Superclass;
        ctx.CurrentClassExpr = classExpr;
        ctx.VarToClassExpr = _classExprs.VarToClassExpr;
        // Module-level / captured top-level variable access — without these a
        // class-expression method or accessor body referencing a top-level
        // binding throws ReferenceError at runtime (same omission #300 fixed
        // for class-declaration accessor bodies).
        ApplyCapturedTopLevelVariableAccess(ctx, memberBodyExports: true);
        if (_classExprs.CaptureFields.TryGetValue(classExpr, out var captureFields)
            && captureFields.Count > 0)
        {
            ctx.TopLevelStaticVars = ctx.TopLevelStaticVars == null
                ? new Dictionary<string, FieldBuilder>(captureFields, StringComparer.Ordinal)
                : new Dictionary<string, FieldBuilder>(ctx.TopLevelStaticVars, StringComparer.Ordinal);
            foreach (var (name, field) in captureFields)
                ctx.TopLevelStaticVars[name] = field;
        }
        return ctx;
    }

    /// <summary>
    /// Emits static constructor for class expression static field initializers and static blocks.
    /// </summary>
    private void EmitClassExpressionStaticConstructor(Expr.ClassExpr classExpr, TypeBuilder typeBuilder)
    {
        bool hasStaticFields = classExpr.Fields.Any(f => f.IsStatic && f.Initializer != null);
        bool hasStaticInitializers = classExpr.StaticInitializers?.Count > 0;
        // Symbol-keyed computed accessors (#281) and methods (#755) register in the .cctor, keyed by
        // this class's generated name (mirrors the class-declaration path #266/#647).
        bool hasSymbolAccessors = _classes.SymbolAccessors.ContainsKey(typeBuilder.Name);
        bool hasSymbolMethods = _classes.SymbolMethods.ContainsKey(typeBuilder.Name);

        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, null, cctor);
        ctx.IsStaticConstructorContext = true;
        var emitter = new ILEmitter(ctx);

        EmitClassPrototypeRegistration(
            il, typeBuilder, GetClassConstructorLength(classExpr.Methods));

        // Process StaticInitializers if available (preserves declaration order)
        if (hasStaticInitializers)
        {
            foreach (var initializer in classExpr.StaticInitializers!)
            {
                switch (initializer)
                {
                    case Stmt.Field field when field.IsStatic && field.Initializer != null:
                        var staticField = _classExprs.StaticFields[classExpr][field.Name.Lexeme];
                        emitter.EmitExpression(field.Initializer);
                        if (staticField.FieldType == typeof(object))
                            emitter.EmitBoxIfNeeded(field.Initializer);
                        il.Emit(OpCodes.Stsfld, staticField);
                        break;

                    case Stmt.StaticBlock block:
                        foreach (var stmt in block.Body)
                            emitter.EmitStatement(stmt);
                        break;
                }
            }
        }
        else
        {
            // Fallback for backward compatibility (fields only)
            foreach (var field in classExpr.Fields.Where(f => f.IsStatic && f.Initializer != null))
            {
                var staticField = _classExprs.StaticFields[classExpr][field.Name.Lexeme];
                emitter.EmitExpression(field.Initializer!);
                if (staticField.FieldType == typeof(object))
                    emitter.EmitBoxIfNeeded(field.Initializer!);
                il.Emit(OpCodes.Stsfld, staticField);
            }
        }

        // Register symbol-keyed computed accessors (#281) and methods (#755) in the runtime registry,
        // keyed by this class's Type so dynamic bracket get / for...of dispatch can find them.
        EmitSymbolAccessorRegistrations(emitter, il, typeBuilder);
        EmitSymbolMethodRegistrations(emitter, il, typeBuilder);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits instance constructor for a class expression.
    /// </summary>
    private void EmitClassExpressionConstructor(Expr.ClassExpr classExpr, TypeBuilder typeBuilder, FieldInfo fieldsField)
    {
        string className = _classExprs.Names[classExpr];
        var ctorBuilder = _classExprs.Constructors[classExpr];
        var constructor = classExpr.Methods.FirstOrDefault(m => !m.IsStatic && m.Name.Lexeme == "constructor" && m.Body != null);

        var il = ctorBuilder.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, fieldsField, ctorBuilder);
        ctx.IsInstanceMethod = true;

        // Add generic type parameters to context
        if (_classExprs.GenericParams.TryGetValue(classExpr, out var genericParams))
        {
            foreach (var gp in genericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Dynamic property storage is materialized only by the first expando write.
        // This is also safe for base-constructor virtual dispatch because SetProperty
        // calls the derived type's ensure helper before touching its private field.
        var ensureFields = _classes.HasFieldsStubs[className].EnsureFields;

        // Emit constructor body first if present (contains super() call)
        var emitter = new ILEmitter(ctx);

        // Determine if we need to call base constructor automatically
        // If there's an explicit constructor body, it should contain super() call
        bool hasExplicitSuperCall = constructor?.Body?.Any(stmt => ContainsSuperCall(stmt)) ?? false;
        bool initializersEmitted = false;

        if (!hasExplicitSuperCall)
        {
            // No explicit super() - call parent constructor automatically.
            il.Emit(OpCodes.Ldarg_0);

            // When the superclass is another emitted class (declaration or
            // expression), its base is a TypeBuilder — Type.GetConstructor is
            // not supported on an unbaked TypeBuilder, so resolve the parent's
            // ConstructorBuilder from the registries instead (#287 family). The
            // implicit derived constructor forwards no args, so supply defaults
            // for any base ctor parameters (parameterless in the common case).
            var superclassName = _classExprs.Superclass.GetValueOrDefault(classExpr);
            if (superclassName == "Array")
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, _runtime.TSArrayCtorFromCtorArgs);
            }
            else if (superclassName == "Promise")
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, _runtime.PromiseFromExecutor);
                il.Emit(OpCodes.Call, _runtime.TSPromiseCtor);
            }
            else if (Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(superclassName ?? ""))
            {
                bool aggregate = superclassName == "AggregateError";
                var messageLocal = il.DeclareLocal(_types.String);
                var hasMessageLocal = il.DeclareLocal(_types.Boolean);
                var convertMessage = il.DefineLabel();
                var haveMessage = il.DefineLabel();
                il.Emit(aggregate ? OpCodes.Ldarg_2 : OpCodes.Ldarg_1);
                il.Emit(OpCodes.Isinst, _runtime.UndefinedType);
                il.Emit(OpCodes.Brfalse, convertMessage);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stloc, hasMessageLocal);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Br, haveMessage);
                il.MarkLabel(convertMessage);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Stloc, hasMessageLocal);
                il.Emit(aggregate ? OpCodes.Ldarg_2 : OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, _runtime.ToJsString);
                il.MarkLabel(haveMessage);
                il.Emit(OpCodes.Stloc, messageLocal);

                if (aggregate)
                    il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloc, messageLocal);
                il.Emit(OpCodes.Call, GetEmittedErrorConstructor(superclassName!));

                var skipMessageDescriptor = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, hasMessageLocal);
                il.Emit(OpCodes.Brfalse, skipMessageDescriptor);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, messageLocal);
                il.Emit(OpCodes.Call, _runtime.ErrorDefineMessageProperty);
                il.MarkLabel(skipMessageDescriptor);
            }
            else if (ResolveClassExprImplicitBaseConstructor(classExpr) is { } baseCtor)
            {
                foreach (var p in baseCtor.GetParameters())
                    emitter.EmitOmittedArgument(p.ParameterType);
                il.Emit(OpCodes.Call, baseCtor);
            }
            else
            {
                Type baseType = typeBuilder.BaseType ?? typeof(object);
                var fallbackCtor = _types.TryGetConstructor(baseType) ?? _types.ObjectDefaultCtor;
                il.Emit(OpCodes.Call, fallbackCtor);
            }
        }
        if (constructor != null)
        {
            // Define parameters with typed parameter types from constructor signature
            var ctorParams = ctorBuilder.GetParameters();
            for (int i = 0; i < constructor.Parameters.Count; i++)
            {
                Type? paramType = i < ctorParams.Length ? ctorParams[i].ParameterType : null;
                ctx.DefineParameter(constructor.Parameters[i].Name.Lexeme, i + 1, paramType);
            }

            var constructorParamTypes = ctorBuilder.GetParameters().Select(p => p.ParameterType).ToArray();
            EmitFunctionEnvironmentPrologue(
                il,
                ctx,
                emitter,
                constructor.Parameters,
                constructor.Body,
                constructorParamTypes,
                argumentOffset: 1);

            // Base-class fields are initialized before the constructor body. Derived-class
            // fields are initialized immediately after the explicit super() establishes this.
            if (!hasExplicitSuperCall)
            {
                EmitInstanceFieldInitializers();
                initializersEmitted = true;
            }

            if (constructor.Body != null)
            {
                foreach (var stmt in constructor.Body)
                {
                    emitter.EmitStatement(stmt);
                    if (!initializersEmitted && ContainsSuperCall(stmt))
                    {
                        EmitInstanceFieldInitializers();
                        initializersEmitted = true;
                    }
                }
            }
        }

        if (!initializersEmitted)
            EmitInstanceFieldInitializers();

        emitter.FinalizeReturns();

        il.Emit(OpCodes.Ret);

        void EmitInstanceFieldInitializers()
        {
            foreach (var field in classExpr.Fields.Where(f => !f.IsStatic && f.Initializer != null))
            {
                string fieldName = field.Name.Lexeme;
                string pascalName = NamingConventions.ToPascalCase(fieldName);

                if (_classExprs.BackingFields[classExpr].TryGetValue(pascalName, out var backingField))
                {
                    // Store in backing field
                    il.Emit(OpCodes.Ldarg_0);
                    emitter.EmitExpression(field.Initializer!);

                    Type targetType = _classExprs.PropertyTypes[classExpr][pascalName];
                    EmitTypeConversion(il, emitter, field.Initializer!, targetType);

                    il.Emit(OpCodes.Stfld, backingField);
                }
                else
                {
                    // Fallback to _fields dictionary
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(ensureFields));
                    il.Emit(OpCodes.Ldstr, fieldName);
                    emitter.EmitExpression(field.Initializer!);
                    emitter.EmitBoxIfNeeded(field.Initializer!);
                    il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
                }
            }

            // Initialize instance declare fields (without initializers) to null in _fields dictionary
            // TypeScript semantics: uninitialized fields return null/undefined, not CLR defaults
            foreach (var field in classExpr.Fields.Where(f =>
                !f.IsStatic && !f.IsPrivate && f.IsDeclare && f.Initializer == null && f.ComputedKey == null))
            {
                string fieldName = field.Name.Lexeme;
                // Store null in _fields dictionary
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(ensureFields));
                il.Emit(OpCodes.Ldstr, fieldName);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
            }
        }
    }

    /// <summary>
    /// Resolves the parent constructor for a class expression's implicit
    /// (no explicit super()) base-constructor call. The parent may be another
    /// class expression bound to a variable or a class declaration; in both
    /// cases the parent's base type is an unbaked TypeBuilder for which
    /// Type.GetConstructor throws, so the ConstructorBuilder is pulled from the
    /// emit-time registries instead. Returns null when the superclass is a
    /// baked/built-in type (caller falls back to reflection).
    /// </summary>
    private System.Reflection.ConstructorInfo? ResolveClassExprImplicitBaseConstructor(Expr.ClassExpr classExpr)
    {
        if (!_classExprs.Superclass.TryGetValue(classExpr, out var superName) || superName == null)
            return null;

        // Parent is another class expression bound to a variable.
        if (_classExprs.VarToClassExpr.TryGetValue(superName, out var parentExpr)
            && _classExprs.Constructors.TryGetValue(parentExpr, out var exprCtor))
            return exprCtor;

        // Parent is a class declaration.
        var resolved = GetDefinitionContext().ResolveClassName(superName);
        if (_classes.Constructors.TryGetValue(resolved, out var declCtor))
            return declCtor;

        return null;
    }

    /// <summary>
    /// Checks if a statement contains a super() call.
    /// </summary>
    private static bool ContainsSuperCall(Stmt stmt)
    {
        return stmt switch
        {
            Stmt.Expression expr => ContainsSuperCallInExpr(expr.Expr),
            Stmt.Block block => block.Statements.Any(ContainsSuperCall),
            Stmt.If ifStmt => ContainsSuperCallInExpr(ifStmt.Condition) ||
                              ContainsSuperCall(ifStmt.ThenBranch) ||
                              (ifStmt.ElseBranch != null && ContainsSuperCall(ifStmt.ElseBranch)),
            _ => false
        };
    }

    /// <summary>
    /// Checks if an expression contains a super() call.
    /// </summary>
    private static bool ContainsSuperCallInExpr(Expr expr)
    {
        return expr switch
        {
            Expr.Call call => call.Callee is Expr.Super,
            Expr.Binary bin => ContainsSuperCallInExpr(bin.Left) || ContainsSuperCallInExpr(bin.Right),
            Expr.Logical log => ContainsSuperCallInExpr(log.Left) || ContainsSuperCallInExpr(log.Right),
            Expr.Grouping grp => ContainsSuperCallInExpr(grp.Expression),
            _ => false
        };
    }

    /// <summary>
    /// Emits an instance method body for a class expression.
    /// </summary>
    private void EmitClassExpressionMethod(
        Expr.ClassExpr classExpr,
        TypeBuilder typeBuilder,
        Stmt.Function method,
        FieldInfo fieldsField)
    {
        if (method.IsAbstract) return;

        if (!_classExprs.InstanceMethods[classExpr].TryGetValue(method.Name.Lexeme, out var methodBuilder))
            return;

        // #703: class-expression instance method invoked as a value pads omitted optional
        // args with the `undefined` sentinel on the value-call path (covers sync + async).
        MarkPadsUndefined(methodBuilder);
        MarkFunctionLength(methodBuilder, method.Parameters);

        // Generator methods route through the same state-machine emitters as class declarations (#765).
        // Async generator FIRST since `async *m()` has both IsAsync and IsGenerator set.
        if (method.IsAsync && method.IsGenerator)
        {
            EmitAsyncGeneratorMethodBody(methodBuilder, method, fieldsField);
            return;
        }

        // Async (non-generator) methods route through the same async state-machine emitter as
        // class declarations (#776), mirroring the async-generator line above. The earlier stub
        // emitted the body synchronously and returned Task.FromResult(null), so `return <value>`
        // produced a raw value (breaking `.then`) and an `await` in the body emitted invalid IL.
        if (method.IsAsync)
        {
            EmitAsyncMethodBody(methodBuilder, method, fieldsField,
                currentClassName: _classExprs.Names[classExpr]);
            return;
        }

        // Generator methods use the generator state machine (#765).
        if (method.IsGenerator)
        {
            EmitGeneratorMethodBody(methodBuilder, method, fieldsField);
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, fieldsField, methodBuilder);
        ctx.IsInstanceMethod = true;
        SetupSyncMethodFunctionDisplayClass(ctx, il, method);

        // Define parameters with typed parameter types from method signature
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            Type? paramType = i < methodParams.Length ? methodParams[i].ParameterType : null;
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i + 1, paramType);
        }

        var emitter = new ILEmitter(ctx);
        var environmentParamTypes = methodBuilder.GetParameters().Select(p => p.ParameterType).ToArray();
        EmitFunctionEnvironmentPrologue(
            il,
            ctx,
            emitter,
            method.Parameters,
            method.Body,
            environmentParamTypes,
            argumentOffset: 1);
        InitializeSyncMethodCapturedParameters(
            ctx, il, method, methodBuilder, argumentOffset: 1);

        if (method.Body != null)
        {
            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Falling off the end completes with `undefined` (ECMA-262). Route through
            // EmitDefaultReturnValue so an `object` slot materializes the `$Undefined`
            // sentinel instead of CLR null. (#588)
            EmitDefaultReturnValue(il, methodBuilder.ReturnType);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits a static method body for a class expression.
    /// </summary>
    private void EmitClassExpressionStaticMethodBody(Expr.ClassExpr classExpr, Stmt.Function method)
    {
        if (!_classExprs.StaticMethods[classExpr].TryGetValue(method.Name.Lexeme, out var methodBuilder))
            return;

        // #703: class-expression static method invoked as a value pads omitted optional
        // args with the `undefined` sentinel on the value-call path (covers sync + async).
        MarkPadsUndefined(methodBuilder);
        MarkFunctionLength(methodBuilder, method.Parameters);

        var typeBuilder = _classExprs.Builders[classExpr];

        // Static generator methods route like a free function (no `this`), mirroring the class-
        // declaration static path (#765/#778). Async generator FIRST since it has both flags set.
        if (method.IsAsync && method.IsGenerator)
        {
            EmitAsyncGeneratorMethodBody(methodBuilder, method, fieldsField: null, isInstanceMethod: false);
            return;
        }

        // Async (non-generator) static methods route through the shared async state-machine
        // emitter (#776), mirroring the static async-generator line above. EmitAsyncMethodBody
        // takes the methodBuilder directly (deriving SM name/lock fields from its DeclaringType),
        // so it works for the class-expression builder — unlike the declaration's
        // EmitStaticAsyncMethodBody, which re-resolves from the declaration registry.
        if (method.IsAsync)
        {
            EmitAsyncMethodBody(methodBuilder, method, fieldsField: null, isInstanceMethod: false,
                currentClassName: _classExprs.Names[classExpr]);
            return;
        }

        if (method.IsGenerator)
        {
            EmitGeneratorMethodBody(methodBuilder, method, fieldsField: null, isInstanceMethod: false);
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, null, methodBuilder);
        ctx.IsInstanceMethod = false;
        SetupSyncMethodFunctionDisplayClass(ctx, il, method);

        // Define parameters with typed parameter types from method signature (starting at index 0 for static)
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            Type? paramType = i < methodParams.Length ? methodParams[i].ParameterType : null;
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i, paramType);
        }

        var emitter = new ILEmitter(ctx);
        var environmentParamTypes = methodBuilder.GetParameters().Select(p => p.ParameterType).ToArray();
        EmitFunctionEnvironmentPrologue(
            il,
            ctx,
            emitter,
            method.Parameters,
            method.Body,
            environmentParamTypes,
            argumentOffset: 0);
        InitializeSyncMethodCapturedParameters(
            ctx, il, method, methodBuilder, argumentOffset: 0);

        if (method.Body != null)
        {
            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Falling off the end completes with `undefined` (ECMA-262). Route through
            // EmitDefaultReturnValue so an `object` slot materializes the `$Undefined`
            // sentinel instead of CLR null. (#588)
            EmitDefaultReturnValue(il, methodBuilder.ReturnType);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits an accessor body for a class expression.
    /// </summary>
    /// <summary>
    /// Emits the bodies of the synthetic symbol-keyed accessor methods (#281)
    /// recorded for a class expression by <see cref="DefineSymbolAccessorMethod"/>.
    /// Uses the class-expression compilation context (so `this`, fields, and
    /// top-level bindings resolve) rather than the class-declaration accessor path.
    /// </summary>
    private void EmitClassExpressionSymbolAccessors(
        Expr.ClassExpr classExpr,
        TypeBuilder typeBuilder,
        FieldInfo fieldsField)
    {
        if (!_classes.SymbolAccessors.TryGetValue(typeBuilder.Name, out var list))
            return;

        foreach (var (accessor, methodBuilder) in list)
        {
            var il = methodBuilder.GetILGenerator();
            var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, accessor.IsStatic ? null : fieldsField, methodBuilder);
            ctx.IsInstanceMethod = !accessor.IsStatic;

            if (accessor.Kind.Type == TokenType.SET && accessor.SetterParam != null)
            {
                var accessorParams = methodBuilder.GetParameters();
                Type? paramType = accessorParams.Length > 0 ? accessorParams[0].ParameterType : null;
                // Instance setter: arg0 is `this`, value at index 1. Static: value at index 0.
                int paramIndex = accessor.IsStatic ? 0 : 1;
                ctx.DefineParameter(accessor.SetterParam.Name.Lexeme, paramIndex, paramType);
            }

            var emitter = new ILEmitter(ctx);
            foreach (var stmt in accessor.Body)
                emitter.EmitStatement(stmt);

            if (emitter.HasDeferredReturns)
                emitter.FinalizeReturns();
            else
            {
                // Falling off the end completes with `undefined` (ECMA-262); the slot is
                // `object`, so emit the `$Undefined` sentinel instead of CLR null. (#588)
                EmitDefaultReturnValue(il, methodBuilder.ReturnType);
                il.Emit(OpCodes.Ret);
            }
        }
    }

    /// <summary>
    /// Pre-defines a uniquely-named .NET method for each computed symbol-keyed method of a class
    /// EXPRESSION (<c>*[Symbol.iterator]() {…}</c> and the async/generator forms), mirroring the
    /// class-declaration <see cref="DefineSymbolMethods"/> (#755). Recorded in the shared
    /// <see cref="ClassState.SymbolMethods"/> registry (keyed by the generated type name) so the
    /// bodies emit through the normal class-expression per-method emitters and the .cctor registers
    /// them in the runtime symbol-method registry.
    /// </summary>
    private void DefineClassExpressionSymbolMethods(Expr.ClassExpr classExpr, TypeBuilder typeBuilder)
    {
        string className = typeBuilder.Name;
        if (_classes.SymbolMethods.ContainsKey(className))
            return;  // already defined (idempotent across multi-module pre-define/emit passes)

        var computed = classExpr.Methods.Where(m =>
            !m.IsPrivate && m.ComputedKey != null && m.Body != null).ToList();
        if (computed.Count == 0)
            return;

        var list = new List<(Stmt.Function, Expr, MethodBuilder)>();
        for (int i = 0; i < computed.Count; i++)
        {
            var method = computed[i];
            // Unique, deterministic name so multiple computed methods don't collide and the synthetic
            // `<computed>` lexeme (not a dispatchable name) is replaced by a real IL name.
            string uniqueName = $"$symmethod_{i}";
            var renamed = method with { Name = new Token(TokenType.IDENTIFIER, uniqueName, null, method.Name.Line) };

            // Class-expression methods use all-object parameter slots (computed iterator methods are
            // typically parameterless anyway). Async generator FIRST since it sets both flags.
            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();
            Type returnType = (method.IsAsync && method.IsGenerator) ? _types.IAsyncEnumerableOfObject :
                              method.IsAsync ? _types.TaskOfObject :
                              method.IsGenerator ? _types.IEnumerableOfObject :
                              typeof(object);

            // Non-virtual (like symbol accessors): the registry holds the exact MethodInfo.
            MethodAttributes attrs = MethodAttributes.Public | MethodAttributes.HideBySig;
            if (method.IsStatic)
                attrs |= MethodAttributes.Static;

            var mb = typeBuilder.DefineMethod(uniqueName, attrs, returnType, paramTypes);

            // Register under the unique name so EmitClassExpression(Static)MethodBody resolves the builder.
            if (method.IsStatic)
                _classExprs.StaticMethods[classExpr][uniqueName] = mb;
            else
                _classExprs.InstanceMethods[classExpr][uniqueName] = mb;

            list.Add((renamed, method.ComputedKey!, mb));
        }
        _classes.SymbolMethods[className] = list;
        if (DefineDeferredComputedMethodKeyRegistrar(typeBuilder) is { } deferred)
            _classExprs.DeferredComputedKeys[classExpr] = deferred;
    }

    /// <summary>
    /// Emits the bodies of the computed symbol-keyed methods recorded by
    /// <see cref="DefineClassExpressionSymbolMethods"/>, reusing the class-expression per-method
    /// emitters so the generator/async state machines compose (#755).
    /// </summary>
    private void EmitClassExpressionSymbolMethods(Expr.ClassExpr classExpr, TypeBuilder typeBuilder, FieldInfo fieldsField)
    {
        if (!_classes.SymbolMethods.TryGetValue(typeBuilder.Name, out var list))
            return;
        foreach (var (method, _key, _builder) in list)
        {
            if (method.IsStatic)
                EmitClassExpressionStaticMethodBody(classExpr, method);
            else
                EmitClassExpressionMethod(classExpr, typeBuilder, method, fieldsField);
        }
    }

    private void EmitClassExpressionAccessor(
        Expr.ClassExpr classExpr,
        TypeBuilder typeBuilder,
        Stmt.Accessor accessor,
        FieldInfo fieldsField)
    {
        string pascalName = NamingConventions.ToPascalCase(accessor.Name.Lexeme);
        MethodBuilder? methodBuilder = accessor.Kind.Type == TokenType.GET
            ? _classExprs.Getters[classExpr].GetValueOrDefault(pascalName)
            : _classExprs.Setters[classExpr].GetValueOrDefault(pascalName);

        if (methodBuilder == null) return;

        var il = methodBuilder.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, fieldsField, methodBuilder);
        ctx.IsInstanceMethod = true;

        if (accessor.Kind.Type == TokenType.SET && accessor.SetterParam != null)
        {
            // Get typed parameter type from accessor method signature
            var accessorParams = methodBuilder.GetParameters();
            Type? paramType = accessorParams.Length > 0 ? accessorParams[0].ParameterType : null;
            ctx.DefineParameter(accessor.SetterParam.Name.Lexeme, 1, paramType);
        }

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in accessor.Body)
        {
            emitter.EmitStatement(stmt);
        }

        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Falling off the end completes with `undefined` (ECMA-262). Route through
            // EmitDefaultReturnValue so an `object` slot materializes the `$Undefined`
            // sentinel instead of CLR null. (#588)
            EmitDefaultReturnValue(il, methodBuilder.ReturnType);
            il.Emit(OpCodes.Ret);
        }
    }
}
