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

        // Set TypeAttributes.Abstract if the class is abstract
        TypeAttributes typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (classStmt.IsAbstract)
        {
            typeAttrs |= TypeAttributes.Abstract;
        }

        // Resolve superclass name for tracking (before creating TypeBuilder)
        string? qualifiedSuperclassName = null;
        if (classStmt.Superclass != null)
        {
            qualifiedSuperclassName = ctx.ResolveClassName(classStmt.Superclass.Lexeme);
        }

        // Create TypeBuilder initially without parent - we'll set it after defining generic params
        // This is necessary because the base type may reference our generic params (e.g., class Foo<T> extends Box<T>)
        var typeBuilder = _moduleBuilder.DefineType(
            qualifiedClassName,
            typeAttrs
        );

        // Track superclass for inheritance-aware method resolution
        _classes.Superclass[qualifiedClassName] = qualifiedSuperclassName;

        // Handle generic type parameters FIRST (before resolving superclass type args)
        GenericTypeParameterBuilder[]? classGenericParams = null;
        if (classStmt.TypeParams != null && classStmt.TypeParams.Count > 0)
        {
            string[] typeParamNames = classStmt.TypeParams.Select(tp => tp.Name.Lexeme).ToArray();
            classGenericParams = typeBuilder.DefineGenericParameters(typeParamNames);

            // Apply constraints
            for (int i = 0; i < classStmt.TypeParams.Count; i++)
            {
                var constraint = classStmt.TypeParams[i].Constraint;
                if (constraint != null)
                {
                    Type constraintType = ResolveConstraintType(constraint);
                    if (constraintType.IsInterface)
                        classGenericParams[i].SetInterfaceConstraints(constraintType);
                    else
                        classGenericParams[i].SetBaseTypeConstraint(constraintType);
                }
            }

            _classes.GenericParams[qualifiedClassName] = classGenericParams;
        }

        // NOW resolve the base type (may use our generic params for type arguments)
        Type? baseType = null;
        if (qualifiedSuperclassName != null && _classes.Builders.TryGetValue(qualifiedSuperclassName, out var superBuilder))
        {
            if (classStmt.SuperclassTypeArgs != null && classStmt.SuperclassTypeArgs.Count > 0)
            {
                // Resolve type arguments - may reference our own generic params
                var typeArgs = ResolveSuperclassTypeArguments(
                    classStmt.SuperclassTypeArgs,
                    classGenericParams,
                    classStmt.TypeParams);
                baseType = superBuilder.MakeGenericType(typeArgs);
            }
            else
            {
                baseType = superBuilder;
            }
        }

        // Set the parent type (defaults to Object if baseType is null)
        if (baseType != null)
        {
            typeBuilder.SetParent(baseType);
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

        // Define real .NET properties with typed backing fields for instance fields
        // Skip fields with generic type parameters - they'll use _extras dictionary instead
        // Skip ES2022 private fields (#field) - they're not exposed as .NET properties
        // Skip declare fields - they use _extras dictionary to support TypeScript null semantics
        foreach (var field in classStmt.Fields)
        {
            if (!field.IsStatic && !field.IsPrivate && !field.IsDeclare)
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
        // Skip ES2022 private static fields (#field) - they're not exposed as .NET fields
        Dictionary<string, FieldBuilder> staticFieldBuilders = [];
        foreach (var field in classStmt.Fields)
        {
            if (field.IsStatic && !field.IsPrivate)
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

        // ES2022 Private Class Elements: Define storage for private fields and methods
        DefinePrivateClassElements(typeBuilder, className, classStmt, classGenericParams);

        // Define auto-accessor properties (TypeScript 4.9+)
        if (classStmt.AutoAccessors != null)
        {
            foreach (var autoAccessor in classStmt.AutoAccessors)
            {
                DefineAutoAccessorProperty(typeBuilder, className, autoAccessor, classGenericParams);
            }
        }

        // Apply class-level decorators as .NET attributes
        if (_decoratorMode != DecoratorMode.None)
        {
            ApplyClassDecorators(classStmt, typeBuilder);
        }
    }

    /// <summary>
    /// Defines infrastructure for ES2022 private class elements (#field, #method).
    /// This includes:
    /// - ConditionalWeakTable for instance private field storage
    /// - Static fields for static private fields
    /// - Methods for private instance and static methods
    /// </summary>
    private void DefinePrivateClassElements(
        TypeBuilder typeBuilder,
        string className,
        Stmt.Class classStmt,
        GenericTypeParameterBuilder[]? classGenericParams)
    {
        // Collect private fields (IsPrivate flag indicates #field syntax)
        var instancePrivateFields = classStmt.Fields.Where(f => f.IsPrivate && !f.IsStatic).ToList();
        var staticPrivateFields = classStmt.Fields.Where(f => f.IsPrivate && f.IsStatic).ToList();

        // Collect private methods (IsPrivate flag indicates #method syntax)
        var instancePrivateMethods = classStmt.Methods.Where(m => m.IsPrivate && !m.IsStatic && m.Name.Lexeme != "constructor").ToList();
        var staticPrivateMethods = classStmt.Methods.Where(m => m.IsPrivate && m.IsStatic).ToList();

        // Initialize tracking dictionaries
        _classes.PrivateFieldNames[className] = [];
        _classes.StaticPrivateFields[className] = [];
        _classes.PrivateMethods[className] = [];
        _classes.StaticPrivateMethods[className] = [];

        // Define ConditionalWeakTable storage for instance private fields
        if (instancePrivateFields.Count > 0)
        {
            // Define: private static readonly ConditionalWeakTable<object, Dictionary<string, object?>> __privateFields
            var cwtType = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>)
                .MakeGenericType(typeof(object), typeof(Dictionary<string, object?>));

            var storageField = typeBuilder.DefineField(
                "__privateFields",
                cwtType,
                FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
            );
            _classes.PrivateFieldStorage[className] = storageField;

            // Track private field names for initialization (preserve declaration order)
            foreach (var field in instancePrivateFields)
            {
                // Store the field name without the # prefix (the lexer includes it in the token)
                string fieldName = field.Name.Lexeme;
                if (fieldName.StartsWith('#'))
                    fieldName = fieldName[1..];
                _classes.PrivateFieldNames[className].Add(fieldName);
            }
        }

        // Define static private fields as actual static fields with mangled names
        foreach (var field in staticPrivateFields)
        {
            string fieldName = field.Name.Lexeme;
            if (fieldName.StartsWith('#'))
                fieldName = fieldName[1..];

            // Mangle the name to avoid collisions with public fields
            var staticField = typeBuilder.DefineField(
                $"__private_{fieldName}",
                typeof(object),
                FieldAttributes.Private | FieldAttributes.Static
            );
            _classes.StaticPrivateFields[className][fieldName] = staticField;
        }

        // Define private instance methods
        foreach (var method in instancePrivateMethods)
        {
            string methodName = method.Name.Lexeme;
            if (methodName.StartsWith('#'))
                methodName = methodName[1..];

            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();
            Type returnType = method.IsAsync ? _types.TaskOfObject : typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                $"__private_{methodName}",
                MethodAttributes.Private | MethodAttributes.HideBySig,
                returnType,
                paramTypes
            );
            _classes.PrivateMethods[className][methodName] = methodBuilder;
        }

        // Define static private methods
        foreach (var method in staticPrivateMethods)
        {
            string methodName = method.Name.Lexeme;
            if (methodName.StartsWith('#'))
                methodName = methodName[1..];

            var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();
            Type returnType = method.IsAsync ? _types.TaskOfObject : typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                $"__private_{methodName}",
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
                returnType,
                paramTypes
            );
            _classes.StaticPrivateMethods[className][methodName] = methodBuilder;
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
    /// Resolves superclass type arguments to .NET Types.
    /// Handles primitive types, user-defined classes, array types, and type parameter forwarding.
    /// </summary>
    /// <param name="typeArgs">The type argument strings from the AST (e.g., ["string", "T"])</param>
    /// <param name="classGenericParams">The class's own generic type parameters (for forwarding like extends Box&lt;T&gt;)</param>
    /// <param name="classTypeParams">The class's type parameter declarations (for matching names)</param>
    /// <returns>Array of resolved .NET Types</returns>
    private Type[] ResolveSuperclassTypeArguments(
        List<string> typeArgs,
        GenericTypeParameterBuilder[]? classGenericParams,
        List<Parsing.TypeParam>? classTypeParams)
    {
        var result = new Type[typeArgs.Count];
        for (int i = 0; i < typeArgs.Count; i++)
        {
            result[i] = ResolveTypeArgument(typeArgs[i], classGenericParams, classTypeParams);
        }
        return result;
    }

    /// <summary>
    /// Resolves a single type argument string to a .NET Type.
    /// </summary>
    private Type ResolveTypeArgument(
        string typeArg,
        GenericTypeParameterBuilder[]? classGenericParams,
        List<Parsing.TypeParam>? classTypeParams)
    {
        // 1. Check if it's a reference to the class's own type parameter (e.g., class Foo<T> extends Box<T>)
        if (classGenericParams != null && classTypeParams != null)
        {
            for (int i = 0; i < classTypeParams.Count; i++)
            {
                if (classTypeParams[i].Name.Lexeme == typeArg)
                    return classGenericParams[i];
            }
        }

        // 2. Check if it's a primitive type
        var primitiveType = TypeMapper.GetClrType(typeArg);
        if (primitiveType != typeof(object))
            return primitiveType;

        // 3. Check for specific primitive type names that map to object but should be typed
        if (PrimitiveTypeMappings.StringToClrType.TryGetValue(typeArg, out var mappedType))
            return mappedType;

        // 4. Check if it's a user-defined class
        var defCtx = GetDefinitionContext();
        var resolvedClassName = defCtx.ResolveClassName(typeArg);
        if (_classes.Builders.TryGetValue(resolvedClassName, out var classBuilder))
            return classBuilder;

        // Also try the simple name
        if (_classes.Builders.TryGetValue(typeArg, out classBuilder))
            return classBuilder;

        // 5. Check for array types (e.g., "number[]")
        if (typeArg.EndsWith("[]"))
        {
            var elementTypeArg = typeArg[..^2];
            var elementType = ResolveTypeArgument(elementTypeArg, classGenericParams, classTypeParams);
            return elementType.MakeArrayType();
        }

        // 6. Check for nested generics (e.g., "Map<string, number>")
        if (typeArg.Contains('<'))
        {
            return ResolveNestedGenericTypeArgument(typeArg, classGenericParams, classTypeParams);
        }

        // Fallback to object
        return typeof(object);
    }

    /// <summary>
    /// Resolves a nested generic type argument (e.g., "Map&lt;string, number&gt;").
    /// </summary>
    private Type ResolveNestedGenericTypeArgument(
        string typeArg,
        GenericTypeParameterBuilder[]? classGenericParams,
        List<Parsing.TypeParam>? classTypeParams)
    {
        // Parse "Map<string, number>" into baseName and type args
        int angleIndex = typeArg.IndexOf('<');
        string baseName = typeArg[..angleIndex];
        string typeArgsStr = typeArg[(angleIndex + 1)..^1]; // Remove < and >

        // Split type args (handling nested generics)
        var nestedTypeArgs = ParseTypeArgsString(typeArgsStr);

        // Resolve base type
        Type? baseType = null;
        var defCtx = GetDefinitionContext();
        var resolvedBaseName = defCtx.ResolveClassName(baseName);
        if (_classes.Builders.TryGetValue(resolvedBaseName, out var classBuilder))
        {
            baseType = classBuilder;
        }
        else if (_classes.Builders.TryGetValue(baseName, out classBuilder))
        {
            baseType = classBuilder;
        }
        else if (baseName == "Map")
        {
            baseType = typeof(Dictionary<,>);
        }
        else if (baseName == "Set")
        {
            baseType = typeof(HashSet<>);
        }
        else if (baseName == "Promise")
        {
            baseType = typeof(Task<>);
        }

        if (baseType == null)
            return typeof(object);

        // Resolve each type argument recursively
        var resolvedArgs = nestedTypeArgs
            .Select(ta => ResolveTypeArgument(ta.Trim(), classGenericParams, classTypeParams))
            .ToArray();

        return baseType.MakeGenericType(resolvedArgs);
    }

    /// <summary>
    /// Parses a comma-separated type arguments string, handling nested generics.
    /// E.g., "string, Map&lt;string, number&gt;" -> ["string", "Map&lt;string, number&gt;"]
    /// </summary>
    private static List<string> ParseTypeArgsString(string typeArgsStr)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < typeArgsStr.Length; i++)
        {
            char c = typeArgsStr[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(typeArgsStr[start..i].Trim());
                start = i + 1;
            }
        }

        // Add last segment
        if (start < typeArgsStr.Length)
        {
            result.Add(typeArgsStr[start..].Trim());
        }

        return result;
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

        // Track superclass name for inheritance resolution
        string? superclassName = classExpr.Superclass?.Lexeme;
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
                        classGenericParams[i].SetBaseTypeConstraint(constraintType);
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
                    baseType = superTypeBuilder.MakeGenericType(typeArgs);
                }
                else
                {
                    baseType = superTypeBuilder;
                }
            }
        }

        // Set the parent type
        if (baseType != null)
        {
            typeBuilder.SetParent(baseType);
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
        // Skip declare fields - they use _fields dictionary to support TypeScript null semantics
        foreach (var field in classExpr.Fields.Where(f => !f.IsStatic && !f.IsDeclare))
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
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
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
            BuiltInModuleMethodBindings = _builtInModuleMethodBindings,
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
            VarToClassExpr = _classExprs.VarToClassExpr,
            IsStrictMode = _isStrictMode,
            // Registry services
            ClassRegistry = GetClassRegistry()
        };
    }

    /// <summary>
    /// Emits static constructor for class expression static field initializers and static blocks.
    /// </summary>
    private void EmitClassExpressionStaticConstructor(Expr.ClassExpr classExpr, TypeBuilder typeBuilder)
    {
        bool hasStaticFields = classExpr.Fields.Any(f => f.IsStatic && f.Initializer != null);
        bool hasStaticInitializers = classExpr.StaticInitializers?.Count > 0;

        if (!hasStaticFields && !hasStaticInitializers) return;

        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        var ctx = CreateClassExpressionContext(il, classExpr, typeBuilder, null);
        ctx.IsStaticConstructorContext = true;
        var emitter = new ILEmitter(ctx);

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
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stfld, fieldsField);

        // Determine if we need to call base constructor automatically
        // If there's an explicit constructor body, it should contain super() call
        bool hasExplicitSuperCall = constructor?.Body?.Any(stmt => ContainsSuperCall(stmt)) ?? false;

        if (!hasExplicitSuperCall)
        {
            // No explicit super() - call parent constructor automatically
            Type baseType = typeBuilder.BaseType ?? typeof(object);
            il.Emit(OpCodes.Ldarg_0);
            var baseCtor = baseType.GetConstructor([]) ?? _types.ObjectDefaultCtor;
            il.Emit(OpCodes.Call, baseCtor);
        }

        // Emit constructor body first if present (contains super() call)
        var emitter = new ILEmitter(ctx);
        if (constructor != null)
        {
            // Define parameters with typed parameter types from constructor signature
            var ctorParams = ctorBuilder.GetParameters();
            for (int i = 0; i < constructor.Parameters.Count; i++)
            {
                Type? paramType = i < ctorParams.Length ? ctorParams[i].ParameterType : null;
                ctx.DefineParameter(constructor.Parameters[i].Name.Lexeme, i + 1, paramType);
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
            il.Emit(OpCodes.Ldfld, fieldsField);
            il.Emit(OpCodes.Ldstr, fieldName);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
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

        // Define parameters with typed parameter types from method signature
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            Type? paramType = i < methodParams.Length ? methodParams[i].ParameterType : null;
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i + 1, paramType);
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

        // Define parameters with typed parameter types from method signature (starting at index 0 for static)
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            Type? paramType = i < methodParams.Length ? methodParams[i].ParameterType : null;
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i, paramType);
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

        // Define parameters with typed parameter types from method signature
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            Type? paramType = i < methodParams.Length ? methodParams[i].ParameterType : null;
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i + 1, paramType);
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

        // Define parameters with typed parameter types from method signature
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            Type? paramType = i < methodParams.Length ? methodParams[i].ParameterType : null;
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i, paramType);
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
