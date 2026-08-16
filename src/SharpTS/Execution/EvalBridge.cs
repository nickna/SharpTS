namespace SharpTS.Execution;

/// <summary>
/// Reflection entry point for the global <c>eval()</c> function in <b>compiled</b> output.
/// </summary>
/// <remarks>
/// <para>
/// Compiled programs run as native .NET IL with no live <see cref="Interpreter"/> behind
/// them, so there is no caller scope for eval'd code to capture. This bridge therefore
/// implements <b>indirect eval</b> semantics: it spins up a fresh interpreter and evaluates
/// the source in a clean global scope (built-in globals like <c>Number</c>, <c>Math</c>,
/// <c>JSON</c> resolve normally; compiled local variables are intentionally not visible).
/// </para>
/// <para>
/// The compiled DLL never references this type directly — it is invoked via
/// <c>Type.GetType("SharpTS.Execution.EvalBridge, SharpTS")</c> at runtime (see
/// <c>GlobalFunctionHandler.EmitEval</c>), preserving the standalone-DLL constraint. When
/// SharpTS is not present the compiled call site degrades gracefully with a deterministic
/// throw rather than resolving this method.
/// </para>
/// </remarks>
public static class EvalBridge
{
    /// <summary>
    /// Evaluates <paramref name="argument"/> as JavaScript/TypeScript source in a fresh global
    /// scope and returns the completion value. Per ECMA-262 §19.2.1, a non-string argument is
    /// returned unchanged.
    /// </summary>
    public static object? Eval(object? argument)
    {
        if (argument is not string source)
            return argument;

        var interpreter = new Interpreter();
        return interpreter.Eval(source);
    }
}
