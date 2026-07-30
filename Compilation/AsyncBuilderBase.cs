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

    /// <summary>
    /// The <c>AsyncTaskMethodBuilder</c> variant driving this state machine. Set by the subclass
    /// (constructor or DefineStateMachine) before any builder accessor below is used.
    /// </summary>
    public Type BuilderType { get; protected set; } = null!;

    /// <summary>Gets the IsCompleted property getter for the awaiter.</summary>
    public MethodInfo GetAwaiterIsCompletedGetter()
    {
        return Types.GetProperty(AwaiterType, "IsCompleted", BindingFlags.Public | BindingFlags.Instance).GetGetMethod()!;
    }

    /// <summary>Gets the GetResult method for the awaiter.</summary>
    public MethodInfo GetAwaiterGetResultMethod()
    {
        return Types.GetMethod(AwaiterType, "GetResult", BindingFlags.Public | BindingFlags.Instance);
    }

    /// <summary>Gets the GetAwaiter method for <c>Task&lt;object&gt;</c>.</summary>
    public MethodInfo GetTaskGetAwaiterMethod()
    {
        return Types.GetMethodNoParams(Types.TaskOfObject, "GetAwaiter");
    }

    /// <summary>Gets the static Create method for the specific builder type.</summary>
    public MethodInfo GetBuilderCreateMethod()
    {
        return Types.GetMethod(BuilderType, "Create", BindingFlags.Public | BindingFlags.Static);
    }

    /// <summary>Gets the Task property getter for the specific builder type.</summary>
    public MethodInfo GetBuilderTaskGetter()
    {
        return Types.GetProperty(BuilderType, "Task", BindingFlags.Public | BindingFlags.Instance).GetGetMethod()!;
    }

    /// <summary>Gets Start&lt;TStateMachine&gt;(ref TStateMachine) instantiated for this state machine.</summary>
    public MethodInfo GetBuilderStartMethod()
    {
        var methods = Types.GetMethods(BuilderType, BindingFlags.Public | BindingFlags.Instance);
        var startMethod = methods.First(m => m.Name == "Start" && m.IsGenericMethod);
        return EmitGenerics.MakeGenericMethod(startMethod, StateMachineType);
    }

    /// <summary>Gets the SetException method for the specific builder type.</summary>
    public MethodInfo GetBuilderSetExceptionMethod()
    {
        return Types.GetMethod(BuilderType, "SetException", BindingFlags.Public | BindingFlags.Instance);
    }

    /// <summary>
    /// Gets AwaitUnsafeOnCompleted&lt;TAwaiter, TStateMachine&gt;(ref TAwaiter, ref TStateMachine)
    /// instantiated for this state machine's awaiter and struct.
    /// </summary>
    public MethodInfo GetBuilderAwaitUnsafeOnCompletedMethod()
    {
        var methods = Types.GetMethods(BuilderType, BindingFlags.Public | BindingFlags.Instance);
        var awaitMethod = methods.First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod);
        return EmitGenerics.MakeGenericMethod(awaitMethod, AwaiterType, StateMachineType);
    }

    // GetBuilderSetResultMethod stays specialized in each subclass: the non-generic
    // AsyncTaskMethodBuilder has a parameterless SetResult, the generic builders take the value.

    /// <summary>
    /// Finalizes the type after the MoveNext body has been emitted. Validates labels on every
    /// method in this state-machine type first — CreateType() clears the ILGenerator control-flow
    /// state, so a post-finalize sweep cannot see unmarked branched labels.
    /// </summary>
    public override Type CreateType()
    {
        ILLabelValidator.SweepAllTypes(new[] { StateMachineType });
        ILLabelValidator.SweepConstructors(new[] { StateMachineType });
        return StateMachineType.CreateType()!;
    }
}
