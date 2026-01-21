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

    // For instance methods
    public FieldInfo? FieldsField { get; set; }
    public bool IsInstanceMethod { get; set; }

    // For static members: current class being compiled
    public TypeBuilder? CurrentClassBuilder { get; set; }

    // For inheritance: current class's superclass name (if any)
    public string? CurrentSuperclassName { get; set; }

    // Current class name being compiled (needed for private member access)
    public string? CurrentClassName { get; set; }

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
