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
        if (_currentModulePath != null)
        {
            _classToModule[classStmt.Name.Lexeme] = _currentModulePath;
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
                _externalTypes[qualifiedClassName] = externalType;
                _externalTypes[classStmt.Name.Lexeme] = externalType; // Also register simple name

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

            if (_classBuilders.TryGetValue(qualifiedSuperclassName, out var superBuilder))
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
            _syncLockFields[className] = syncLockField;

            // Async lock using SemaphoreSlim (permits: 1, max: 1)
            var asyncLockField = typeBuilder.DefineField(
                "_asyncLock",
                typeof(SemaphoreSlim),
                FieldAttributes.Private | FieldAttributes.InitOnly
            );
            _asyncLockFields[className] = asyncLockField;

            // Reentrancy tracking using AsyncLocal<int>
            var reentrancyField = typeBuilder.DefineField(
                "_lockReentrancy",
                typeof(AsyncLocal<int>),
                FieldAttributes.Private | FieldAttributes.InitOnly
            );
            _lockReentrancyFields[className] = reentrancyField;
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
            _staticSyncLockFields[className] = staticSyncLockField;

            // Static async lock
            var staticAsyncLockField = typeBuilder.DefineField(
                "_staticAsyncLock",
                typeof(SemaphoreSlim),
                FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
            );
            _staticAsyncLockFields[className] = staticAsyncLockField;

            // Static reentrancy tracking
            var staticReentrancyField = typeBuilder.DefineField(
                "_staticLockReentrancy",
                typeof(AsyncLocal<int>),
                FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
            );
            _staticLockReentrancyFields[className] = staticReentrancyField;
        }

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
}
