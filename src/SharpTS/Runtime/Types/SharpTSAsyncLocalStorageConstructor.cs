using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents the AsyncLocalStorage constructor exported from the 'async_hooks' module.
/// Supports instantiation via <c>new AsyncLocalStorage()</c>.
/// </summary>
public sealed class SharpTSAsyncLocalStorageConstructor : ISharpTSCallable
{
    /// <summary>
    /// The singleton instance of the AsyncLocalStorage constructor.
    /// </summary>
    public static readonly SharpTSAsyncLocalStorageConstructor Instance = new();

    private SharpTSAsyncLocalStorageConstructor() { }

    public int Arity() => 0;

    public object? Call(Interp interpreter, List<object?> arguments)
    {
        return new SharpTSAsyncLocalStorage();
    }

    public override string ToString() => "[Function: AsyncLocalStorage]";
}
