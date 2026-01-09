using System.Reflection;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Methods

    public static object? InvokeMethod(object? receiver, string methodName, int argCount)
    {
        // This is a simplified implementation
        // In practice, arguments would need to be passed differently
        if (receiver == null) return null;

        // Array methods
        if (receiver is List<object?> list)
        {
            return methodName switch
            {
                "push" => null, // Would need args
                "pop" => list.Count > 0 ? list[^1] : null,
                "length" => (double)list.Count,
                _ => null
            };
        }

        // String methods
        if (receiver is string s)
        {
            return methodName switch
            {
                "toUpperCase" => s.ToUpperInvariant(),
                "toLowerCase" => s.ToLowerInvariant(),
                "trim" => s.Trim(),
                "length" => (double)s.Length,
                _ => null
            };
        }

        return null;
    }

    public static object? InvokeValue(object? value, object?[] args)
    {
        // If value is a TSFunction, call its Invoke method
        if (value is TSFunction func)
        {
            return func.Invoke(args);
        }

        // If value is a bound method (from CreateBoundMethod)
        if (value is Func<object?[], object?> boundMethod)
        {
            return boundMethod(args);
        }

        // Handle Delegate types (for bound methods created dynamically)
        if (value is Delegate del)
        {
            // DynamicInvoke is slow, but we can't easily optimize arbitrary delegates
            return del.DynamicInvoke(new object[] { args });
        }

        // If value is null, return null
        if (value == null)
        {
            return null;
        }

        // For other callable types (shouldn't normally happen)
        throw new InvalidOperationException($"Cannot invoke value of type {value.GetType().Name}");
    }

    private static object CreateBoundMethod(object receiver, MethodInfo method)
    {
        // Create a delegate bound to the receiver using MethodInvoker
        // We cache the invoker for this method
        var invoker = ReflectionCache.GetInvoker(method);
        
        return new Func<object?[], object?>(args =>
        {
            // Invoke directly using the optimized invoker
            return invoker.Invoke(receiver, new Span<object?>(args));
        });
    }

    public static object? GetSuperMethod(object? instance, string methodName)
    {
        if (instance == null) return null;

        var type = instance.GetType();
        var baseType = type.BaseType;
        if (baseType == null || baseType == typeof(object)) return null;

        var method = ReflectionCache.GetMethod(baseType, methodName);
        if (method != null)
        {
            return CreateBoundMethod(instance, method);
        }

        return null;
    }

    #endregion

    #region Instantiation

    public static object? CreateInstance(object?[] args, string className)
    {
        if (_compiledTypes.TryGetValue(className, out var type))
        {
            try
            {
                // Find the constructor and pad args with nulls for default parameters
                var ctor = ReflectionCache.GetConstructor(type);
                if (ctor != null)
                {
                    var paramCount = ctor.GetParameters().Length;
                    var invoker = ReflectionCache.GetInvoker(ctor);

                    if (args.Length < paramCount)
                    {
                        var paddedArgs = new object?[paramCount];
                        Array.Copy(args, paddedArgs, args.Length);
                        return invoker.Invoke(null, new Span<object?>(paddedArgs));
                    }
                    
                    // Invoker for constructor handles "obj" as null (for static ctors) or unused?
                    // Actually for ConstructorInfo, invoker.Invoke returns the new instance
                    return invoker.Invoke(null, new Span<object?>(args));
                }
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    #endregion
}
