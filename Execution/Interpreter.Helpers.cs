using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.DotNet;
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
            case SharpTSProxy proxy:
                value = proxy.TrapGet(name.Lexeme, this);
                return true;
            case SharpTSClass klass:
                value = klass.GetStaticProperty(name.Lexeme);
                return true;
            case SharpTSInstance instance:
                instance.SetInterpreter(this);
                value = instance.Get(name);
                return true;
            case SharpTSObject simpleObj:
                value = simpleObj.GetProperty(name.Lexeme);
                return true;
            case DotNetInstance external:
                value = external.GetMember(name.Lexeme);
                return true;
            default:
                value = null;
                return false;
        }
    }

    /// <summary>
    /// Attempts to get a property value from an object-like runtime value, returning RuntimeValue directly.
    /// </summary>
    private bool TryGetPropertyRV(object? obj, Token name, out RuntimeValue value)
    {
        switch (obj)
        {
            case SharpTSProxy proxy:
                value = proxy.TrapGetRV(name.Lexeme, this);
                return true;
            case SharpTSClass klass:
                value = RuntimeValue.FromBoxed(klass.GetStaticProperty(name.Lexeme));
                return true;
            case SharpTSInstance instance:
                instance.SetInterpreter(this);
                value = instance.GetRV(name);
                return true;
            case SharpTSObject simpleObj:
                value = simpleObj.GetPropertyRV(name.Lexeme);
                return true;
            case DotNetInstance external:
                value = RuntimeValue.FromBoxed(external.GetMember(name.Lexeme));
                return true;
            default:
                value = RuntimeValue.Undefined;
                return false;
        }
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
            case SharpTSProxy proxy:
                proxy.TrapSet(name.Lexeme, value, this);
                return true;
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
            case DotNetInstance external:
                external.SetMember(name.Lexeme, value, this);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to get an element from an array-like runtime value by index.
    /// </summary>
    /// <param name="obj">The array-like object.</param>
    /// <param name="index">The index value (expected to be a double).</param>
    /// <param name="value">The retrieved value if successful.</param>
    /// <returns><c>true</c> if the element was found; otherwise <c>false</c>.</returns>
    private bool TryGetIndex(object? obj, object? index, out object? value)
    {
        if (obj is SharpTSArray array && index is double idx)
        {
            value = array.Get((int)idx);
            return true;
        }
        if (obj is DotNetInstance external && external.HasReadableIndexer)
        {
            value = external.GetIndex(index, this);
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
    private bool TrySetIndex(object? obj, object? index, object? value)
    {
        if (obj is SharpTSArray array && index is double idx)
        {
            array.Set((int)idx, value);
            return true;
        }
        if (obj is DotNetInstance external && external.HasWritableIndexer)
        {
            external.SetIndex(index, value, this);
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
    private RuntimeValue EvaluateIncrement(Expr operand, double delta, bool returnOld)
    {
        switch (operand)
        {
            case Expr.Variable variable:
                return IncrementVariable(variable, delta, returnOld);

            case Expr.Get get:
                return IncrementProperty(Evaluate(get.Object), get.Name, delta, returnOld);

            case Expr.GetIndex getIndex:
            {
                object? obj = Evaluate(getIndex.Object);
                object? index = Evaluate(getIndex.Index);
                return IncrementIndex(obj, index, delta, returnOld);
            }
        }

        throw new InterpreterException("Invalid increment operand.");
    }

    /// <summary>
    /// Async counterpart of <see cref="EvaluateIncrement"/>. Resolves the operand's
    /// receiver (and index) through the async-aware evaluator so an <c>await</c> or
    /// thenable in the receiver/index — e.g. <c>(await foo()).n++</c>, <c>arr[await i()]--</c> —
    /// is awaited rather than routed to the synchronous <c>VisitAwait</c>, which throws
    /// "'await' can only be used inside async functions." The read-modify-write itself is
    /// identical to the synchronous path. See issue #451.
    /// </summary>
    private async Task<RuntimeValue> EvaluateIncrementAsync(Expr operand, double delta, bool returnOld)
    {
        switch (operand)
        {
            case Expr.Variable variable:
                return IncrementVariable(variable, delta, returnOld);

            case Expr.Get get:
                return IncrementProperty((await EvaluateAsync(get.Object)).ToObject(), get.Name, delta, returnOld);

            case Expr.GetIndex getIndex:
            {
                object? obj = (await EvaluateAsync(getIndex.Object)).ToObject();
                object? index = (await EvaluateAsync(getIndex.Index)).ToObject();
                return IncrementIndex(obj, index, delta, returnOld);
            }
        }

        throw new InterpreterException("Invalid increment operand.");
    }

    /// <summary>Increments a variable l-value, returning the old or new value.</summary>
    private RuntimeValue IncrementVariable(Expr.Variable variable, double delta, bool returnOld)
    {
        RuntimeValue oldValue = _environment.Get(variable.Name);
        if (TryEvaluateDotNetIncrement(
                oldValue,
                delta > 0 ? TokenType.PLUS_PLUS : TokenType.MINUS_MINUS,
                out var clrValue))
        {
            _environment.Assign(variable.Name, clrValue);
            return returnOld ? oldValue : clrValue;
        }

        // ECMA-262 13.4 (postfix)/13.5.7 (prefix): apply ToNumber to the operand's current
        // value before adding ±1. A widened (`any`) variable can hold a non-number — a numeric
        // string ("5"→5), undefined (→NaN), etc. — so coerce rather than asserting a boxed double.
        double current = CoerceToNumber(oldValue);
        double newValue = current + delta;
        _environment.Assign(variable.Name, RuntimeValue.FromNumber(newValue));
        return RuntimeValue.FromNumber(returnOld ? current : newValue);
    }

    private bool TryEvaluateDotNetIncrement(
        RuntimeValue current,
        TokenType token,
        out RuntimeValue result)
    {
        if (current.ToObject() is not SharpTS.Runtime.DotNet.DotNetInstance instance)
        {
            result = default;
            return false;
        }

        var methods = SharpTS.Runtime.DotNet.DotNetOperatorResolver.GetIncrementCandidates(
            token, instance.Type);
        if (methods.Length == 0)
        {
            result = default;
            return false;
        }

        var arguments = new List<object?> { instance };
        var candidate = SharpTS.Runtime.DotNet.DotNetMethodResolver.ResolveMethod(
            methods, arguments);
        var method = (System.Reflection.MethodInfo)candidate.Method;
        var invokeArgs = SharpTS.Runtime.DotNet.DotNetMethod.BuildInvokeArgs(
            method.GetParameters(), arguments, candidate, this);
        object? value = SharpTS.Runtime.DotNet.DotNetInstance.InvokeWithMapping(
            () => method.Invoke(null, invokeArgs));
        result = RuntimeValue.FromBoxed(
            SharpTS.Runtime.DotNet.DotNetMarshaller.WrapReturn(value, method.ReturnType));
        return true;
    }

    /// <summary>
    /// Increments a property l-value on an already-resolved receiver. Shared by the
    /// synchronous and async increment paths so the read-modify-write semantics stay identical.
    /// </summary>
    private RuntimeValue IncrementProperty(object? obj, Token name, double delta, bool returnOld)
    {
        if (TryGetProperty(obj, name, out object? currentObj))
        {
            RuntimeValue oldValue = RuntimeValue.FromBoxed(currentObj);
            if (TryEvaluateDotNetIncrement(
                    oldValue,
                    delta > 0 ? TokenType.PLUS_PLUS : TokenType.MINUS_MINUS,
                    out RuntimeValue externalValue) &&
                TrySetProperty(obj, name, externalValue.ToObject()))
            {
                return returnOld ? oldValue : externalValue;
            }

            // ECMA-262 ToNumber on the member's current value (matches the variable path and
            // compiled mode's ConvertToNumber): a non-numeric `any` member ("5"→5, undefined→NaN)
            // follows JS semantics instead of throwing on a failed hard cast (#471).
            double current = CoerceToNumber(currentObj);
            double newValue = current + delta;
            if (TrySetProperty(obj, name, newValue))
            {
                return RuntimeValue.FromNumber(returnOld ? current : newValue);
            }
        }

        throw new InterpreterException("Invalid increment operand.");
    }

    /// <summary>
    /// Increments an indexed l-value on an already-resolved receiver/index. Shared by the
    /// synchronous and async increment paths so the read-modify-write semantics stay identical.
    /// </summary>
    private RuntimeValue IncrementIndex(object? obj, object? index, double delta, bool returnOld)
    {
        if (TryGetIndex(obj, index, out object? currentObj))
        {
            RuntimeValue oldValue = RuntimeValue.FromBoxed(currentObj);
            if (TryEvaluateDotNetIncrement(
                    oldValue,
                    delta > 0 ? TokenType.PLUS_PLUS : TokenType.MINUS_MINUS,
                    out RuntimeValue externalValue) &&
                TrySetIndex(obj, index, externalValue.ToObject()))
            {
                return returnOld ? oldValue : externalValue;
            }

            // ECMA-262 ToNumber on the element's current value (see IncrementProperty): a
            // non-numeric element in an `any[]` ("7"→7, undefined→NaN) follows JS semantics
            // instead of throwing on a failed hard cast (#471).
            double current = CoerceToNumber(currentObj);
            double newValue = current + delta;
            if (TrySetIndex(obj, index, newValue))
            {
                return RuntimeValue.FromNumber(returnOld ? current : newValue);
            }
        }

        throw new InterpreterException("Invalid increment operand.");
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

    /// <summary>
    /// Creates a scoped context for script execution that saves/restores environment and current module.
    /// Use with <c>using</c> statement to ensure proper context cleanup.
    /// </summary>
    /// <param name="newEnvironment">The new environment to use within the scope.</param>
    /// <param name="newModule">The new current module to use within the scope.</param>
    /// <returns>A disposable scope guard that restores the previous context on disposal.</returns>
    private ScopedScriptContext PushScriptContext(RuntimeEnvironment newEnvironment, ParsedModule? newModule)
    {
        return new ScopedScriptContext(this, newEnvironment, newModule);
    }

    /// <summary>
    /// A disposable scope guard that manages environment and module context switching for script execution.
    /// </summary>
    private readonly struct ScopedScriptContext : IDisposable
    {
        private readonly Interpreter _interpreter;
        private readonly RuntimeEnvironment _previousEnvironment;
        private readonly ParsedModule? _previousModule;

        public ScopedScriptContext(Interpreter interpreter, RuntimeEnvironment newEnvironment, ParsedModule? newModule)
        {
            _interpreter = interpreter;
            _previousEnvironment = interpreter._environment;
            _previousModule = interpreter._currentModule;
            interpreter._environment = newEnvironment;
            interpreter._currentModule = newModule;
        }

        public void Dispose()
        {
            _interpreter._environment = _previousEnvironment;
            _interpreter._currentModule = _previousModule;
        }
    }

    /// <summary>
    /// Creates a scoped context for module execution that saves/restores environment, current module, and module instance.
    /// Use with <c>using</c> statement to ensure proper context cleanup.
    /// </summary>
    /// <param name="newEnvironment">The new environment to use within the scope.</param>
    /// <param name="newModule">The new current module to use within the scope.</param>
    /// <param name="newModuleInstance">The new module instance to use within the scope.</param>
    /// <returns>A disposable scope guard that restores the previous context on disposal.</returns>
    private ScopedModuleContext PushModuleContext(RuntimeEnvironment newEnvironment, ParsedModule? newModule, ModuleInstance? newModuleInstance)
    {
        return new ScopedModuleContext(this, newEnvironment, newModule, newModuleInstance);
    }

    /// <summary>
    /// A disposable scope guard that manages full module context switching (environment, module, and module instance).
    /// </summary>
    private readonly struct ScopedModuleContext : IDisposable
    {
        private readonly Interpreter _interpreter;
        private readonly RuntimeEnvironment _previousEnvironment;
        private readonly ParsedModule? _previousModule;
        private readonly ModuleInstance? _previousModuleInstance;

        public ScopedModuleContext(Interpreter interpreter, RuntimeEnvironment newEnvironment, ParsedModule? newModule, ModuleInstance? newModuleInstance)
        {
            _interpreter = interpreter;
            _previousEnvironment = interpreter._environment;
            _previousModule = interpreter._currentModule;
            _previousModuleInstance = interpreter._currentModuleInstance;
            interpreter._environment = newEnvironment;
            interpreter._currentModule = newModule;
            interpreter._currentModuleInstance = newModuleInstance;
        }

        public void Dispose()
        {
            _interpreter._environment = _previousEnvironment;
            _interpreter._currentModule = _previousModule;
            _interpreter._currentModuleInstance = _previousModuleInstance;
        }
    }

    #endregion

    #region Promise Helpers

    /// <summary>
    /// Creates a Promise from an executor function following the JavaScript Promise constructor pattern.
    /// The executor is called immediately with (resolve, reject) callbacks.
    /// </summary>
    /// <param name="executor">The executor function that receives resolve and reject callbacks.</param>
    /// <returns>A SharpTSPromise that will be resolved or rejected based on the executor's behavior.</returns>
    private SharpTSPromise CreatePromiseFromExecutor(object? executor)
    {
        if (executor is not ISharpTSCallable callable)
        {
            throw new InterpreterException("Promise executor must be callable.");
        }

        // The executor wiring (host resolve/reject callbacks, promise
        // flattening, throw-to-rejection) is shared with the Promise
        // subclass bridge's super(executor) path (#242).
        var tcs = new TaskCompletionSource<object?>();
        SharpTSPromiseClass.RunExecutor(this, callable, tcs);
        return new SharpTSPromise(tcs.Task);
    }

    #endregion
}
