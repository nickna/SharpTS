using System.Reflection;

namespace SharpTS.Compilation;

/// <summary>
/// Shared base for the two async <em>function</em> state-machine builders —
/// <see cref="AsyncStateMachineBuilder"/> and <see cref="AsyncArrowStateMachineBuilder"/>. Both await
/// through a <c>TaskAwaiter&lt;object&gt;</c>, so the awaiter accessors (IsCompleted getter, GetResult
/// method, and <c>Task&lt;object&gt;.GetAwaiter</c>) are byte-identical between them; this layer holds
/// the single copy (#1125). The async generator does <em>not</em> derive from here — it drives its
/// suspension off a <c>ManualResetValueTaskSourceCore&lt;bool&gt;</c>, not a <c>TaskAwaiter</c>.
/// </summary>
public abstract class AsyncBuilderBase : StateMachineBuilderBase
{
    /// <summary>The <see cref="TypeProvider"/> the awaiter accessors resolve BCL types through.</summary>
    protected abstract TypeProvider Types { get; }

    /// <summary>The <c>TaskAwaiter&lt;object&gt;</c> type stored at every await point.</summary>
    public Type AwaiterType { get; protected set; } = null!;

    /// <summary>Gets the IsCompleted property getter for the awaiter.</summary>
    public MethodInfo GetAwaiterIsCompletedGetter()
    {
        return AwaiterType.GetProperty("IsCompleted", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
    }

    /// <summary>Gets the GetResult method for the awaiter.</summary>
    public MethodInfo GetAwaiterGetResultMethod()
    {
        return AwaiterType.GetMethod("GetResult", BindingFlags.Public | BindingFlags.Instance)!;
    }

    /// <summary>Gets the GetAwaiter method for <c>Task&lt;object&gt;</c>.</summary>
    public MethodInfo GetTaskGetAwaiterMethod()
    {
        return Types.GetMethodNoParams(Types.TaskOfObject, "GetAwaiter");
    }
}
