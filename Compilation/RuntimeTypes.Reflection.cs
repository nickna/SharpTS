using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
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
                var mi = type.GetMethod($"get_{char.ToUpperInvariant(name[0])}{name[1..]}");
                return new CacheEntry<MethodInfo>(mi);
            });
            return entry.Value;
        }

        public static MethodInfo? GetSetter(Type type, string propertyName)
        {
            var cache = _setterCache.GetValue(type, _ => new ConcurrentDictionary<string, CacheEntry<MethodInfo>>());
            var entry = cache.GetOrAdd(propertyName, name => 
            {
                var mi = type.GetMethod($"set_{char.ToUpperInvariant(name[0])}{name[1..]}");
                return new CacheEntry<MethodInfo>(mi);
            });
            return entry.Value;
        }

        public static MethodInfo? GetMethod(Type type, string methodName)
        {
            var cache = _methodCache.GetValue(type, _ => new ConcurrentDictionary<string, CacheEntry<MethodInfo>>());
            var entry = cache.GetOrAdd(methodName, name => 
            {
                var mi = type.GetMethod(name);
                return new CacheEntry<MethodInfo>(mi);
            });
            return entry.Value;
        }

        public static FieldInfo? GetField(Type type, string fieldName)
        {
            var cache = _fieldCache.GetValue(type, _ => new ConcurrentDictionary<string, CacheEntry<FieldInfo>>());
            var entry = cache.GetOrAdd(fieldName, name => 
            {
                var fi = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static);
                return new CacheEntry<FieldInfo>(fi);
            });
            return entry.Value;
        }

        public static ConstructorInfo? GetConstructor(Type type)
        {
            var entry = _constructorCache.GetValue(type, t => 
            {
                var ctors = t.GetConstructors();
                var ctor = ctors.Length > 0 ? ctors[0] : null;
                return new CacheEntry<ConstructorInfo>(ctor);
            });
            return entry.Value;
        }

        public static FieldInfo[] GetBackingFields(Type type)
        {
            return _backingFieldsCache.GetValue(type, t => 
                t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                 .Where(f => f.Name.StartsWith("__"))
                 .ToArray());
        }

        public static MethodInvoker GetInvoker(MethodBase method)
        {
            return _invokerCache.GetValue(method, m => MethodInvoker.Create(m));
        }
    }
}