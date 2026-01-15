using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    #region Object Property Dispatch Helpers

    /// <summary>
    /// Attempts to get a property value from an object-like runtime value.
    /// Handles <see cref="SharpTSInstance"/>, <see cref="SharpTSObject"/>, and <see cref="SharpTSArray"/>.
    /// </summary>
    /// <param name="obj">The object to get the property from.</param>
    /// <param name="name">The property name (as a Token for instance access).</param>
    /// <param name="value">The retrieved value if successful.</param>
    /// <returns><c>true</c> if the property was found; otherwise <c>false</c>.</returns>
    private bool TryGetProperty(object? obj, Token name, out object? value)
    {
        switch (obj)
        {
            case SharpTSInstance instance:
                instance.SetInterpreter(this);
                value = instance.Get(name);
                return true;
            case SharpTSObject simpleObj:
                value = simpleObj.GetProperty(name.Lexeme);
                return true;
            default:
                value = null;
                return false;
        }
    }

    /// <summary>
    /// Attempts to get a property value from an object-like runtime value using a string key.
    /// Uses <see cref="ISharpTSPropertyAccessor"/> interface for unified dispatch.
    /// </summary>
    /// <param name="obj">The object to get the property from.</param>
    /// <param name="name">The property name as a string.</param>
    /// <param name="value">The retrieved value if successful.</param>
    /// <returns><c>true</c> if the property was found; otherwise <c>false</c>.</returns>
    private static bool TryGetPropertyByName(object? obj, string name, out object? value)
    {
        if (obj is ISharpTSPropertyAccessor accessor)
        {
            value = accessor.GetProperty(name);
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Attempts to set a property value on an object-like runtime value.
    /// Handles <see cref="SharpTSInstance"/>, <see cref="SharpTSObject"/>, and <see cref="SharpTSClass"/> (static properties).
    /// </summary>
    /// <param name="obj">The object to set the property on.</param>
    /// <param name="name">The property name (as a Token for instance access).</param>
    /// <param name="value">The value to set.</param>
    /// <returns><c>true</c> if the property was set; otherwise <c>false</c>.</returns>
    private bool TrySetProperty(object? obj, Token name, object? value)
    {
        switch (obj)
        {
            case SharpTSClass klass:
                klass.SetStaticProperty(name.Lexeme, value);
                return true;
            case SharpTSInstance instance:
                instance.SetInterpreter(this);
                instance.Set(name, value);
                return true;
            case SharpTSObject simpleObj:
                simpleObj.SetProperty(name.Lexeme, value);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to set a property value on an object-like runtime value using a string key.
    /// Uses <see cref="ISharpTSPropertyAccessor"/> interface for unified dispatch.
    /// </summary>
    /// <param name="obj">The object to set the property on.</param>
    /// <param name="name">The property name as a string.</param>
    /// <param name="value">The value to set.</param>
    /// <returns><c>true</c> if the property was set; otherwise <c>false</c>.</returns>
    private static bool TrySetPropertyByName(object? obj, string name, object? value)
    {
        if (obj is ISharpTSPropertyAccessor accessor)
        {
            accessor.SetProperty(name, value);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Attempts to get an element from an array-like runtime value by index.
    /// </summary>
    /// <param name="obj">The array-like object.</param>
    /// <param name="index">The index value (expected to be a double).</param>
    /// <param name="value">The retrieved value if successful.</param>
    /// <returns><c>true</c> if the element was found; otherwise <c>false</c>.</returns>
    private static bool TryGetIndex(object? obj, object? index, out object? value)
    {
        if (obj is SharpTSArray array && index is double idx)
        {
            value = array.Get((int)idx);
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Attempts to set an element on an array-like runtime value by index.
    /// </summary>
    /// <param name="obj">The array-like object.</param>
    /// <param name="index">The index value (expected to be a double).</param>
    /// <param name="value">The value to set.</param>
    /// <returns><c>true</c> if the element was set; otherwise <c>false</c>.</returns>
    private static bool TrySetIndex(object? obj, object? index, object? value)
    {
        if (obj is SharpTSArray array && index is double idx)
        {
            array.Set((int)idx, value);
            return true;
        }
        return false;
    }

    #endregion

    #region Increment/Decrement Helpers

    /// <summary>
    /// Evaluates an increment or decrement operation on any valid l-value.
    /// Consolidates prefix (++x, --x) and postfix (x++, x--) increment logic.
    /// </summary>
    /// <param name="operand">The l-value expression to increment/decrement.</param>
    /// <param name="delta">The amount to add (+1 for increment, -1 for decrement).</param>
    /// <param name="returnOld">If <c>true</c>, returns the old value (postfix); otherwise returns the new value (prefix).</param>
    /// <returns>The old or new value depending on <paramref name="returnOld"/>.</returns>
    /// <exception cref="Exception">Thrown if the operand is not a valid l-value.</exception>
    private object? EvaluateIncrement(Expr operand, double delta, bool returnOld)
    {
        switch (operand)
        {
            case Expr.Variable variable:
            {
                double current = (double)_environment.Get(variable.Name)!;
                double newValue = current + delta;
                _environment.Assign(variable.Name, newValue);
                return returnOld ? current : newValue;
            }

            case Expr.Get get:
            {
                object? obj = Evaluate(get.Object);
                if (TryGetProperty(obj, get.Name, out object? currentObj))
                {
                    double current = (double)currentObj!;
                    double newValue = current + delta;
                    if (TrySetProperty(obj, get.Name, newValue))
                    {
                        return returnOld ? current : newValue;
                    }
                }
                break;
            }

            case Expr.GetIndex getIndex:
            {
                object? obj = Evaluate(getIndex.Object);
                object? index = Evaluate(getIndex.Index);
                if (TryGetIndex(obj, index, out object? currentObj))
                {
                    double current = (double)currentObj!;
                    double newValue = current + delta;
                    if (TrySetIndex(obj, index, newValue))
                    {
                        return returnOld ? current : newValue;
                    }
                }
                break;
            }
        }

        throw new Exception("Invalid increment operand.");
    }

    #endregion

    #region Scope Management

    /// <summary>
    /// Creates a scoped environment that automatically restores the previous environment on disposal.
    /// Use with <c>using</c> statement to ensure proper scope cleanup.
    /// </summary>
    /// <param name="newEnvironment">The new environment to use within the scope.</param>
    /// <returns>A disposable scope guard that restores the environment on disposal.</returns>
    /// <example>
    /// <code>
    /// using (PushScope(new RuntimeEnvironment(_environment)))
    /// {
    ///     // Execute statements in new scope
    /// }
    /// // Previous environment automatically restored
    /// </code>
    /// </example>
    private ScopedEnvironment PushScope(RuntimeEnvironment newEnvironment)
    {
        return new ScopedEnvironment(this, newEnvironment);
    }

    /// <summary>
    /// A disposable scope guard that manages environment switching.
    /// </summary>
    private readonly struct ScopedEnvironment : IDisposable
    {
        private readonly Interpreter _interpreter;
        private readonly RuntimeEnvironment _previous;

        public ScopedEnvironment(Interpreter interpreter, RuntimeEnvironment newEnvironment)
        {
            _interpreter = interpreter;
            _previous = interpreter._environment;
            interpreter._environment = newEnvironment;
        }

        public void Dispose()
        {
            _interpreter._environment = _previous;
        }
    }

    #endregion
}
