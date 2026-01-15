using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Class definition and method emission for the IL compiler.
///
/// This partial class is split across multiple files:
/// - ILCompiler.Classes.cs: Core class definition (DefineClass)
/// - ILCompiler.Classes.Properties.cs: Property/field handling
/// - ILCompiler.Classes.Methods.cs: Instance method definition and emission
/// - ILCompiler.Classes.Static.cs: Static methods and static constructor
/// - ILCompiler.Classes.Constructors.cs: Constructor emission
/// - ILCompiler.Classes.Accessors.cs: Getter/setter accessors
/// </summary>
public partial class ILCompiler
{
    private void DefineClass(Stmt.Class classStmt)
    {
        var ctx = GetDefinitionContext();

        // Get qualified class name (includes module prefix and .NET namespace if set)
        string qualifiedClassName = ctx.GetQualifiedClassName(classStmt.Name.Lexeme);

        // Track simple name -> module mapping for later lookups
        if (_modules.CurrentPath != null)
        {
            _modules.ClassToModule[classStmt.Name.Lexeme] = _modules.CurrentPath;
        }

        // Check for @DotNetType decorator - external .NET type mapping
        string? dotNetTypeName = GetDotNetTypeMapping(classStmt);
        if (dotNetTypeName != null)
        {
            // Convert friendly syntax to CLR name (e.g., "List<>" -> "List`1")
            string clrTypeName = ToClrTypeName(dotNetTypeName);

            // Try to resolve the external type
            Type? externalType = TryResolveExternalType(clrTypeName);

            if (externalType != null)
            {
                // Register the external type mapping
                _classes.ExternalTypes[qualifiedClassName] = externalType;
                _classes.ExternalTypes[classStmt.Name.Lexeme] = externalType; // Also register simple name

                // Register in TypeMapper for type resolution during IL emission
                _typeMapper.RegisterExternalType(qualifiedClassName, externalType);
                _typeMapper.RegisterExternalType(classStmt.Name.Lexeme, externalType);
            }
            else
            {
                // Warning: type not found but continue compilation
                Console.WriteLine($"Warning: External .NET type '{clrTypeName}' not found in loaded assemblies.");
            }

            // Skip DefineType - don't emit TypeBuilder for external types
            return;
        }

        Type? baseType = null;
        string? qualifiedSuperclassName = null;
        if (classStmt.Superclass != null)
        {
            // Resolve superclass name (includes module prefix and .NET namespace if set)
            qualifiedSuperclassName = ctx.ResolveClassName(classStmt.Superclass.Lexeme);

            if (_classes.Builders.TryGetValue(qualifiedSuperclassName, out var superBuilder))
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
        _classes.Superclass[qualifiedClassName] = qualifiedSuperclassName;

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

            _classes.GenericParams[qualifiedClassName] = genericParams;
        }

        string className = qualifiedClassName;

        // Initialize property tracking dictionaries for this class
        _typedInterop.PropertyBackingFields[className] = [];
        _typedInterop.ClassProperties[className] = [];
        _typedInterop.DeclaredPropertyNames[className] = [];
        _typedInterop.ReadonlyPropertyNames[className] = [];
        _typedInterop.PropertyTypes[className] = [];

        // Add _fields dictionary for dynamic property storage
        // Note: We keep this as _fields for now to maintain compatibility with RuntimeEmitter.Objects.cs
        // In Phase 4, both this and the runtime will be updated to use _extras
        var fieldsField = typeBuilder.DefineField(
            "_fields",
            typeof(Dictionary<string, object>),
            FieldAttributes.Private
        );
        _typedInterop.ExtrasFields[className] = fieldsField;
        _classes.InstanceFieldsField[className] = fieldsField;

        // Analyze @lock decorator requirements and emit lock fields
        var (needsSyncLock, needsAsyncLock, needsStaticSyncLock, needsStaticAsyncLock) = AnalyzeLockRequirements(classStmt);

        // Emit instance lock fields
        if (needsSyncLock || needsAsyncLock)
        {
            // Sync lock object for Monitor
            var syncLockField = typeBuilder.DefineField(
                "_syncLock",
                typeof(object),
                FieldAttributes.Private | FieldAttributes.InitOnly
            );
            _locks.SyncLockFields[className] = syncLockField;

            // Async lock using SemaphoreSlim (permits: 1, max: 1)
            var asyncLockField = typeBuilder.DefineField(
                "_asyncLock",
                typeof(SemaphoreSlim),
                FieldAttributes.Private | FieldAttributes.InitOnly
            );
            _locks.AsyncLockFields[className] = asyncLockField;

            // Reentrancy tracking using AsyncLocal<int>
            var reentrancyField = typeBuilder.DefineField(
                "_lockReentrancy",
                typeof(AsyncLocal<int>),
                FieldAttributes.Private | FieldAttributes.InitOnly
            );
            _locks.ReentrancyFields[className] = reentrancyField;
        }

        // Emit static lock fields
        if (needsStaticSyncLock || needsStaticAsyncLock)
        {
            // Static sync lock object
            var staticSyncLockField = typeBuilder.DefineField(
                "_staticSyncLock",
                typeof(object),
                FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
            );
            _locks.StaticSyncLockFields[className] = staticSyncLockField;

            // Static async lock
            var staticAsyncLockField = typeBuilder.DefineField(
                "_staticAsyncLock",
                typeof(SemaphoreSlim),
                FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
            );
            _locks.StaticAsyncLockFields[className] = staticAsyncLockField;

            // Static reentrancy tracking
            var staticReentrancyField = typeBuilder.DefineField(
                "_staticLockReentrancy",
                typeof(AsyncLocal<int>),
                FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
            );
            _locks.StaticReentrancyFields[className] = staticReentrancyField;
        }

        // Get class generic params if any
        _classes.GenericParams.TryGetValue(className, out var classGenericParams);

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
        Dictionary<string, FieldBuilder> staticFieldBuilders = [];
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

                // Apply field-level decorators as .NET attributes
                if (_decoratorMode != DecoratorMode.None)
                {
                    ApplyFieldDecorators(field, fieldBuilder);
                }
            }
        }

        _classes.Builders[className] = typeBuilder;
        _classes.StaticFields[className] = staticFieldBuilders;

        // Apply class-level decorators as .NET attributes
        if (_decoratorMode != DecoratorMode.None)
        {
            ApplyClassDecorators(classStmt, typeBuilder);
        }
    }

    /// <summary>
    /// Extracts the .NET type name from a @DotNetType decorator if present.
    /// Returns null if the decorator is not present.
    /// </summary>
    private static string? GetDotNetTypeMapping(Stmt.Class classStmt)
    {
        if (classStmt.Decorators == null) return null;

        foreach (var decorator in classStmt.Decorators)
        {
            if (decorator.Expression is Expr.Call call &&
                call.Callee is Expr.Variable v &&
                v.Name.Lexeme == "DotNetType" &&
                call.Arguments.Count == 1 &&
                call.Arguments[0] is Expr.Literal { Value: string typeName })
            {
                return typeName;
            }
        }
        return null;
    }

    /// <summary>
    /// Converts friendly generic syntax to CLR syntax.
    /// Examples: "List&lt;&gt;" -> "System.Collections.Generic.List`1"
    ///           "Dictionary&lt;,&gt;" -> "System.Collections.Generic.Dictionary`2"
    /// </summary>
    private static string ToClrTypeName(string friendlyName)
    {
        int genericStart = friendlyName.IndexOf('<');
        if (genericStart < 0) return friendlyName;

        string baseName = friendlyName[..genericStart];
        string genericPart = friendlyName[genericStart..];

        // Count commas + 1 = number of type parameters
        int paramCount = genericPart.Count(c => c == ',') + 1;

        return $"{baseName}`{paramCount}";
    }

    /// <summary>
    /// Attempts to resolve an external .NET type by name.
    /// First tries the reference loader (for external assemblies),
    /// then falls back to standard type resolution.
    /// </summary>
    private Type? TryResolveExternalType(string clrTypeName)
    {
        // Try reference loader first (external assemblies)
        if (_referenceLoader != null)
        {
            var externalType = _referenceLoader.TryResolve(clrTypeName);
            if (externalType != null) return externalType;
        }

        // Try TypeProvider (BCL types)
        try
        {
            return _types.Resolve(clrTypeName);
        }
        catch
        {
            // Not found in TypeProvider, try Type.GetType
            return Type.GetType(clrTypeName, throwOnError: false);
        }
    }

    /// <summary>
    /// Defines types for all collected class expressions.
    /// Class expressions are collected during arrow function collection phase.
    /// </summary>
    private void DefineClassExpressionTypes()
    {
        foreach (var classExpr in _classExprs.ToDefine)
        {
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

        // Resolve superclass - check both class declarations and other class expressions
        Type? baseType = null;
        string? superclassName = null;
        if (classExpr.Superclass != null)
        {
            superclassName = classExpr.Superclass.Lexeme;

            // Check class declarations first (with module resolution)
            var resolvedSuperName = GetDefinitionContext().ResolveClassName(superclassName);
            if (_classes.Builders.TryGetValue(resolvedSuperName, out var superTypeBuilder))
            {
                baseType = superTypeBuilder;
            }
            else
            {
                // Check other class expressions by their generated name
                foreach (var (expr, name) in _classExprs.Names)
                {
                    if (name == superclassName && _classExprs.Builders.TryGetValue(expr, out var superExprBuilder))
                    {
                        baseType = superExprBuilder;
                        break;
                    }
                }
            }
        }
        _classExprs.Superclass[classExpr] = superclassName;

        // Create TypeBuilder with appropriate attributes
        TypeAttributes typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (classExpr.IsAbstract)
        {
            typeAttrs |= TypeAttributes.Abstract;
        }

        var typeBuilder = _moduleBuilder.DefineType(
            className,
            typeAttrs,
            baseType ?? typeof(object)
        );

        // Handle generic type parameters
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
                        classGenericParams[i].SetBaseTypeConstraint(constraintType);
                }
            }

            _classExprs.GenericParams[classExpr] = classGenericParams;
        }

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
        foreach (var field in classExpr.Fields.Where(f => !f.IsStatic))
        {
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
        var constructor = classExpr.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);
        var ctorParamTypes = constructor?.Parameters.Select(_ => typeof(object)).ToArray() ?? [];

        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            ctorParamTypes
        );
        _classExprs.Constructors[classExpr] = ctorBuilder;
        _classes.Constructors[className] = ctorBuilder;

        // Define static methods
        foreach (var method in classExpr.Methods.Where(m => m.Body != null && m.IsStatic && m.Name.Lexeme != "constructor"))
        {
            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();
            Type returnType = method.IsAsync ? _types.TaskOfObject : typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                MethodAttributes.Public | MethodAttributes.Static,
                returnType,
                paramTypes
            );
            _classExprs.StaticMethods[classExpr][method.Name.Lexeme] = methodBuilder;
        }

        // Define instance methods
        foreach (var method in classExpr.Methods.Where(m => m.Body != null && !m.IsStatic && m.Name.Lexeme != "constructor"))
        {
            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
            if (method.IsAbstract)
                methodAttrs |= MethodAttributes.Abstract;

            Type returnType = method.IsAsync ? typeof(Task<object>) : typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                methodAttrs,
                returnType,
                paramTypes
            );
            _classExprs.InstanceMethods[classExpr][method.Name.Lexeme] = methodBuilder;
        }

        // Define user-defined accessors (overrides property accessors)
        if (classExpr.Accessors != null)
        {
            foreach (var accessor in classExpr.Accessors)
            {
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

        // Emit static constructor if there are static field initializers
        EmitClassExpressionStaticConstructor(classExpr, typeBuilder);

        // Emit instance constructor
        EmitClassExpressionConstructor(classExpr, typeBuilder, fieldsField);

        // Emit instance method bodies
        foreach (var method in classExpr.Methods.Where(m => m.Body != null && !m.IsStatic && m.Name.Lexeme != "constructor"))
        {
            EmitClassExpressionMethod(classExpr, typeBuilder, method, fieldsField);
        }

        // Emit static method bodies
        foreach (var method in classExpr.Methods.Where(m => m.Body != null && m.IsStatic))
        {
            EmitClassExpressionStaticMethodBody(classExpr, method);
        }

        // Emit user-defined accessor bodies
        if (classExpr.Accessors != null)
        {
            foreach (var accessor in classExpr.Accessors)
            {
                if (!accessor.IsAbstract)
                {
                    EmitClassExpressionAccessor(classExpr, typeBuilder, accessor, fieldsField);
                }
            }
        }
    }

    /// <summary>
    /// Creates a CompilationContext for class expression method emission.
    /// </summary>
    private CompilationContext CreateClassExpressionContext(
        ILGenerator il,
        Expr.ClassExpr classExpr,
        TypeBuilder typeBuilder,
        FieldInfo? fieldsField)
    {
        string className = _classExprs.Names[classExpr];

        return new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            FieldsField = fieldsField,
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _classes.StaticFields,
            StaticMethods = _classes.StaticMethods,
            ClassConstructors = _classes.Constructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            ClassGenericParams = _classes.GenericParams,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _classes.InstanceMethods,
            InstanceGetters = _classes.InstanceGetters,
            InstanceSetters = _classes.InstanceSetters,
            ClassSuperclass = _classes.Superclass,
            CurrentModulePath = _modules.CurrentPath,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            PropertyBackingFields = _typedInterop.PropertyBackingFields,
            ClassProperties = _typedInterop.ClassProperties,
            DeclaredPropertyNames = _typedInterop.DeclaredPropertyNames,
            ReadonlyPropertyNames = _typedInterop.ReadonlyPropertyNames,
            PropertyTypes = _typedInterop.PropertyTypes,
            ExtrasFields = _typedInterop.ExtrasFields,
            UnionGenerator = _unionGenerator,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            ClassExprBuilders = _classExprs.Builders,
            ClassExprBackingFields = _classExprs.BackingFields,
            ClassExprProperties = _classExprs.Properties,
            ClassExprPropertyTypes = _classExprs.PropertyTypes,
            ClassExprDeclaredProperties = _classExprs.DeclaredProperties,
            ClassExprReadonlyProperties = _classExprs.ReadonlyProperties,
            ClassExprStaticFields = _classExprs.StaticFields,
            ClassExprStaticMethods = _classExprs.StaticMethods,
            ClassExprInstanceMethods = _classExprs.InstanceMethods,
            ClassExprGetters = _classExprs.Getters,
            ClassExprSetters = _classExprs.Setters,
            ClassExprConstructors = _classExprs.Constructors,
            ClassExprGenericParams = _classExprs.GenericParams,
            ClassExprSuperclass = _classExprs.Superclass,
            CurrentClassExpr = classExpr,
            VarToClassExpr = _classExprs.VarToClassExpr
        };
    }

    /// <summary>
    /// Emits static constructor for class expression static field initializers.
    /// </summary>
    private void EmitClassExpressionStaticConstructor(Expr.ClassExpr classExpr, TypeBuilder typeBuilder)
    {
        var staticFieldsWithInit = classExpr.Fields.Where(f => f.IsStatic && f.Initializer != null).ToList();
        if (staticFieldsWithInit.Count == 0) return;

        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, null);
        var emitter = new ILEmitter(ctx);

        foreach (var field in staticFieldsWithInit)
        {
            var staticField = _classExprs.StaticFields[classExpr][field.Name.Lexeme];

            emitter.EmitExpression(field.Initializer!);

            // Only box if the field type is object (dynamic field)
            // Don't box for typed fields (number -> double, etc.)
            if (staticField.FieldType == typeof(object))
            {
                emitter.EmitBoxIfNeeded(field.Initializer!);
            }

            il.Emit(OpCodes.Stsfld, staticField);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits instance constructor for a class expression.
    /// </summary>
    private void EmitClassExpressionConstructor(Expr.ClassExpr classExpr, TypeBuilder typeBuilder, FieldInfo fieldsField)
    {
        string className = _classExprs.Names[classExpr];
        var ctorBuilder = _classExprs.Constructors[classExpr];
        var constructor = classExpr.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);

        var il = ctorBuilder.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, fieldsField);
        ctx.IsInstanceMethod = true;

        // Add generic type parameters to context
        if (_classExprs.GenericParams.TryGetValue(classExpr, out var genericParams))
        {
            foreach (var gp in genericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Initialize _fields dictionary FIRST
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);
        il.Emit(OpCodes.Stfld, fieldsField);

        // Determine if we need to call base constructor automatically
        // If there's an explicit constructor body, it should contain super() call
        bool hasExplicitSuperCall = constructor?.Body?.Any(stmt => ContainsSuperCall(stmt)) ?? false;

        if (!hasExplicitSuperCall)
        {
            // No explicit super() - call parent constructor automatically
            Type baseType = typeBuilder.BaseType ?? typeof(object);
            il.Emit(OpCodes.Ldarg_0);
            var baseCtor = baseType.GetConstructor([]) ?? typeof(object).GetConstructor([])!;
            il.Emit(OpCodes.Call, baseCtor);
        }

        // Emit constructor body first if present (contains super() call)
        var emitter = new ILEmitter(ctx);
        if (constructor != null)
        {
            // Define parameters
            for (int i = 0; i < constructor.Parameters.Count; i++)
            {
                ctx.DefineParameter(constructor.Parameters[i].Name.Lexeme, i + 1);
            }

            emitter.EmitDefaultParameters(constructor.Parameters, true);

            if (constructor.Body != null)
            {
                foreach (var stmt in constructor.Body)
                {
                    emitter.EmitStatement(stmt);
                }
            }
        }

        // Emit instance field initializers to backing fields AFTER super() call
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
                il.Emit(OpCodes.Ldfld, fieldsField);
                il.Emit(OpCodes.Ldstr, fieldName);
                emitter.EmitExpression(field.Initializer!);
                emitter.EmitBoxIfNeeded(field.Initializer!);
                il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
            }
        }

        il.Emit(OpCodes.Ret);
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

        // Handle async methods via state machine
        if (method.IsAsync)
        {
            EmitClassExpressionAsyncMethod(classExpr, typeBuilder, method, methodBuilder, fieldsField);
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, fieldsField);
        ctx.IsInstanceMethod = true;

        // Define parameters
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i + 1);
        }

        var emitter = new ILEmitter(ctx);
        emitter.EmitDefaultParameters(method.Parameters, true);

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
            il.Emit(OpCodes.Ldnull);
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

        var typeBuilder = _classExprs.Builders[classExpr];

        if (method.IsAsync)
        {
            EmitClassExpressionStaticAsyncMethod(classExpr, typeBuilder, method, methodBuilder);
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, null);
        ctx.IsInstanceMethod = false;

        // Define parameters (starting at index 0 for static)
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i);
        }

        var emitter = new ILEmitter(ctx);
        emitter.EmitDefaultParameters(method.Parameters, false);

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
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits an accessor body for a class expression.
    /// </summary>
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
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, fieldsField);
        ctx.IsInstanceMethod = true;

        if (accessor.Kind.Type == TokenType.SET && accessor.SetterParam != null)
        {
            ctx.DefineParameter(accessor.SetterParam.Name.Lexeme, 1);
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
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits an async instance method for a class expression using state machine.
    /// </summary>
    private void EmitClassExpressionAsyncMethod(
        Expr.ClassExpr classExpr,
        TypeBuilder typeBuilder,
        Stmt.Function method,
        MethodBuilder methodBuilder,
        FieldInfo fieldsField)
    {
        // For now, emit a simple wrapper that runs synchronously and returns Task.FromResult
        // Full state machine support can be added later if needed
        var il = methodBuilder.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, fieldsField);
        ctx.IsInstanceMethod = true;

        // Define parameters
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i + 1);
        }

        var emitter = new ILEmitter(ctx);
        emitter.EmitDefaultParameters(method.Parameters, true);

        // Emit body statements
        if (method.Body != null)
        {
            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Return Task.FromResult(null)
        if (!emitter.HasDeferredReturns)
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(object)));
            il.Emit(OpCodes.Ret);
        }
        else
        {
            emitter.FinalizeReturns();
        }
    }

    /// <summary>
    /// Emits an async static method for a class expression.
    /// </summary>
    private void EmitClassExpressionStaticAsyncMethod(
        Expr.ClassExpr classExpr,
        TypeBuilder typeBuilder,
        Stmt.Function method,
        MethodBuilder methodBuilder)
    {
        var il = methodBuilder.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, null);
        ctx.IsInstanceMethod = false;

        // Define parameters
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i);
        }

        var emitter = new ILEmitter(ctx);
        emitter.EmitDefaultParameters(method.Parameters, false);

        // Emit body statements
        if (method.Body != null)
        {
            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Return Task.FromResult(null)
        if (!emitter.HasDeferredReturns)
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(object)));
            il.Emit(OpCodes.Ret);
        }
        else
        {
            emitter.FinalizeReturns();
        }
    }
}
