using System.Collections.Concurrent;
using System.Reflection;

namespace SharpTS.Compilation;

/// <summary>
/// Provides type resolution for IL compilation using runtime types.
/// </summary>
/// <remarks>
/// Uses typeof() directly for fast compilation. The AssemblyReferenceRewriter is used
/// as a post-processing step when --ref-asm is enabled to rewrite System.Private.CoreLib
/// references to SDK reference assemblies.
/// </remarks>
public class TypeProvider
{
    private readonly Assembly _coreAssembly;
    private readonly ConcurrentDictionary<string, Type> _typeCache = new();
    private readonly ConcurrentDictionary<(Type, string, Type[]), MethodInfo> _methodCache = new(new MethodCacheKeyComparer());
    private readonly ConcurrentDictionary<(Type, string), PropertyInfo> _propertyCache = new();
    private readonly ConcurrentDictionary<(Type, Type[]), ConstructorInfo> _ctorCache = new(new CtorCacheKeyComparer());

    /// <summary>
    /// Comparer for method cache keys with Type[] array comparison.
    /// </summary>
    private class MethodCacheKeyComparer : IEqualityComparer<(Type, string, Type[])>
    {
        public bool Equals((Type, string, Type[]) x, (Type, string, Type[]) y)
        {
            if (x.Item1 != y.Item1 || x.Item2 != y.Item2) return false;
            if (x.Item3.Length != y.Item3.Length) return false;
            for (int i = 0; i < x.Item3.Length; i++)
                if (x.Item3[i] != y.Item3[i]) return false;
            return true;
        }

        public int GetHashCode((Type, string, Type[]) obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Item1);
            hash.Add(obj.Item2);
            foreach (var t in obj.Item3)
                hash.Add(t);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Comparer for constructor cache keys with Type[] array comparison.
    /// </summary>
    private class CtorCacheKeyComparer : IEqualityComparer<(Type, Type[])>
    {
        public bool Equals((Type, Type[]) x, (Type, Type[]) y)
        {
            if (x.Item1 != y.Item1) return false;
            if (x.Item2.Length != y.Item2.Length) return false;
            for (int i = 0; i < x.Item2.Length; i++)
                if (x.Item2[i] != y.Item2[i]) return false;
            return true;
        }

        public int GetHashCode((Type, Type[]) obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Item1);
            foreach (var t in obj.Item2)
                hash.Add(t);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Gets the singleton TypeProvider instance.
    /// </summary>
    public static TypeProvider Runtime { get; } = new(typeof(object).Assembly);

    /// <summary>
    /// Gets the core assembly (System.Private.CoreLib).
    /// </summary>
    public Assembly CoreAssembly => _coreAssembly;

    private TypeProvider(Assembly coreAssembly)
    {
        _coreAssembly = coreAssembly;
    }

    #region Core Types

    public Type Object => Resolve("System.Object");
    public Type String => Resolve("System.String");
    public Type Double => Resolve("System.Double");
    public Type Boolean => Resolve("System.Boolean");
    public Type Int32 => Resolve("System.Int32");
    public Type Int64 => Resolve("System.Int64");
    public Type Void => Resolve("System.Void");
    public Type Char => Resolve("System.Char");
    public Type Byte => Resolve("System.Byte");
    public Type ValueType => Resolve("System.ValueType");

    #endregion

    #region Common Types

    public Type Type => Resolve("System.Type");
    public Type Delegate => Resolve("System.Delegate");
    public Type MulticastDelegate => Resolve("System.MulticastDelegate");
    public Type Attribute => Resolve("System.Attribute");
    public Type Exception => Resolve("System.Exception");
    public Type NotSupportedException => Resolve("System.NotSupportedException");
    public Type TargetInvocationException => Resolve("System.Reflection.TargetInvocationException");
    public Type DateTime => Resolve("System.DateTime");
    public Type TimeSpan => Resolve("System.TimeSpan");
    public Type Guid => Resolve("System.Guid");
    public Type Convert => Resolve("System.Convert");
    public Type Math => Resolve("System.Math");
    public Type Console => Resolve("System.Console");
    public Type Environment => Resolve("System.Environment");
    public Type Activator => Resolve("System.Activator");
    public Type Random => Resolve("System.Random");
    public Type Stopwatch => Resolve("System.Diagnostics.Stopwatch");
    public Type StackTrace => Resolve("System.Diagnostics.StackTrace");
    public Type StackFrame => Resolve("System.Diagnostics.StackFrame");
    public Type Interlocked => Resolve("System.Threading.Interlocked");
    public Type Version => Resolve("System.Version");
    public Type GC => Resolve("System.GC");
    public Type Process => Resolve("System.Diagnostics.Process");
    public Type ProcessStartInfo => Resolve("System.Diagnostics.ProcessStartInfo");

    #endregion

    #region Reflection Types

    public Type MethodBase => Resolve("System.Reflection.MethodBase");
    public Type MethodInfo => Resolve("System.Reflection.MethodInfo");
    public Type RuntimeMethodHandle => Resolve("System.RuntimeMethodHandle");
    public Type RuntimeTypeHandle => Resolve("System.RuntimeTypeHandle");

    #endregion

    #region Nullable and Arrays

    public Type NullableOpen => Resolve("System.Nullable`1");
    public Type ArrayType => Resolve("System.Array");
    public Type ObjectArray => MakeArrayType(Object);
    public Type StringArray => MakeArrayType(String);
    public Type BoolArray => MakeArrayType(Boolean);
    public Type DoubleArray => MakeArrayType(Double);
    public Type Int32Array => MakeArrayType(Int32);
    public Type StringSplitOptions => Resolve("System.StringSplitOptions");

    #endregion

    #region Collections

    public Type ListOpen => Resolve("System.Collections.Generic.List`1");
    public Type DictionaryOpen => Resolve("System.Collections.Generic.Dictionary`2");
    public Type HashSetOpen => Resolve("System.Collections.Generic.HashSet`1");
    public Type IEnumerableOpen => Resolve("System.Collections.Generic.IEnumerable`1");
    public Type IEnumeratorOpen => Resolve("System.Collections.Generic.IEnumerator`1");
    public Type ICollectionOpen => Resolve("System.Collections.Generic.ICollection`1");
    public Type KeyValuePairOpen => Resolve("System.Collections.Generic.KeyValuePair`2");
    public Type IEnumerable => Resolve("System.Collections.IEnumerable");
    public Type IEnumerator => Resolve("System.Collections.IEnumerator");
    public Type ConditionalWeakTableOpen => Resolve("System.Runtime.CompilerServices.ConditionalWeakTable`2");
    public Type ConcurrentDictionaryOpen => Resolve("System.Collections.Concurrent.ConcurrentDictionary`2");
    public Type IEqualityComparerOpen => Resolve("System.Collections.Generic.IEqualityComparer`1");
    public Type RuntimeHelpers => Resolve("System.Runtime.CompilerServices.RuntimeHelpers");

    public Type ListOfObject => MakeGenericType(ListOpen, Object);
    public Type ListOfObjectNullable => typeof(List<object?>);
    public Type SharpTSArray => typeof(SharpTS.Runtime.Types.SharpTSArray);
    public Type SharpTSObject => typeof(SharpTS.Runtime.Types.SharpTSObject);
    public Type IEqualityComparerOfObject => MakeGenericType(IEqualityComparerOpen, Object);
    public Type DictionaryObjectObject => MakeGenericType(DictionaryOpen, Object, Object);
    public Type DictionaryStringObject => MakeGenericType(DictionaryOpen, String, Object);
    public Type IEnumerableOfObject => MakeGenericType(IEnumerableOpen, Object);
    public Type IEnumeratorOfObject => MakeGenericType(IEnumeratorOpen, Object);
    public Type KeyValuePairStringObject => MakeGenericType(KeyValuePairOpen, String, Object);
    public Type DictionaryStringObjectEnumerator => DictionaryStringObject.GetMethod("GetEnumerator")!.ReturnType;
    public Type HashSetOfString => MakeGenericType(HashSetOpen, String);
    public Type HashSetOfObject => MakeGenericType(HashSetOpen, Object);
    public Type ConditionalWeakTableObjectObject => MakeGenericType(ConditionalWeakTableOpen, Object, Object);
    public Type ConditionalWeakTable => ConditionalWeakTableObjectObject;  // Alias for Object.freeze/seal tracking

    #endregion

    #region Tasks and Async

    public Type Task => Resolve("System.Threading.Tasks.Task");
    public Type TaskOpen => Resolve("System.Threading.Tasks.Task`1");
    public Type ValueTask => Resolve("System.Threading.Tasks.ValueTask");
    public Type ValueTaskOpen => Resolve("System.Threading.Tasks.ValueTask`1");
    public Type TaskCompletionSourceOpen => Resolve("System.Threading.Tasks.TaskCompletionSource`1");
    public Type CancellationToken => Resolve("System.Threading.CancellationToken");
    public Type CancellationTokenSource => Resolve("System.Threading.CancellationTokenSource");
    public Type TaskContinuationOptions => Resolve("System.Threading.Tasks.TaskContinuationOptions");
    public Type TaskScheduler => Resolve("System.Threading.Tasks.TaskScheduler");
    public Type Monitor => Resolve("System.Threading.Monitor");
    public Type IntPtr => Resolve("System.IntPtr");

    public Type TaskOfObject => MakeGenericType(TaskOpen, Object);
    public Type TaskCompletionSourceOfObject => MakeGenericType(TaskCompletionSourceOpen, Object);

    #endregion

    #region Async State Machine Types

    public Type AsyncTaskMethodBuilder => Resolve("System.Runtime.CompilerServices.AsyncTaskMethodBuilder");
    public Type AsyncTaskMethodBuilderOpen => Resolve("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1");
    public Type AsyncVoidMethodBuilder => Resolve("System.Runtime.CompilerServices.AsyncVoidMethodBuilder");
    public Type IAsyncStateMachine => Resolve("System.Runtime.CompilerServices.IAsyncStateMachine");
    public Type TaskAwaiter => Resolve("System.Runtime.CompilerServices.TaskAwaiter");
    public Type TaskAwaiterOpen => Resolve("System.Runtime.CompilerServices.TaskAwaiter`1");
    public Type TaskAwaiterOfObject => MakeGenericType(TaskAwaiterOpen, Object);

    // Specialized async types for Promises
    public Type AsyncTaskMethodBuilderOfObject => MakeGenericType(AsyncTaskMethodBuilderOpen, Object);
    public Type TaskOfObjectArray => MakeGenericType(TaskOpen, ObjectArray);
    public Type TaskAwaiterOfObjectArray => MakeGenericType(TaskAwaiterOpen, ObjectArray);
    public Type ListOfTaskOfObject => MakeGenericType(ListOpen, TaskOfObject);
    public Type TaskOfTaskOfObject => MakeGenericType(TaskOpen, TaskOfObject);
    public Type TaskAwaiterOfTaskOfObject => MakeGenericType(TaskAwaiterOpen, TaskOfObject);

    #endregion

    #region Async Enumerable Types (for async generators)

    public Type IAsyncEnumerableOpen => Resolve("System.Collections.Generic.IAsyncEnumerable`1");
    public Type IAsyncEnumeratorOpen => Resolve("System.Collections.Generic.IAsyncEnumerator`1");
    public Type IAsyncDisposable => Resolve("System.IAsyncDisposable");
    public Type ManualResetValueTaskSourceCoreOpen => Resolve("System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1");
    public Type IValueTaskSourceOpen => Resolve("System.Threading.Tasks.Sources.IValueTaskSource`1");
    public Type ValueTaskSourceStatus => Resolve("System.Threading.Tasks.Sources.ValueTaskSourceStatus");

    public Type IAsyncEnumerableOfObject => MakeGenericType(IAsyncEnumerableOpen, Object);
    public Type IAsyncEnumeratorOfObject => MakeGenericType(IAsyncEnumeratorOpen, Object);
    public Type ValueTaskOfBool => MakeGenericType(ValueTaskOpen, Boolean);
    public Type ValueTaskAwaiterOpen => Resolve("System.Runtime.CompilerServices.ValueTaskAwaiter`1");
    public Type ValueTaskAwaiterOfBool => MakeGenericType(ValueTaskAwaiterOpen, Boolean);
    public Type ManualResetValueTaskSourceCoreOfBool => MakeGenericType(ManualResetValueTaskSourceCoreOpen, Boolean);
    public Type IValueTaskSourceOfBool => MakeGenericType(IValueTaskSourceOpen, Boolean);

    #endregion

    #region Reflection Types (extended)

    public Type PropertyInfo => Resolve("System.Reflection.PropertyInfo");
    public Type FieldInfo => Resolve("System.Reflection.FieldInfo");
    public Type FieldInfoArray => FieldInfo.MakeArrayType();
    public Type ConstructorInfo => Resolve("System.Reflection.ConstructorInfo");
    public Type ParameterInfo => Resolve("System.Reflection.ParameterInfo");
    public Type Assembly => Resolve("System.Reflection.Assembly");
    public Type BindingFlags => Resolve("System.Reflection.BindingFlags");
    public Type Binder => Resolve("System.Reflection.Binder");
    public Type ParameterModifier => Resolve("System.Reflection.ParameterModifier");

    #endregion

    #region Date/Time Types

    public Type DateTimeKind => Resolve("System.DateTimeKind");

    #endregion

    #region Numeric Types

    public Type BigInteger => Resolve("System.Numerics.BigInteger");
    public Type Decimal => Resolve("System.Decimal");
    public Type Single => Resolve("System.Single");
    public Type UInt32 => Resolve("System.UInt32");
    public Type UInt64 => Resolve("System.UInt64");
    public Type Int16 => Resolve("System.Int16");
    public Type UInt16 => Resolve("System.UInt16");
    public Type SByte => Resolve("System.SByte");

    #endregion

    #region Regex

    public Type Regex => Resolve("System.Text.RegularExpressions.Regex");
    public Type RegexOptions => Resolve("System.Text.RegularExpressions.RegexOptions");
    public Type Match => Resolve("System.Text.RegularExpressions.Match");
    public Type MatchCollection => Resolve("System.Text.RegularExpressions.MatchCollection");
    public Type Group => Resolve("System.Text.RegularExpressions.Group");
    public Type GroupCollection => Resolve("System.Text.RegularExpressions.GroupCollection");

    #endregion

    #region Other Common Types

    public Type StringBuilder => Resolve("System.Text.StringBuilder");
    public Type StringComparison => Resolve("System.StringComparison");
    public Type Encoding => Resolve("System.Text.Encoding");
    public Type Stream => Resolve("System.IO.Stream");
    public Type TextReader => Resolve("System.IO.TextReader");
    public Type TextWriter => Resolve("System.IO.TextWriter");
    public Type IDictionary => Resolve("System.Collections.IDictionary");
    public Type IDictionaryEnumerator => Resolve("System.Collections.IDictionaryEnumerator");
    public Type DictionaryEntry => Resolve("System.Collections.DictionaryEntry");
    public Type IDisposable => Resolve("System.IDisposable");
    public Type Uri => Resolve("System.Uri");
    public Type UriKind => Resolve("System.UriKind");
    public Type OSPlatform => Resolve("System.Runtime.InteropServices.OSPlatform");
    public Type RuntimeInformation => Resolve("System.Runtime.InteropServices.RuntimeInformation");
    public Type StreamReader => Resolve("System.IO.StreamReader");

    #endregion

    #region IO Types (for built-in modules)

    public Type Path => Resolve("System.IO.Path");
    public Type Directory => Resolve("System.IO.Directory");
    public Type File => Resolve("System.IO.File");
    public Type FileInfo => Resolve("System.IO.FileInfo");
    public Type DirectoryInfo => Resolve("System.IO.DirectoryInfo");
    public Type FileNotFoundException => Resolve("System.IO.FileNotFoundException");
    public Type DirectoryNotFoundException => Resolve("System.IO.DirectoryNotFoundException");
    public Type IOException => Resolve("System.IO.IOException");

    #endregion

    #region JSON Serialization

    public Type JsonSerializer => Resolve("System.Text.Json.JsonSerializer");
    public Type JsonSerializerOptions => Resolve("System.Text.Json.JsonSerializerOptions");

    #endregion

    #region Func and Action Delegates

    public Type ActionOpen1 => Resolve("System.Action`1");
    public Type ActionOpen2 => Resolve("System.Action`2");
    public Type FuncOpen1 => Resolve("System.Func`1");
    public Type FuncOpen2 => Resolve("System.Func`2");
    public Type FuncOpen3 => Resolve("System.Func`3");

    // Specialized Action types for async continuations
    public Type ActionTaskOfObjectAndObject => MakeGenericType(ActionOpen2, TaskOfObject, Object);

    #endregion

    #region Type Resolution Methods

    /// <summary>
    /// Resolves a type by its full name.
    /// </summary>
    public Type Resolve(string fullName)
    {
        return _typeCache.GetOrAdd(fullName, ResolveCore);
    }

    private Type ResolveCore(string fullName)
    {
        var type = Type.GetType(fullName, throwOnError: false);
        if (type == null)
        {
            // Try common assemblies
            type = typeof(object).Assembly.GetType(fullName)
                ?? typeof(List<>).Assembly.GetType(fullName)
                ?? typeof(Task).Assembly.GetType(fullName)
                ?? typeof(System.Collections.Concurrent.ConcurrentDictionary<,>).Assembly.GetType(fullName)
                ?? typeof(System.Text.RegularExpressions.Regex).Assembly.GetType(fullName)
                ?? typeof(System.Numerics.BigInteger).Assembly.GetType(fullName)
                ?? typeof(System.Console).Assembly.GetType(fullName)
                ?? typeof(System.Text.StringBuilder).Assembly.GetType(fullName)
                ?? typeof(System.Convert).Assembly.GetType(fullName)
                ?? typeof(System.Text.Json.JsonSerializer).Assembly.GetType(fullName)
                ?? typeof(System.Diagnostics.Process).Assembly.GetType(fullName)
                ?? typeof(System.Diagnostics.Stopwatch).Assembly.GetType(fullName)
                ?? typeof(System.Uri).Assembly.GetType(fullName)
                ?? typeof(System.Runtime.InteropServices.RuntimeInformation).Assembly.GetType(fullName);
        }

        if (type == null)
            throw new InvalidOperationException($"Could not resolve type: {fullName}");

        return type;
    }

    /// <summary>
    /// Creates a generic type from an open generic definition and type arguments.
    /// </summary>
    public Type MakeGenericType(Type genericDefinition, params Type[] typeArguments)
    {
        return genericDefinition.MakeGenericType(typeArguments);
    }

    /// <summary>
    /// Creates an array type for the specified element type.
    /// </summary>
    public Type MakeArrayType(Type elementType)
    {
        return elementType.MakeArrayType();
    }

    /// <summary>
    /// Creates a nullable type for the specified value type.
    /// </summary>
    public Type MakeNullable(Type valueType)
    {
        return MakeGenericType(NullableOpen, valueType);
    }

    #endregion

    #region Method Resolution

    /// <summary>
    /// Gets a method from a type with the specified parameter types.
    /// </summary>
    public MethodInfo GetMethod(Type type, string name, params Type[] parameterTypes)
    {
        var key = (type, name, parameterTypes);
        return _methodCache.GetOrAdd(key, k =>
        {
            var method = k.Item1.GetMethod(k.Item2, k.Item3);
            if (method == null)
                throw new InvalidOperationException($"Could not find method {k.Item1.FullName}.{k.Item2}({string.Join(", ", k.Item3.Select(t => t.FullName))})");
            return method;
        });
    }

    /// <summary>
    /// Gets a method from a type by name only (for methods without overloads).
    /// WARNING: This will throw AmbiguousMatchException for overloaded methods.
    /// Use GetMethodNoParams() for parameterless methods that have overloads.
    /// </summary>
    public MethodInfo GetMethod(Type type, string name)
    {
        var method = type.GetMethod(name);
        if (method == null)
            throw new InvalidOperationException($"Could not find method {type.FullName}.{name}");
        return method;
    }

    /// <summary>
    /// Gets a parameterless method from a type. Safe to use for overloaded methods.
    /// </summary>
    public MethodInfo GetMethodNoParams(Type type, string name)
    {
        return GetMethod(type, name, EmptyTypes);
    }

    /// <summary>
    /// Gets a property from a type by name.
    /// </summary>
    public PropertyInfo GetProperty(Type type, string name)
    {
        var key = (type, name);
        return _propertyCache.GetOrAdd(key, k =>
        {
            var property = k.Item1.GetProperty(k.Item2);
            if (property == null)
                throw new InvalidOperationException($"Could not find property {k.Item1.FullName}.{k.Item2}");
            return property;
        });
    }

    /// <summary>
    /// Gets the getter method for a property from a type by name.
    /// </summary>
    public MethodInfo GetPropertyGetter(Type type, string name)
    {
        var property = GetProperty(type, name);
        var getter = property.GetGetMethod();
        if (getter == null)
            throw new InvalidOperationException($"Property {type.FullName}.{name} does not have a getter");
        return getter;
    }

    /// <summary>
    /// Gets the setter method for a property from a type by name.
    /// </summary>
    public MethodInfo GetPropertySetter(Type type, string name)
    {
        var property = GetProperty(type, name);
        var setter = property.GetSetMethod();
        if (setter == null)
            throw new InvalidOperationException($"Property {type.FullName}.{name} does not have a setter");
        return setter;
    }

    /// <summary>
    /// Gets a static method from a type by name (no parameters).
    /// </summary>
    public MethodInfo GetMethodStatic(Type type, string name)
    {
        var method = type.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (method == null)
            throw new InvalidOperationException($"Could not find static method {type.FullName}.{name}");
        return method;
    }

    /// <summary>
    /// Gets a generic instance method from a type by name.
    /// </summary>
    public MethodInfo GetGenericMethod(Type type, string name)
    {
        var method = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == name && m.IsGenericMethod);
        if (method == null)
            throw new InvalidOperationException($"Could not find generic method {type.FullName}.{name}");
        return method;
    }

    /// <summary>
    /// Gets a constructor from a type with the specified parameter types.
    /// </summary>
    public ConstructorInfo GetConstructor(Type type, params Type[] parameterTypes)
    {
        var key = (type, parameterTypes);
        return _ctorCache.GetOrAdd(key, k =>
        {
            var ctor = k.Item1.GetConstructor(k.Item2);
            if (ctor == null)
                throw new InvalidOperationException($"Could not find constructor {k.Item1.FullName}({string.Join(", ", k.Item2.Select(t => t.FullName))})");
            return ctor;
        });
    }

    /// <summary>
    /// Gets the parameterless constructor for a type.
    /// </summary>
    public ConstructorInfo GetDefaultConstructor(Type type)
    {
        return GetConstructor(type, Type.EmptyTypes);
    }

    /// <summary>
    /// Gets a field from a type by name.
    /// </summary>
    public FieldInfo GetField(Type type, string name)
    {
        var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
        if (field == null)
            throw new InvalidOperationException($"Could not find field {type.FullName}.{name}");
        return field;
    }

    #endregion

    #region Type Comparison

    /// <summary>
    /// Checks if a type is equivalent to System.Object.
    /// </summary>
    public bool IsObject(Type type) => type == Object || type.FullName == "System.Object";

    /// <summary>
    /// Checks if a type is equivalent to System.String.
    /// </summary>
    public bool IsString(Type type) => type == String || type.FullName == "System.String";

    /// <summary>
    /// Checks if a type is equivalent to System.Double.
    /// </summary>
    public bool IsDouble(Type type) => type == Double || type.FullName == "System.Double";

    /// <summary>
    /// Checks if a type is equivalent to System.Boolean.
    /// </summary>
    public bool IsBoolean(Type type) => type == Boolean || type.FullName == "System.Boolean";

    /// <summary>
    /// Checks if a type is equivalent to System.Void.
    /// </summary>
    public bool IsVoid(Type type) => type == Void || type.FullName == "System.Void";

    #endregion

    #region Empty Types Helper

    /// <summary>
    /// Gets an empty Type array (equivalent to Type.EmptyTypes).
    /// </summary>
    public Type[] EmptyTypes => Type.EmptyTypes;

    #endregion
}
