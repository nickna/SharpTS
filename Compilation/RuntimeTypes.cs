using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Represents a TypeScript function value that can be stored, passed, and invoked.
/// </summary>
/// <remarks>
/// Runtime wrapper for function references in compiled assemblies. Supports both static
/// methods (non-capturing functions) and instance methods (closures with display class).
/// Handles argument padding for default parameters and packing for rest parameters.
/// Used when functions are passed as values or stored in variables.
/// </remarks>
/// <seealso cref="RuntimeTypes"/>
/// <seealso cref="EmittedRuntime"/>
public class TSFunction
{
    private readonly object? _target;      // Display class instance (null for static)
    private readonly System.Reflection.MethodInfo _method;   // The actual method to invoke
    private readonly System.Reflection.MethodInvoker _invoker; // Optimized invoker

    public TSFunction(object? target, System.Reflection.MethodInfo method)
    {
        _target = target;
        _method = method;
        _invoker = RuntimeTypes.ReflectionCache.GetInvoker(method);
    }

    /// <summary>
    /// Creates a TSFunction for a static method.
    /// </summary>
    public TSFunction(System.Reflection.MethodInfo method) : this(null, method)
    {
    }

    /// <summary>
    /// Invoke the function with the given arguments.
    /// Missing arguments are padded with null to support default parameters.
    /// Excess arguments are packed into an array for rest parameters.
    /// </summary>
    public object? Invoke(params object?[] args)
    {
        try
        {
            var paramCount = _method.GetParameters().Length;
            object?[] finalArgs;
            object? invokeTarget;

            // 1. Handle "this" binding / static closure target
            // For static methods with a bound target (closures, async arrows),
            // prepend the target to the arguments
            if (_method.IsStatic && _target != null)
            {
                // Static method with bound first argument
                // We need to create a new array to prepend the target
                // TODO: Optimize with ArrayPool if this becomes a bottleneck
                finalArgs = new object?[args.Length + 1];
                finalArgs[0] = _target;
                Array.Copy(args, 0, finalArgs, 1, args.Length);
                invokeTarget = null;
            }
            else
            {
                finalArgs = args;
                invokeTarget = _target;
            }

            // 2. Handle Argument Count Mismatch (Padding / Trimming)
            if (finalArgs.Length != paramCount)
            {
                var adjustedArgs = new object?[paramCount];
                // Copy what we have, up to the limit
                int copyCount = Math.Min(finalArgs.Length, paramCount);
                if (copyCount > 0)
                {
                    Array.Copy(finalArgs, adjustedArgs, copyCount);
                }
                // Remaining slots are already null (default)
                
                // Use the adjusted array
                return _invoker.Invoke(invokeTarget, new Span<object?>(adjustedArgs));
            }

            // 3. Perfect Match
            return _invoker.Invoke(invokeTarget, new Span<object?>(finalArgs));
        }
        catch (System.Reflection.TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }

    /// <summary>
    /// Get the number of expected parameters.
    /// </summary>
    public int Arity => _method.GetParameters().Length;

    /// <summary>
    /// Gets the display class instance (for closures) or null for non-capturing functions.
    /// </summary>
    public object? Target => _target;

    /// <summary>
    /// Binds 'this' to the given object by setting the display class's 'this' field.
    /// Used for object method shorthand where 'this' should reference the containing object.
    /// </summary>
    /// <param name="thisValue">The object to bind as 'this'.</param>
    /// <returns>True if binding succeeded, false if there's no 'this' field to bind.</returns>
    public bool BindThis(object? thisValue)
    {
        if (_target == null) return false;

        // Find the 'this' field in the display class
        var thisField = RuntimeTypes.ReflectionCache.GetField(_target.GetType(), "this");
        if (thisField != null)
        {
            thisField.SetValue(_target, thisValue);
            return true;
        }
        return false;
    }

    public override string ToString() => "[Function]";
}

/// <summary>
/// Runtime support methods emitted into each compiled assembly.
/// </summary>
/// <remarks>
/// Provides TypeScript runtime semantics for compiled DLLs: console.log, type coercion
/// (Stringify, ToNumber, IsTruthy), operators (Add, Equals), array/object operations,
/// dynamic invocation, and Math functions. Methods are copied into generated assemblies
/// to enable standalone execution without SharpTS.dll dependency.
/// </remarks>
/// <seealso cref="EmittedRuntime"/>
/// <seealso cref="ILCompiler"/>
public static partial class RuntimeTypes
{
    private static readonly Random _random = new();
    private static readonly Dictionary<string, Type> _compiledTypes = [];

    // Symbol-keyed property storage: object -> (symbol -> value)
    private static readonly ConditionalWeakTable<object, Dictionary<object, object?>> _symbolStorage = new();

    // Cache for enum reverse mappings: enumName -> (value -> memberName)
    private static readonly Dictionary<string, Dictionary<double, string>> _enumReverseCache = [];

    private static Dictionary<object, object?> GetSymbolDict(object obj)
    {
        return _symbolStorage.GetOrCreateValue(obj);
    }

    public static void RegisterType(string name, Type type)
    {
        _compiledTypes[name] = type;
    }

    #region Type Emission

    public static void EmitAll(ModuleBuilder moduleBuilder)
    {
        // Runtime types are static methods in this class
        // No additional types need to be emitted - we use the existing RuntimeTypes class
    }

    #endregion
}