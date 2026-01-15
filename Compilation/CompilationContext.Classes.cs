using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class CompilationContext
{
    // ============================================
    // Class Compilation State
    // ============================================

    public Dictionary<string, TypeBuilder> Classes { get; }

    // For instance methods
    public FieldInfo? FieldsField { get; set; }
    public bool IsInstanceMethod { get; set; }

    // For static members: current class being compiled
    public TypeBuilder? CurrentClassBuilder { get; set; }

    // For inheritance: current class's superclass name (if any)
    public string? CurrentSuperclassName { get; set; }

    // Static fields by class name -> field name -> FieldBuilder
    public Dictionary<string, Dictionary<string, FieldBuilder>>? StaticFields { get; set; }

    // Static methods by class name -> method name -> MethodBuilder
    public Dictionary<string, Dictionary<string, MethodBuilder>>? StaticMethods { get; set; }

    // Constructor overloads for default parameters: class name -> list of overload constructors
    public Dictionary<string, List<ConstructorBuilder>>? ConstructorOverloads { get; set; }

    // Class constructors by class name -> ConstructorBuilder
    public Dictionary<string, ConstructorBuilder>? ClassConstructors { get; set; }

    // Instance methods for direct dispatch (class name -> method name -> MethodBuilder)
    public Dictionary<string, Dictionary<string, MethodBuilder>>? InstanceMethods { get; set; }

    // Instance getters for direct dispatch (class name -> property name -> MethodBuilder)
    public Dictionary<string, Dictionary<string, MethodBuilder>>? InstanceGetters { get; set; }

    // Instance setters for direct dispatch (class name -> property name -> MethodBuilder)
    public Dictionary<string, Dictionary<string, MethodBuilder>>? InstanceSetters { get; set; }

    // Class superclass mapping (class name -> superclass name or null)
    public Dictionary<string, string?>? ClassSuperclass { get; set; }

    // Track generic params per class for instantiation
    public Dictionary<string, GenericTypeParameterBuilder[]>? ClassGenericParams { get; set; }

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
    public string ResolveClassName(string simpleClassName)
    {
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
    public string GetQualifiedClassName(string simpleClassName)
    {
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
        string? current = className;
        while (current != null)
        {
            if (InstanceMethods?.TryGetValue(current, out var methods) == true &&
                methods.TryGetValue(methodName, out var method))
                return method;
            current = ClassSuperclass?.GetValueOrDefault(current);
        }
        return null;
    }

    /// <summary>
    /// Resolve an instance getter by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceGetter(string className, string propertyName)
    {
        string? current = className;
        while (current != null)
        {
            if (InstanceGetters?.TryGetValue(current, out var getters) == true &&
                getters.TryGetValue(propertyName, out var getter))
                return getter;
            current = ClassSuperclass?.GetValueOrDefault(current);
        }
        return null;
    }

    /// <summary>
    /// Resolve an instance setter by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceSetter(string className, string propertyName)
    {
        string? current = className;
        while (current != null)
        {
            if (InstanceSetters?.TryGetValue(current, out var setters) == true &&
                setters.TryGetValue(propertyName, out var setter))
                return setter;
            current = ClassSuperclass?.GetValueOrDefault(current);
        }
        return null;
    }
}
