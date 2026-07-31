using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    /// <summary>
    /// Clears the internal reflection caches. Public surface so Test262 (and
    /// any external collectible-ALC consumer) can release pinned types after
    /// unloading. See <see cref="ReflectionCache.Clear"/> for details.
    /// </summary>
    public static void ClearReflectionCaches() => ReflectionCache.Clear();

    internal static class ReflectionCache
    {
        // Wrapper to allow caching "null" results (member not found) in ConcurrentDictionary/CWT
        private class CacheEntry<T>
        {
            public readonly T? Value;
            public CacheEntry(T? value) { Value = value; }
        }

        // Cache for type members using ConditionalWeakTable to allow type unloading and prevent unbounded growth
        // Key: Type (holds the cache alive as long as Type is alive)
        // Value: Dictionary of members by name
        
        private static readonly ConditionalWeakTable<Type, ConcurrentDictionary<string, CacheEntry<MethodInfo>>> _getterCache = new();
        private static readonly ConditionalWeakTable<Type, ConcurrentDictionary<string, CacheEntry<MethodInfo>>> _setterCache = new();
        private static readonly ConditionalWeakTable<Type, ConcurrentDictionary<string, CacheEntry<MethodInfo>>> _methodCache = new();
        private static readonly ConditionalWeakTable<Type, ConcurrentDictionary<string, CacheEntry<FieldInfo>>> _fieldCache = new();
        
        // Single-item caches
        private static readonly ConditionalWeakTable<Type, CacheEntry<ConstructorInfo>> _constructorCache = new();
        private static readonly ConditionalWeakTable<Type, FieldInfo[]> _backingFieldsCache = new();

        // Invoker cache
        // Key: MethodBase (holds the cache alive as long as the MethodBase/Type is alive)
        private static readonly ConditionalWeakTable<MethodBase, MethodInvoker> _invokerCache = new();

        public static MethodInfo? GetGetter(Type type, string propertyName)
        {
            var cache = _getterCache.GetValue(type, _ => new ConcurrentDictionary<string, CacheEntry<MethodInfo>>());
            var entry = cache.GetOrAdd(propertyName, name => 
            {
                var mi = ManagedOutputRuntimeReflection.GetPublicMethodByName(
                    type, $"get_{char.ToUpperInvariant(name[0])}{name[1..]}");
                return new CacheEntry<MethodInfo>(mi);
            });
            return entry.Value;
        }

        public static MethodInfo? GetSetter(Type type, string propertyName)
        {
            var cache = _setterCache.GetValue(type, _ => new ConcurrentDictionary<string, CacheEntry<MethodInfo>>());
            var entry = cache.GetOrAdd(propertyName, name => 
            {
                var mi = ManagedOutputRuntimeReflection.GetPublicMethodByName(
                    type, $"set_{char.ToUpperInvariant(name[0])}{name[1..]}");
                return new CacheEntry<MethodInfo>(mi);
            });
            return entry.Value;
        }

        public static MethodInfo? GetMethod(Type type, string methodName)
        {
            var cache = _methodCache.GetValue(type, _ => new ConcurrentDictionary<string, CacheEntry<MethodInfo>>());
            var entry = cache.GetOrAdd(methodName, name => 
            {
                var mi = ManagedOutputRuntimeReflection.GetPublicMethodByName(type, name);
                return new CacheEntry<MethodInfo>(mi);
            });
            return entry.Value;
        }

        public static FieldInfo? GetField(Type type, string fieldName)
        {
            var cache = _fieldCache.GetValue(type, _ => new ConcurrentDictionary<string, CacheEntry<FieldInfo>>());
            var entry = cache.GetOrAdd(fieldName, name => 
            {
                var fi = ManagedOutputRuntimeReflection.GetFieldByName(
                    type,
                    name,
                    BindingFlags.NonPublic | BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.Static);
                return new CacheEntry<FieldInfo>(fi);
            });
            return entry.Value;
        }

        public static ConstructorInfo? GetConstructor(Type type)
        {
            var entry = _constructorCache.GetValue(type, t => 
            {
                var ctor = ManagedOutputRuntimeReflection.GetFirstPublicConstructor(t);
                return new CacheEntry<ConstructorInfo>(ctor);
            });
            return entry.Value;
        }

        public static FieldInfo[] GetBackingFields(Type type)
        {
            return _backingFieldsCache.GetValue(
                type,
                t => ManagedOutputRuntimeReflection.GetNonPublicInstanceFieldsWithPrefix(
                    t, "__"));
        }

        public static MethodInvoker GetInvoker(MethodBase method)
        {
            return _invokerCache.GetValue(method, m => MethodInvoker.Create(m));
        }

        /// <summary>
        /// Clears all cached reflection entries. Required after unloading
        /// collectible AssemblyLoadContexts: the cache values (MethodInfo,
        /// FieldInfo, etc.) hold strong back-references to their declaring
        /// Type, which defeats <see cref="ConditionalWeakTable{TKey, TValue}"/>'s
        /// weak-key semantics and pins test-emitted types indefinitely.
        ///
        /// Tradeoff: built-in SharpTS types lose their cache entries too and
        /// re-populate on next access. That cost is negligible compared to
        /// the unbounded memory growth this avoids during Test262 regen.
        /// See issue #109.
        /// </summary>
        public static void Clear()
        {
            _getterCache.Clear();
            _setterCache.Clear();
            _methodCache.Clear();
            _fieldCache.Clear();
            _constructorCache.Clear();
            _backingFieldsCache.Clear();
            _invokerCache.Clear();
        }
    }
}
