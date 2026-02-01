using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for Function.prototype (bind, call, apply).
/// </summary>
public static class FunctionBuiltIns
{
    private static readonly BuiltInMethod _bind = new("bind", 0, int.MaxValue, Bind);
    private static readonly BuiltInMethod _call = new("call", 0, int.MaxValue, Call);
    private static readonly BuiltInMethod _apply = new("apply", 0, 2, Apply);

    /// <summary>
    /// Gets a member from a function (bind, call, apply, length, name).
    /// </summary>
    public static object? GetMember(ISharpTSCallable receiver, string name)
    {
        return name switch
        {
            "bind" => _bind.Bind(receiver),
            "call" => _call.Bind(receiver),
            "apply" => _apply.Bind(receiver),
            "length" => (double)receiver.Arity(),
            "name" => GetFunctionName(receiver),
            _ => null
        };
    }

    private static string GetFunctionName(ISharpTSCallable callable)
    {
        return callable switch
        {
            SharpTSFunction fn => fn.ToString().Replace("<fn ", "").TrimEnd('>'),
            SharpTSArrowFunction arrow => arrow.ToString().Contains("<fn ")
                ? arrow.ToString().Replace("<fn ", "").TrimEnd('>')
                : "",
            BoundFunction bound => bound.Name,
            BuiltInMethod method => method.ToString().Replace("<built-in ", "").TrimEnd('>'),
            _ => ""
        };
    }

    /// <summary>
    /// Function.prototype.bind(thisArg, ...args)
    /// Returns a new function with 'this' bound and optional partial application.
    /// </summary>
    private static object? Bind(Interpreter interp, object? receiver, List<object?> args)
    {
        var callable = receiver as ISharpTSCallable
            ?? throw new Exception("Runtime Error: bind called on non-function.");

        var thisArg = args.Count > 0 ? args[0] : null;
        var boundArgs = args.Count > 1 ? args.Skip(1).ToList() : new List<object?>();

        // Arrow functions ignore thisArg (they capture 'this' from lexical scope)
        if (callable is SharpTSArrowFunction arrow && !arrow.HasOwnThis)
        {
            // Still create a bound function for partial application, but 'this' won't change
            return new BoundFunction(callable, null, boundArgs, ignoreThisArg: true);
        }

        return new BoundFunction(callable, thisArg, boundArgs);
    }

    /// <summary>
    /// Function.prototype.call(thisArg, ...args)
    /// Calls the function with the specified 'this' value and individual arguments.
    /// </summary>
    private static object? Call(Interpreter interp, object? receiver, List<object?> args)
    {
        var callable = receiver as ISharpTSCallable
            ?? throw new Exception("Runtime Error: call invoked on non-function.");

        var thisArg = args.Count > 0 ? args[0] : null;
        var callArgs = args.Count > 1 ? args.Skip(1).ToList() : new List<object?>();

        return InvokeWithThis(interp, callable, thisArg, callArgs);
    }

    /// <summary>
    /// Function.prototype.apply(thisArg, argsArray)
    /// Calls the function with the specified 'this' value and arguments as an array.
    /// </summary>
    private static object? Apply(Interpreter interp, object? receiver, List<object?> args)
    {
        var callable = receiver as ISharpTSCallable
            ?? throw new Exception("Runtime Error: apply invoked on non-function.");

        var thisArg = args.Count > 0 ? args[0] : null;
        var argsArray = args.Count > 1 ? args[1] : null;

        List<object?> callArgs;
        if (argsArray == null)
        {
            callArgs = new List<object?>();
        }
        else if (argsArray is SharpTSArray tsArray)
        {
            callArgs = new List<object?>(tsArray.Elements);
        }
        else if (argsArray is List<object?> list)
        {
            callArgs = new List<object?>(list);
        }
        else
        {
            throw new Exception("Runtime Error: apply second argument must be an array or null.");
        }

        return InvokeWithThis(interp, callable, thisArg, callArgs);
    }

    /// <summary>
    /// Invokes a callable with a specific 'this' value.
    /// </summary>
    private static object? InvokeWithThis(Interpreter interp, ISharpTSCallable callable, object? thisArg, List<object?> args)
    {
        // Arrow functions ignore thisArg
        if (callable is SharpTSArrowFunction arrow && !arrow.HasOwnThis)
        {
            return callable.Call(interp, args);
        }

        // For regular functions, we need to bind 'this'
        if (callable is SharpTSFunction fn)
        {
            // Create a temporary bound function
            var bound = new BoundFunction(fn, thisArg, new List<object?>());
            return bound.Call(interp, args);
        }

        if (callable is SharpTSArrowFunction arrowWithThis)
        {
            // Function expression with its own 'this'
            var bound = arrowWithThis.Bind(thisArg!);
            return bound.Call(interp, args);
        }

        // For built-in methods and other callables, just call directly
        return callable.Call(interp, args);
    }
}

/// <summary>
/// A function that has been bound to a specific 'this' value and/or has partial application.
/// </summary>
public class BoundFunction : ISharpTSCallable
{
    private readonly ISharpTSCallable _target;
    private readonly object? _thisArg;
    private readonly List<object?> _boundArgs;
    private readonly bool _ignoreThisArg;

    /// <summary>
    /// The name of the bound function (for Function.prototype.name).
    /// </summary>
    public string Name { get; }

    public BoundFunction(ISharpTSCallable target, object? thisArg, List<object?> boundArgs, bool ignoreThisArg = false)
    {
        _target = target;
        _thisArg = thisArg;
        _boundArgs = boundArgs;
        _ignoreThisArg = ignoreThisArg;

        // Get base function name and prefix with "bound "
        var baseName = GetBaseName(target);
        Name = string.IsNullOrEmpty(baseName) ? "bound " : $"bound {baseName}";
    }

    private static string GetBaseName(ISharpTSCallable target)
    {
        return target switch
        {
            SharpTSFunction fn => fn.ToString().Replace("<fn ", "").TrimEnd('>'),
            SharpTSArrowFunction arrow => arrow.ToString().Contains("<fn ")
                ? arrow.ToString().Replace("<fn ", "").TrimEnd('>')
                : "",
            BoundFunction bound => bound.Name.StartsWith("bound ")
                ? bound.Name.Substring(6)
                : bound.Name,
            _ => ""
        };
    }

    public int Arity()
    {
        int baseArity = _target.Arity();
        return Math.Max(0, baseArity - _boundArgs.Count);
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Combine bound args with call args
        var combinedArgs = new List<object?>(_boundArgs);
        combinedArgs.AddRange(arguments);

        // Handle binding 'this' for the target function
        if (!_ignoreThisArg && _thisArg != null)
        {
            if (_target is SharpTSFunction fn)
            {
                // For regular functions, we need to invoke with proper 'this' binding
                // Since SharpTSFunction.Bind requires SharpTSInstance, we need a workaround
                // We'll create a special environment handling in the BoundFunction call
                var boundFn = CreateBoundSharpTSFunction(fn, _thisArg);
                return boundFn.Call(interpreter, combinedArgs);
            }

            if (_target is SharpTSArrowFunction arrow && arrow.HasOwnThis)
            {
                var boundArrow = arrow.Bind(_thisArg);
                return boundArrow.Call(interpreter, combinedArgs);
            }
        }

        // For arrow functions or when no 'this' binding needed
        return _target.Call(interpreter, combinedArgs);
    }

    /// <summary>
    /// Creates a SharpTSFunction-like callable that binds 'this' to any object.
    /// </summary>
    private static ISharpTSCallable CreateBoundSharpTSFunction(SharpTSFunction fn, object thisArg)
    {
        // We wrap the function in a BoundSharpTSFunctionWrapper
        return new BoundSharpTSFunctionWrapper(fn, thisArg);
    }

    public override string ToString() => $"<fn {Name}>";
}

/// <summary>
/// Internal wrapper to call a SharpTSFunction with an arbitrary 'this' value.
/// </summary>
internal class BoundSharpTSFunctionWrapper : ISharpTSCallable
{
    private readonly SharpTSFunction _fn;
    private readonly object _thisArg;

    public BoundSharpTSFunctionWrapper(SharpTSFunction fn, object thisArg)
    {
        _fn = fn;
        _thisArg = thisArg;
    }

    public int Arity() => _fn.Arity();

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Create a wrapper instance if needed
        if (_thisArg is SharpTSInstance instance)
        {
            var boundFn = _fn.Bind(instance);
            return boundFn.Call(interpreter, arguments);
        }

        // For non-instance 'this' values, we need to set up the environment manually
        // Create a synthetic instance that wraps the actual object
        var syntheticInstance = new SyntheticThisInstance(_thisArg);
        var boundFn2 = _fn.Bind(syntheticInstance);
        return boundFn2.Call(interpreter, arguments);
    }
}

/// <summary>
/// Synthetic instance wrapper for binding 'this' to non-instance values.
/// This allows binding functions to plain objects like { name: "foo" }.
/// </summary>
internal class SyntheticThisInstance : SharpTSInstance
{
    private readonly object _actualThis;
    private static readonly SharpTSClass _dummyClass = CreateDummyClass();

    public SyntheticThisInstance(object actualThis)
        : base(_dummyClass)
    {
        _actualThis = actualThis;

        // Copy fields from the actual 'this' object into this instance
        if (actualThis is SharpTSObject obj)
        {
            foreach (var kvp in obj.Fields)
            {
                SetRawField(kvp.Key, kvp.Value);
            }
        }
        else if (actualThis is Dictionary<string, object?> dict)
        {
            foreach (var kvp in dict)
            {
                SetRawField(kvp.Key, kvp.Value);
            }
        }
    }

    private static SharpTSClass CreateDummyClass()
    {
        // Create a minimal dummy class for the base constructor
        return new SharpTSClass(
            name: "SyntheticThis",
            superclass: null,
            methods: new Dictionary<string, ISharpTSCallable>(),
            staticMethods: new Dictionary<string, ISharpTSCallable>(),
            staticProperties: new Dictionary<string, object?>(),
            getters: null,
            setters: null,
            isAbstract: false);
    }
}
