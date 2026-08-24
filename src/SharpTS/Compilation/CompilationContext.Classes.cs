using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class CompilationContext
{
    // ============================================
    // Class Compilation State
    // ============================================

    /// <summary>
    /// Gets the class builders dictionary.
    /// </summary>
    /// <remarks>
    /// Consider using <see cref="ClassRegistry"/> methods like TryGetClass() for lookups.
    /// </remarks>
    public Dictionary<string, TypeBuilder> Classes { get; }

    /// <summary>Uniquely named emitted Types for class declarations whose value binding lives in a block local.</summary>
    public IReadOnlyDictionary<Parsing.Stmt.Class, TypeBuilder>? BlockScopedClassBuilders { get; set; }

    // For instance methods
    public FieldInfo? FieldsField { get; set; }
    public bool IsInstanceMethod { get; set; }

    /// <summary>
    /// Primitive-returning non-virtual method companions keyed by qualified class
    /// and JavaScript method name. Only analyzer-marked exact receiver calls may
    /// bypass the public virtual object-returning method.
    /// </summary>
    public Dictionary<string, Dictionary<string, MethodBuilder>>?
        TypedPrimitiveInstanceMethodCores { get; set; }

    /// <summary>
    /// True when the current method body is an inner <c>function</c> declaration
    /// emitted onto a display class. In that case <see cref="IsInstanceMethod"/> is
    /// also true (arg0 is the display-class self — needed for capture access), but
    /// <c>this</c> in the body is NOT arg0 — it's the dynamic receiver from the
    /// call site (or null for a bare call). <see cref="LocalVariableResolver.LoadThis"/>
    /// consults this flag to pick the thread-local <c>_currentFunctionThis</c>
    /// over the class-instance path.
    /// </summary>
    public bool IsInnerFunctionOnDisplayClass { get; set; }

    // For static members: current class being compiled
    public TypeBuilder? CurrentClassBuilder { get; set; }

    /// <summary>
    /// The TypeBuilder whose method body the context's <see cref="CompilationContext.IL"/>
    /// is generating, set ONLY when emitting directly into a class's own methods
    /// (instance/static methods, constructors, accessors). Null for top-level code,
    /// arrow display classes, and async/generator state machines — those bodies live on
    /// other types even though <see cref="CurrentClassBuilder"/> still points at the class.
    /// Used to decide whether the instantiated self-form of a generic class
    /// (e.g. <c>Stack&lt;!T&gt;</c>) is expressible for direct member dispatch (#178).
    /// </summary>
    public TypeBuilder? EmittingTypeBuilder { get; set; }

    // For inheritance: current class's superclass name (if any)
    public string? CurrentSuperclassName { get; set; }
    public bool CurrentSuperclassIsAnonymousEmptyClass { get; set; }

    // Current class name being compiled (needed for private member access)
    private string? _currentClassName;
    private string? _currentClassShortName;

    public string? CurrentClassName
    {
        get => _currentClassName;
        set
        {
            _currentClassName = value;
            _currentClassShortName = value?.Split('.').Last().Split('_').Last();
        }
    }

    /// <summary>
    /// The last segment of CurrentClassName after splitting on '.' and '_'.
    /// Cached to avoid repeated string allocations in private member access checks.
    /// </summary>
    public string? CurrentClassShortName => _currentClassShortName;

    /// <summary>Lexically enclosing classes whose private names remain visible.</summary>
    public IReadOnlyList<string>? EnclosingClassNames { get; set; }

    public string ResolvePrivateFieldOwner(string fallbackClassName, string fieldName)
    {
        if (ClassRegistry?.GetPrivateFieldNames(fallbackClassName)?.Contains(fieldName) == true
            || ClassRegistry?.TryGetStaticPrivateField(fallbackClassName, fieldName, out _) == true)
        {
            return fallbackClassName;
        }

        if (EnclosingClassNames != null)
        {
            foreach (var className in EnclosingClassNames)
            {
                if (ClassRegistry?.GetPrivateFieldNames(className)?.Contains(fieldName) == true
                    || ClassRegistry?.TryGetStaticPrivateField(className, fieldName, out _) == true)
                {
                    return className;
                }
            }
        }
        return fallbackClassName;
    }

    public string ResolvePrivateMethodOwner(string fallbackClassName, string methodName)
    {
        if (ClassRegistry?.TryGetPrivateMethod(fallbackClassName, methodName, out _) == true
            || ClassRegistry?.TryGetStaticPrivateMethod(fallbackClassName, methodName, out _) == true)
        {
            return fallbackClassName;
        }

        if (EnclosingClassNames != null)
        {
            foreach (var className in EnclosingClassNames)
            {
                if (ClassRegistry?.TryGetPrivateMethod(className, methodName, out _) == true
                    || ClassRegistry?.TryGetStaticPrivateMethod(className, methodName, out _) == true)
                {
                    return className;
                }
            }
        }
        return fallbackClassName;
    }

    // ============================================
    // @lock Decorator Support: Thread-safe Method Execution
    // ============================================

    // Instance sync lock fields (class name -> FieldBuilder for _syncLock)
    public Dictionary<string, FieldBuilder>? SyncLockFields { get; set; }

    // Instance async lock fields (class name -> FieldBuilder for _asyncLock)
    public Dictionary<string, FieldBuilder>? AsyncLockFields { get; set; }

    // Instance reentrancy tracking fields (class name -> FieldBuilder for _lockReentrancy)
    public Dictionary<string, FieldBuilder>? LockReentrancyFields { get; set; }

    // Static sync lock fields (class name -> FieldBuilder for _staticSyncLock)
    public Dictionary<string, FieldBuilder>? StaticSyncLockFields { get; set; }

    // Static async lock fields (class name -> FieldBuilder for _staticAsyncLock)
    public Dictionary<string, FieldBuilder>? StaticAsyncLockFields { get; set; }

    // Static reentrancy tracking fields (class name -> FieldBuilder for _staticLockReentrancy)
    public Dictionary<string, FieldBuilder>? StaticLockReentrancyFields { get; set; }

    /// <summary>
    /// Resolves a simple class name to its qualified name for lookup in the Classes dictionary.
    /// In multi-module compilation, class names are qualified with their module to avoid collisions.
    /// Also applies .NET namespace prefix if set via @Namespace directive.
    /// </summary>
    /// <remarks>
    /// This method delegates to <see cref="ClassRegistry"/>.ResolveClassName() when available.
    /// </remarks>
    public string ResolveClassName(string simpleClassName)
    {
        // Delegate to ClassRegistry if available
        if (ClassRegistry != null)
        {
            return ClassRegistry.ResolveClassName(simpleClassName);
        }

        string baseName;

        // If we have a module mapping, use it to create the qualified name
        if (ClassToModule != null && ClassToModule.TryGetValue(simpleClassName, out var modulePath))
        {
            string sanitizedModule = SanitizeModuleName(Path.GetFileNameWithoutExtension(modulePath));
            baseName = $"$M_{sanitizedModule}_{simpleClassName}";
        }
        else
        {
            baseName = simpleClassName;
        }

        // Apply .NET namespace if set
        if (DotNetNamespace != null)
        {
            return $"{DotNetNamespace}.{baseName}";
        }

        return baseName;
    }

    /// <summary>
    /// Gets the qualified class name for the current module context.
    /// Also applies .NET namespace if set via @Namespace directive.
    /// </summary>
    /// <remarks>
    /// NOTE: This method does NOT delegate to ClassRegistry because it depends on
    /// context-specific state (CurrentModulePath) that varies per CompilationContext instance.
    /// The ClassRegistry's closure reads from _modules.CurrentPath which may not be in sync.
    /// </remarks>
    public string GetQualifiedClassName(string simpleClassName)
    {
        // Do NOT delegate to ClassRegistry - this method depends on context-specific CurrentModulePath
        // which varies per CompilationContext instance. ClassRegistry's closure reads from
        // _modules.CurrentPath which is not always in sync with ctx.CurrentModulePath.

        string baseName;
        if (CurrentModulePath == null)
        {
            baseName = simpleClassName;
        }
        else
        {
            string sanitizedModule = SanitizeModuleName(Path.GetFileNameWithoutExtension(CurrentModulePath));
            baseName = $"$M_{sanitizedModule}_{simpleClassName}";
        }

        // Apply .NET namespace if set
        if (DotNetNamespace != null)
        {
            return $"{DotNetNamespace}.{baseName}";
        }

        return baseName;
    }

    /// <summary>
    /// Resolve an instance method by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceMethod(string className, string methodName)
    {
        return ClassRegistry?.ResolveInstanceMethod(className, methodName);
    }

    /// <summary>
    /// Resolve an instance getter by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceGetter(string className, string propertyName)
    {
        return ClassRegistry?.ResolveInstanceGetter(className, propertyName);
    }

    /// <summary>
    /// Resolve an instance setter by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceSetter(string className, string propertyName)
    {
        return ClassRegistry?.ResolveInstanceSetter(className, propertyName);
    }
}
