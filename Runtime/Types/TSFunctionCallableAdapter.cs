using SharpTS.Compilation;
using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Adapts a TSFunction (from compiled code) to implement ISharpTSCallable.
/// </summary>
/// <remarks>
/// This adapter allows compiled TypeScript functions (TSFunction instances)
/// to be used where ISharpTSCallable is expected, such as in HTTP server
/// request handlers and event listeners.
/// </remarks>
public class TSFunctionCallableAdapter : ISharpTSCallable
{
    private readonly TSFunction _function;

    public TSFunctionCallableAdapter(TSFunction function)
    {
        _function = function ?? throw new ArgumentNullException(nameof(function));
    }

    public int Arity() => _function.Arity;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // TSFunction.Invoke takes object?[] - convert the List
        return _function.Invoke(arguments.ToArray());
    }

    /// <summary>
    /// Creates an ISharpTSCallable from an object that may be a TSFunction,
    /// ISharpTSCallable, or null.
    /// </summary>
    /// <param name="callback">The callback object.</param>
    /// <returns>An ISharpTSCallable, or a no-op handler if null.</returns>
    public static ISharpTSCallable WrapCallback(object? callback)
    {
        if (callback == null)
        {
            return new NoOpHandler();
        }

        if (callback is ISharpTSCallable callable)
        {
            return callable;
        }

        if (callback is TSFunction tsFunc)
        {
            return new TSFunctionCallableAdapter(tsFunc);
        }

        // Fallback - try to invoke it reflectively
        return new ReflectiveCallableAdapter(callback);
    }

    /// <summary>
    /// A no-op request handler for servers created without a callback.
    /// </summary>
    private class NoOpHandler : ISharpTSCallable
    {
        public int Arity() => 2;

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            // Do nothing - user must add 'request' event listener
            return null;
        }
    }

    /// <summary>
    /// Fallback adapter that uses reflection to invoke the callback.
    /// </summary>
    private class ReflectiveCallableAdapter : ISharpTSCallable
    {
        private readonly object _target;
        private readonly System.Reflection.MethodInfo? _invokeMethod;

        public ReflectiveCallableAdapter(object target)
        {
            _target = target;
            // Try to find an Invoke method
            _invokeMethod = target.GetType().GetMethod("Invoke");
        }

        public int Arity() => 2;

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            if (_invokeMethod == null)
                return null;

            try
            {
                return _invokeMethod.Invoke(_target, [arguments.ToArray()]);
            }
            catch
            {
                return null;
            }
        }
    }
}
