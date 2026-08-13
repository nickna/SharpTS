using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Class definition and method emission for the IL compiler.
///
/// This partial class is split across multiple files:
/// - ILCompiler.Classes.cs: Core class definition (DefineClass, type mapping, type argument resolution)
/// - ILCompiler.Classes.ClassExpressions.cs: Class expression types, methods, and emission
/// - ILCompiler.Classes.HasFields.cs: $IHasFields interface method definition and emission
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
        string qualifiedClassName = GetQualifiedClassDeclarationName(classStmt);

        // Track simple name -> module mapping for later lookups
        if (_modules.CurrentPath != null && !_classes.BlockScopedNames.ContainsKey(classStmt))
        {
            _modules.ClassToModule[classStmt.Name.Lexeme] = _modules.CurrentPath;
        }

        // Check for @DotNetType decorator - external .NET type mapping
        string? dotNetTypeName = GetDotNetTypeMapping(classStmt);
        if (dotNetTypeName != null)
        {
            // Resolve ordinary and constructed-generic friendly names through the same parser
            // interpreter mode and dotnet: imports use.
            Type? externalType = Runtime.DotNet.DotNetTypeRegistry.ResolveFriendly(
                dotNetTypeName, TryResolveExternalType);

            if (externalType != null)
            {
                // Register the external type mapping
                _classes.ExternalTypes[qualifiedClassName] = externalType;
                _classes.ExternalTypes[classStmt.Name.Lexeme] = externalType; // Also register simple name

                // Register in TypeMapper for type resolution during IL emission
                _typeMapper.RegisterExternalType(qualifiedClassName, externalType);
                _typeMapper.RegisterExternalType(classStmt.Name.Lexeme, externalType);

                // Collect @DotNetOverload hints so external-call emission can disambiguate.
                var hints = GetOverloadHints(classStmt);
                if (hints.Count > 0)
                {
                    _typeMapper.RegisterOverloadHints(externalType, hints);
                }
            }
            else
            {
                // Warning: type not found but continue compilation
                AddWarning($"External .NET type '{dotNetTypeName}' not found in loaded assemblies.");
            }

            // Skip DefineType - don't emit TypeBuilder for external types
            return;
        }

        // #724: register function display classes for instance generator methods whose arrows write a
        // captured method local, before Phase 5's PropagateFunctionDCRequirements runs. Skipped for
        // external (@DotNetType) classes, which return above without an emitted body.
        RegisterGeneratorMethodFunctionDisplayClasses(classStmt, qualifiedClassName);

        // Same, for async (non-generator) methods whose nested sync arrow writes a captured method local,
        // before Phase 5's PropagateFunctionDCRequirements runs.
        RegisterAsyncMethodFunctionDisplayClasses(classStmt.Methods, qualifiedClassName);

        // Set TypeAttributes.Abstract if the class is abstract
        TypeAttributes typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (classStmt.IsAbstract)
        {
            typeAttrs |= TypeAttributes.Abstract;
        }

        // Resolve superclass name for tracking (before creating TypeBuilder)
        string? qualifiedSuperclassName = null;
        if (classStmt.SuperclassExpr != null)
        {
            qualifiedSuperclassName = ctx.ResolveClassName(Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)!);
        }

        // Create TypeBuilder initially without parent - we'll set it after defining generic params
        // This is necessary because the base type may reference our generic params (e.g., class Foo<T> extends Box<T>)
        var typeBuilder = _moduleBuilder.DefineType(
            qualifiedClassName,
            typeAttrs
        );
        if (_classes.BlockScopedNames.ContainsKey(classStmt))
            _classes.BlockScopedBuilders[classStmt] = typeBuilder;

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
                        EmitTypeDefinitions.SetBaseTypeConstraint(classGenericParams[i], constraintType);
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
                baseType = _types.MakeGenericType(superBuilder, typeArgs);
            }
            else
            {
                baseType = superBuilder;
            }
        }
        else if (qualifiedSuperclassName != null && classStmt.SuperclassExpr != null
            && Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)!))
        {
            // Built-in error types: extend the emitted $Error/$TypeError/etc.
            baseType = GetEmittedErrorType(Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)!);
        }
        else if (qualifiedSuperclassName != null && classStmt.SuperclassExpr != null
            && Expr.GetSuperclassLeafName(classStmt.SuperclassExpr) == "Array")
        {
            // `extends Array` (#233): derive from the emitted $Array, which
            // itself inherits List<object?> — every IList-based array dispatch
            // (push/length/iteration/Array.isArray/instanceof Array) applies
            // to instances unchanged.
            baseType = _runtime.TSArrayType;
        }
        else if (qualifiedSuperclassName != null && classStmt.SuperclassExpr != null
            && Expr.GetSuperclassLeafName(classStmt.SuperclassExpr) == "Promise")
        {
            // `extends Promise` (#242): derive from the emitted $Promise, the
            // promise object representation (wraps Task<object?>) — await,
            // then/catch/finally, and instanceof Promise apply to instances
            // unchanged.
            baseType = _runtime.TSPromiseType;
        }
        else if (qualifiedSuperclassName != null && classStmt.SuperclassExpr != null
            && Expr.GetSuperclassLeafName(classStmt.SuperclassExpr) is
                "Map" or "Set" or "WeakMap" or "WeakSet" or "Date" or "RegExp" or "Buffer")
        {
            // Built-in constructors without a subclassing bridge (#233/#221):
            // fail loudly at compile time rather than silently extending
            // System.Object and misbehaving at runtime. Mirrors the
            // interpreter's "subclassing this built-in is not supported yet".
            throw new Diagnostics.Exceptions.CompileException(
                $"Class '{classStmt.Name.Lexeme}' cannot extend built-in '{Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)}': subclassing this built-in is not supported yet.");
        }

        // Track Error subclass status (direct or transitive)
        if (classStmt.SuperclassExpr != null && Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)!))
        {
            _classes.ErrorSubclasses.Add(qualifiedClassName);
        }
        else if (qualifiedSuperclassName != null && _classes.ErrorSubclasses.Contains(qualifiedSuperclassName))
        {
            _classes.ErrorSubclasses.Add(qualifiedClassName);
        }

        // Track Array subclass status (direct or transitive) for constructor
        // base-call emission (#233)
        if (classStmt.SuperclassExpr != null && Expr.GetSuperclassLeafName(classStmt.SuperclassExpr) == "Array"
            && !_classes.Builders.ContainsKey(qualifiedSuperclassName ?? ""))
        {
            _classes.ArraySubclasses.Add(qualifiedClassName);
        }
        else if (qualifiedSuperclassName != null && _classes.ArraySubclasses.Contains(qualifiedSuperclassName))
        {
            _classes.ArraySubclasses.Add(qualifiedClassName);
        }

        // Track Promise subclass status (direct or transitive) for constructor
        // base-call emission and static-side inheritance (#242)
        if (classStmt.SuperclassExpr != null && Expr.GetSuperclassLeafName(classStmt.SuperclassExpr) == "Promise"
            && !_classes.Builders.ContainsKey(qualifiedSuperclassName ?? ""))
        {
            _classes.PromiseSubclasses.Add(qualifiedClassName);
        }
        else if (qualifiedSuperclassName != null && _classes.PromiseSubclasses.Contains(qualifiedSuperclassName))
        {
            _classes.PromiseSubclasses.Add(qualifiedClassName);
        }

        // Set the parent type (defaults to Object if baseType is null)
        if (baseType != null)
        {
            EmitTypeDefinitions.SetParent(typeBuilder, baseType);
        }

        // Implement $IHasFields interface for unified property access
        // All classes implement this interface (including derived classes)
        // Each class emits its own GetProperty/SetProperty methods that access its _fields
        EmitTypeDefinitions.AddInterfaceImplementation(typeBuilder, _runtime.IHasFieldsInterface);

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

        // Define $IHasFields interface method stubs
        // Bodies are emitted later in EmitHasFieldsInterfaceMethodBodies after method definitions are available
        DefineHasFieldsInterfaceMethods(typeBuilder, className, fieldsField);

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
            var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));

            // Use Assembly (internal) visibility so nested async/generator state machines can access this field
            var storageField = typeBuilder.DefineField(
                "__privateFields",
                cwtType,
                FieldAttributes.Assembly | FieldAttributes.Static | FieldAttributes.InitOnly
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
            // Use Assembly (internal) visibility so nested async/generator state machines can access this field
            var staticField = typeBuilder.DefineField(
                $"__private_{fieldName}",
                typeof(object),
                FieldAttributes.Assembly | FieldAttributes.Static
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
            Type returnType = ResolvePrivateMethodReturnType(method, isStatic: false);

            // Use Assembly (internal) visibility so nested async/generator state machines can access this method
            var methodBuilder = typeBuilder.DefineMethod(
                $"__private_{methodName}",
                MethodAttributes.Assembly | MethodAttributes.HideBySig,
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
            Type returnType = ResolvePrivateMethodReturnType(method, isStatic: true);

            // Use Assembly (internal) visibility so nested async/generator state machines can access this method
            var methodBuilder = typeBuilder.DefineMethod(
                $"__private_{methodName}",
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
                returnType,
                paramTypes
            );
            _classes.StaticPrivateMethods[className][methodName] = methodBuilder;
        }
    }

    /// <summary>
    /// Return type for a private (ES2022 <c>#</c>) method's MethodBuilder, matching the state machine its
    /// body is emitted through (see <see cref="EmitPrivateMethodBody"/>): a real <c>Task&lt;object&gt;</c>
    /// for <c>async</c>, <c>IEnumerable&lt;object&gt;</c> for a generator (instance or static, since static
    /// generator support landed in #692), <c>IAsyncEnumerable&lt;object&gt;</c> for an async generator —
    /// mirroring <c>DefineClassMethodsOnly</c>'s public-method logic (#720). The one exception is a
    /// <em>static</em> async generator: that state machine is instance-only (#761), so it stays on the
    /// linear path and keeps the plain <c>object</c> slot to avoid a return-type/stack mismatch for an
    /// empty (yield-free) body.
    /// </summary>
    private Type ResolvePrivateMethodReturnType(Stmt.Function method, bool isStatic)
    {
        if (method.IsAsync && method.IsGenerator)
            return isStatic ? typeof(object) : _types.IAsyncEnumerableOfObject;
        if (method.IsAsync)
            return _types.TaskOfObject;
        if (method.IsGenerator)
            return _types.IEnumerableOfObject;
        return typeof(object);
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
    /// Collects <c>@DotNetOverload("...")</c> hints from a <c>@DotNetType</c> shim
    /// class, keyed by TS method name (<c>"constructor"</c> for the constructor).
    /// Mirrors <c>Interpreter.DotNet.cs</c>'s <c>ExtractOverloadHints</c>.
    /// </summary>
    private static Dictionary<string, string> GetOverloadHints(Stmt.Class classStmt)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var method in classStmt.Methods)
        {
            if (method.Decorators == null) continue;
            foreach (var decorator in method.Decorators)
            {
                if (decorator.Expression is Expr.Call call &&
                    call.Callee is Expr.Variable v &&
                    v.Name.Lexeme == "DotNetOverload" &&
                    call.Arguments.Count == 1 &&
                    call.Arguments[0] is Expr.Literal { Value: string hint })
                {
                    result[method.Name.Lexeme] = hint;
                    break;
                }
            }
        }
        return result;
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
            var provided = _types.Resolve(clrTypeName);
            if (provided != null) return provided;
        }
        catch
        {
            // Fall through to broader lookup below.
        }

        // The remaining lookup exists for managed embedders whose host assembly owns
        // the requested type. Native compilation supports the TypeProvider/BCL path
        // above but cannot consume arbitrary executable assemblies.
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            return null;

        return TryResolveManagedHostType(clrTypeName);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Only managed embedders reach this fallback; Native AOT returns after the supported TypeProvider/BCL lookup.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2057",
        Justification = "Only managed embedders reach this fallback; Native AOT returns after the supported TypeProvider/BCL lookup.")]
    private static Type? TryResolveManagedHostType(string clrTypeName)
    {
        // Type.GetType only searches the executing assembly + mscorlib by default.
        // Search all currently loaded assemblies too, so types in referencing projects
        // (e.g., a host app with its own .NET domain model) are resolvable just like in
        // interpreter mode via DotNetTypeRegistry.
        var byGetType = Type.GetType(clrTypeName, throwOnError: false);
        if (byGetType != null) return byGetType;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = assembly.GetType(clrTypeName, throwOnError: false);
            if (t != null) return t;
        }
        return null;
    }

    /// <summary>
    /// Returns the emitted Type for a built-in Error type name (Error, TypeError, etc.).
    /// </summary>
    private Type GetEmittedErrorType(string errorTypeName) => errorTypeName switch
    {
        "Error" => _runtime.TSErrorType,
        "TypeError" => _runtime.TSTypeErrorType,
        "RangeError" => _runtime.TSRangeErrorType,
        "ReferenceError" => _runtime.TSReferenceErrorType,
        "SyntaxError" => _runtime.TSSyntaxErrorType,
        "URIError" => _runtime.TSURIErrorType,
        "EvalError" => _runtime.TSEvalErrorType,
        "AggregateError" => _runtime.TSAggregateErrorType,
        _ => _runtime.TSErrorType
    };

    /// <summary>
    /// Returns the emitted ConstructorBuilder for a built-in Error type (takes a single string? message param).
    /// </summary>
    private ConstructorBuilder GetEmittedErrorConstructor(string errorTypeName) => errorTypeName switch
    {
        "Error" => _runtime.TSErrorCtorMessage,
        "TypeError" => _runtime.TSTypeErrorCtor,
        "RangeError" => _runtime.TSRangeErrorCtor,
        "ReferenceError" => _runtime.TSReferenceErrorCtor,
        "SyntaxError" => _runtime.TSSyntaxErrorCtor,
        "URIError" => _runtime.TSURIErrorCtor,
        "EvalError" => _runtime.TSEvalErrorCtor,
        "AggregateError" => _runtime.TSAggregateErrorCtor,
        _ => _runtime.TSErrorCtorMessage
    };

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
            return _types.MakeArrayType(elementType);
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

        return _types.MakeGenericType(baseType, resolvedArgs);
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

}

