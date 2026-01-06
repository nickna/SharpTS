using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for the Reflect metadata API (reflect-metadata polyfill).
/// Used by decorators for storing and retrieving metadata on classes and class members.
/// </summary>
/// <remarks>
/// Implements the reflect-metadata API:
/// - Reflect.defineMetadata(key, value, target, propertyKey?)
/// - Reflect.getMetadata(key, target, propertyKey?)
/// - Reflect.hasMetadata(key, target, propertyKey?)
/// - Reflect.getMetadataKeys(target, propertyKey?)
/// - Reflect.metadata(key, value) - decorator factory
/// </remarks>
public static class ReflectBuiltIns
{
    /// <summary>
    /// Gets a static method from the Reflect namespace.
    /// </summary>
    public static ISharpTSCallable? GetStaticMethod(string name)
    {
        return name switch
        {
            "defineMetadata" => new BuiltInMethod("defineMetadata", 3, 4, (_, _, args) =>
            {
                var key = args[0]?.ToString() ?? "";
                var value = args[1];
                var target = args[2] ?? throw new Exception("Runtime Error: Reflect.defineMetadata requires a target.");
                var propertyKey = args.Count > 3 ? args[3]?.ToString() : null;

                ReflectMetadataStore.Instance.DefineMetadata(key, value, target, propertyKey);
                return null;
            }),

            "getMetadata" => new BuiltInMethod("getMetadata", 2, 3, (_, _, args) =>
            {
                var key = args[0]?.ToString() ?? "";
                var target = args[1] ?? throw new Exception("Runtime Error: Reflect.getMetadata requires a target.");
                var propertyKey = args.Count > 2 ? args[2]?.ToString() : null;

                return ReflectMetadataStore.Instance.GetMetadata(key, target, propertyKey);
            }),

            "getOwnMetadata" => new BuiltInMethod("getOwnMetadata", 2, 3, (_, _, args) =>
            {
                var key = args[0]?.ToString() ?? "";
                var target = args[1] ?? throw new Exception("Runtime Error: Reflect.getOwnMetadata requires a target.");
                var propertyKey = args.Count > 2 ? args[2]?.ToString() : null;

                return ReflectMetadataStore.Instance.GetOwnMetadata(key, target, propertyKey);
            }),

            "hasMetadata" => new BuiltInMethod("hasMetadata", 2, 3, (_, _, args) =>
            {
                var key = args[0]?.ToString() ?? "";
                var target = args[1] ?? throw new Exception("Runtime Error: Reflect.hasMetadata requires a target.");
                var propertyKey = args.Count > 2 ? args[2]?.ToString() : null;

                return ReflectMetadataStore.Instance.HasMetadata(key, target, propertyKey);
            }),

            "hasOwnMetadata" => new BuiltInMethod("hasOwnMetadata", 2, 3, (_, _, args) =>
            {
                var key = args[0]?.ToString() ?? "";
                var target = args[1] ?? throw new Exception("Runtime Error: Reflect.hasOwnMetadata requires a target.");
                var propertyKey = args.Count > 2 ? args[2]?.ToString() : null;

                return ReflectMetadataStore.Instance.HasOwnMetadata(key, target, propertyKey);
            }),

            "getMetadataKeys" => new BuiltInMethod("getMetadataKeys", 1, 2, (_, _, args) =>
            {
                var target = args[0] ?? throw new Exception("Runtime Error: Reflect.getMetadataKeys requires a target.");
                var propertyKey = args.Count > 1 ? args[1]?.ToString() : null;

                var keys = ReflectMetadataStore.Instance.GetMetadataKeys(target, propertyKey);
                return new SharpTSArray(keys.Cast<object?>().ToList());
            }),

            "getOwnMetadataKeys" => new BuiltInMethod("getOwnMetadataKeys", 1, 2, (_, _, args) =>
            {
                var target = args[0] ?? throw new Exception("Runtime Error: Reflect.getOwnMetadataKeys requires a target.");
                var propertyKey = args.Count > 1 ? args[1]?.ToString() : null;

                var keys = ReflectMetadataStore.Instance.GetOwnMetadataKeys(target, propertyKey);
                return new SharpTSArray(keys.Cast<object?>().ToList());
            }),

            "deleteMetadata" => new BuiltInMethod("deleteMetadata", 2, 3, (_, _, args) =>
            {
                var key = args[0]?.ToString() ?? "";
                var target = args[1] ?? throw new Exception("Runtime Error: Reflect.deleteMetadata requires a target.");
                var propertyKey = args.Count > 2 ? args[2]?.ToString() : null;

                return ReflectMetadataStore.Instance.DeleteMetadata(key, target, propertyKey);
            }),

            // Decorator factory: Reflect.metadata(key, value) returns a decorator
            "metadata" => new BuiltInMethod("metadata", 2, (interpreter, _, args) =>
            {
                var key = args[0]?.ToString() ?? "";
                var value = args[1];

                // Return a decorator function that defines the metadata
                return new MetadataDecorator(key, value);
            }),

            _ => null
        };
    }

    /// <summary>
    /// A decorator that defines metadata on the target.
    /// Used with Reflect.metadata(key, value) factory.
    /// </summary>
    private class MetadataDecorator(string key, object? value) : ISharpTSCallable
    {
        public int Arity() => 2; // For Stage 3: (value, context), or Legacy: (target, propertyKey?, descriptor?)

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            // This decorator just defines metadata on the target
            // Works for both Legacy and Stage 3 decorators
            if (arguments.Count >= 1 && arguments[0] != null)
            {
                var target = arguments[0]!;
                string? propertyKey = null;

                // For method/property decorators, second arg is property key (Legacy)
                // or context object (Stage 3)
                if (arguments.Count >= 2 && arguments[1] is string propKey)
                {
                    propertyKey = propKey;
                }
                else if (arguments.Count >= 2 && arguments[1] is SharpTSObject context)
                {
                    // Stage 3 context object has 'name' property
                    var name = context.Get("name");
                    if (name is string contextName)
                    {
                        propertyKey = contextName;
                    }
                }

                ReflectMetadataStore.Instance.DefineMetadata(key, value, target, propertyKey);
            }

            // Return undefined (decorators that don't modify return void/undefined)
            return null;
        }
    }
}
