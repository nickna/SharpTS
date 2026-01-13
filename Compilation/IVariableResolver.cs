using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Abstracts variable resolution for IL emission in state machine emitters.
/// Handles user-defined variables only (parameters, locals, hoisted, captured).
/// Does NOT handle pseudo-variables (Math, classes, functions, namespaces).
/// </summary>
public interface IVariableResolver
{
    /// <summary>
    /// Attempts to load a variable onto the evaluation stack.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <returns>The resulting StackType if found and loaded, null if not found</returns>
    StackType? TryLoadVariable(string name);

    /// <summary>
    /// Attempts to store the top of stack into a variable.
    /// Assumes value is already on stack (boxed for object variables).
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <returns>True if stored successfully, false if variable not found</returns>
    bool TryStoreVariable(string name);

    /// <summary>
    /// Loads 'this' reference onto the stack.
    /// </summary>
    void LoadThis();
}
