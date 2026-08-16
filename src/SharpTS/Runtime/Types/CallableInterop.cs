using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Transitional helpers for invoking callables across the object?/RuntimeValue boundary
/// while the boxed <see cref="ISharpTSCallable.Call"/> surface is being retired.
/// </summary>
/// <remarks>
/// Call sites that still hold boxed <c>List&lt;object?&gt;</c> arguments (typically built from
/// object?-based storage) should use <see cref="CallBoxed"/> rather than invoking the legacy
/// <c>Call</c> method directly; it routes through <see cref="ISharpTSCallable.CallV2"/> so
/// migrated implementors run boxing-free.
/// </remarks>
public static class CallableInterop
{
    /// <summary>
    /// Invokes a callable with boxed arguments by converting them to RuntimeValues and
    /// dispatching through <see cref="ISharpTSCallable.CallV2"/>.
    /// </summary>
    public static object? CallBoxed(this ISharpTSCallable callable, Interpreter interpreter, List<object?> arguments)
    {
        return callable.CallV2(interpreter, ToRuntimeValues(arguments)).ToObject();
    }

    /// <summary>Converts a boxed argument list to a RuntimeValue array.</summary>
    public static RuntimeValue[] ToRuntimeValues(List<object?> arguments)
    {
        if (arguments.Count == 0)
            return [];
        var values = new RuntimeValue[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
            values[i] = RuntimeValue.FromBoxed(arguments[i]);
        return values;
    }

    /// <summary>
    /// Converts a RuntimeValue span to a boxed argument list. This is the single shared
    /// span→boxed-list helper for the built-in V2 shims (Object/Array/Iterator) whose legacy
    /// bodies still consume <c>List&lt;object?&gt;</c>; it replaces the per-file <c>SpanToList</c>
    /// copies that previously duplicated it.
    /// </summary>
    public static List<object?> ToBoxedList(ReadOnlySpan<RuntimeValue> arguments)
    {
        var boxed = new List<object?>(arguments.Length);
        foreach (var arg in arguments)
            boxed.Add(arg.ToObject());
        return boxed;
    }
}
