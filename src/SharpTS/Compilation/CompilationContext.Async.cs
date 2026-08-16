using System.Reflection;
using static SharpTS.Parsing.Expr;

namespace SharpTS.Compilation;

public partial class CompilationContext
{
    // ============================================
    // Async Method Support
    // ============================================

    // Async function name -> compiled MethodInfo
    public Dictionary<string, MethodInfo>? AsyncMethods { get; set; }

    // Async arrow function state machines
    // Arrow function -> its state machine builder
    public Dictionary<ArrowFunction, AsyncArrowStateMachineBuilder>? AsyncArrowBuilders { get; set; }

    // Arrow function -> its outer async function's state machine builder
    public Dictionary<ArrowFunction, AsyncStateMachineBuilder>? AsyncArrowOuterBuilders { get; set; }

    // For nested arrows: arrow function -> its parent arrow's state machine builder
    public Dictionary<ArrowFunction, AsyncArrowStateMachineBuilder>? AsyncArrowParentBuilders { get; set; }
}
