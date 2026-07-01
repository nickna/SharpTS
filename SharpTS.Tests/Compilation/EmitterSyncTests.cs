using System.Reflection;
using SharpTS.Compilation;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Tests.Compilation;

/// <summary>
/// Validates that state machine emitters don't accidentally override base class methods
/// without justification. When a state machine emitter overrides a virtual method from
/// StatementEmitterBase or ExpressionEmitterBase, it must be for a genuine reason
/// (await/yield handling, return semantics, variable capture, etc.).
///
/// This test catches the most common drift pattern: copy-pasting a method from the base
/// into a state machine emitter "just in case" — which then silently falls out of sync
/// as the base evolves.
/// </summary>
public class EmitterSyncTests
{
    private readonly ITestOutputHelper _output;

    public EmitterSyncTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Methods that state machine emitters are allowed to override, with justification.
    /// Any override not in this allowlist will cause the test to fail, forcing the developer
    /// to either add justification or remove the unnecessary override.
    /// </summary>
    private static readonly Dictionary<Type, HashSet<string>> AllowedOverrides = new()
    {
        // Shared state-machine bases. The exit-routing scaffolding (break/continue, loop-scope nav,
        // and the Leave-vs-Br branch helper) lives in StateMachineExitRoutingEmitter — common to all
        // four emitters; the await-aware return/try-catch lives in AsyncFunctionMoveNextEmitter —
        // common to the two async-function emitters. They are validated here as their own entries, so
        // the leaf emitters below no longer list those hoisted methods (they inherit them).
        [typeof(StateMachineExitRoutingEmitter)] = new()
        {
            // --- #500/#554/#559/#774: non-local exits route through enclosing flag-based finally(s) ---
            "EnterLoop",            // Loops share the unified _exitScopes stack with finally scopes
            "ExitLoop",             // (so break/continue can find the finallys between them and the loop)
            "get_CurrentLoop",      // Loop lookups read _exitScopes instead of the base loop stack
            "FindLabeledLoop",      // Labeled loop lookups read _exitScopes
            "EmitBreak",            // Route a break leaving a try through its enclosing finally(s)
            "EmitContinue",         // Route a continue leaving a try through its enclosing finally(s)
            "EmitBranchToLabel",    // #554/#727: Leave instead of Br when inside a real IL exception block
        },
        [typeof(AsyncFunctionMoveNextEmitter)] = new()
        {
            // --- #774: await-aware exception handling + async-function completion ---
            "EmitReturn",           // Async return: route through finally(s), then complete the task
            "EmitTryCatch",         // Await-aware (flag-based) exception handling
            // --- #914: a suspending `throw await f()` in a try body is emitted at the flag-based top
            //     level (outside any mini try/catch), so route it into the active try's catch ---
            "EmitThrow",            // Route a top-level throw in a flag-based try body to its catch
            // --- #1122: the await suspension IL dance is shared here across the two async-function
            //     emitters, which supply only the emitter-specific seams (exit label, state counter,
            //     resume-label marking, awaiter-field lookup) ---
            "EmitAwait",            // Core: suspend/resume the state machine on await
        },
        [typeof(IteratorMoveNextEmitter)] = new()
        {
            // #1124: the block-scope-rename + function-display-class variable overrides are byte-identical
            // between the sync generator and async generator (an async generator is a generator that also
            // awaits), so they live here on the shared iterator base instead of being copied into each. The
            // two leaf iterator emitters inherit them and no longer list them below.
            "EmitVariable",         // Read a captured-and-mutated local through the function DC (+ #711/#766 rename)
            "EmitAssign",           // Write a captured-and-mutated local through the function DC (+ #711/#766 rename)
            "EmitStoreVariable",    // Store side of compound/logical/increment through the function DC
            "EmitVarDeclaration",   // Initialize a captured-and-mutated local into the function DC (+ #711/#766 rename)
            "EmitConstDeclaration", // Route a shadowing const declaration to its own slot
            "EmitCompoundAssign",   // Route a shadowing compound assignment to its own slot
            "EmitLogicalAssign",    // Route a shadowing logical assignment to its own slot
            "EmitPrefixIncrement",  // Route a shadowing prefix ++/-- to its own slot
            "EmitPostfixIncrement", // Route a shadowing postfix ++/-- to its own slot
        },
        [typeof(AsyncMoveNextEmitter)] = new()
        {
            // --- Infrastructure (abstract property/field implementations) ---
            "get_IL",               // Abstract: provides ILGenerator for this emitter
            "get_Ctx",              // Abstract: provides CompilationContext
            "get_Types",            // Abstract: provides TypeProvider
            "get_Resolver",         // Abstract: provides IVariableResolver
            "GetThisField",         // Abstract: provides hoisted 'this' field
            "GetHoistedVariableField", // Hoisted variable fields on state machine
            "SharpTS.Compilation.Emitters.IEmitterContext.get_IL", // IEmitterContext impl
            "SharpTS.Compilation.Emitters.IEmitterContext.SetStackUnknown", // IEmitterContext impl
            "SharpTS.Compilation.Emitters.IEmitterContext.SetStackType",   // IEmitterContext impl
            // --- Genuinely different behavior ---
            // (EmitReturn/EmitTryCatch are inherited from AsyncFunctionMoveNextEmitter; break/continue/
            //  loop-nav/EmitBranchToLabel from StateMachineExitRoutingEmitter — see those entries above.)
            "EmitForOf",            // for-await-of protocol dispatch
            "EmitForAwaitOf",       // #631: override the shared base to SUSPEND on next()/return() (vs blocking GetResult)
            "EmitLabeledStatement", // Labeled continue for for loops (base doesn't handle correctly)
            // (EmitAwait is inherited from AsyncFunctionMoveNextEmitter since #1122 — see above.)
            "EmitArrowFunction",    // Display class in state machine context
            "EmitExpressionAsDouble", // Literal optimization
            "EmitCallPrivate",      // Private method calls in async context
            "EmitGetPrivate",       // Private field access in async context
            "EmitSetPrivate",       // Private field assignment in async context
            "EmitNullishCoalescing", // Nullish coalescing with await-safe evaluation
            "EmitTemplateLiteral",  // Template literals with await-safe temp storage
            "EmitTaggedTemplateLiteral", // Tagged template literals in async context
            // --- Closure mutation sharing ---
            "EmitVariable",         // Route captured function locals through function DC (+ #766 rename)
            "EmitAssign",           // Route captured function locals through function DC (+ #766 rename)
            "EmitStoreVariable",    // Route captured function locals through function DC
            "EmitVarDeclaration",   // Route captured function locals through function DC (+ #766 rename)
            // --- #766: a nested-block let/const that shadows an enclosing binding gets its own storage;
            // these overrides retoken the operator node so the read/write land on the shadow's own
            // field/local instead of the outer binding's hoisted field (async analog of #711) ---
            "EmitConstDeclaration", // #766: route a shadowing const declaration to its own slot
            "EmitCompoundAssign",   // #766: route a shadowing compound assignment to its own slot
            "EmitLogicalAssign",    // #766: route a shadowing logical assignment to its own slot
            "EmitPrefixIncrement",  // #766: route a shadowing prefix ++/-- to its own slot
            "EmitPostfixIncrement", // #766: route a shadowing postfix ++/-- to its own slot
        },
        [typeof(AsyncArrowMoveNextEmitter)] = new()
        {
            // --- Infrastructure ---
            "get_IL",
            "get_Ctx",
            "get_Types",
            "get_Resolver",
            "GetHoistedVariableField", // Hoisted variable fields on state machine
            "RegisterLoopLocal",    // Loop locals stored in _locals dict instead of Ctx.Locals
            "SharpTS.Compilation.Emitters.IEmitterContext.get_IL",           // IEmitterContext impl
            "SharpTS.Compilation.Emitters.IEmitterContext.SetStackUnknown", // IEmitterContext impl
            "SharpTS.Compilation.Emitters.IEmitterContext.SetStackType",   // IEmitterContext impl
            // EmitLiteral is NO LONGER overridden (#441): the old override eagerly boxed numeric/
            // boolean literals and set StackType=Unknown, which desynced EmitConversionForParameter's
            // unboxed-double fast path and produced unverifiable IL for calls with numeric params.
            // The base EmitLiteral (unboxed value types + tracked StackType) is now used.
            // --- Genuinely different behavior ---
            // (EmitReturn/EmitTryCatch are inherited from AsyncFunctionMoveNextEmitter — see above.)
            "EmitForOf",            // #430/#645: for-await-of dispatch to the shared async-iterator lowering (sync for-of delegates to base)
            "EmitVarDeclaration",   // Capture indirection for outer variables
            "EmitVariable",         // Capture indirection
            "EmitAssign",           // Capture indirection
            "EmitStoreVariable",    // Capture indirection
            "EmitThis",             // Outer state machine capture
            "EmitSuper",            // This field indirection
            // (EmitAwait is inherited from AsyncFunctionMoveNextEmitter since #1122 — see above.)
            "EmitArrowFunction",    // Nested async arrows in state machine
            "EmitExpressionAsDouble", // Literal optimization
            // --- #766: a nested-block let/const that shadows an enclosing binding gets its own storage;
            // these overrides retoken the operator node so the read/write land on the shadow's own
            // field/local instead of the outer binding's hoisted field (async analog of #711).
            // (EmitVariable/EmitAssign/EmitVarDeclaration above are also rename-aware now.) ---
            "EmitConstDeclaration", // #766: route a shadowing const declaration to its own slot
            "EmitCompoundAssign",   // #766: route a shadowing compound assignment to its own slot
            "EmitLogicalAssign",    // #766: route a shadowing logical assignment to its own slot
            "EmitPrefixIncrement",  // #766: route a shadowing prefix ++/-- to its own slot
            "EmitPostfixIncrement", // #766: route a shadowing postfix ++/-- to its own slot
        },
        [typeof(GeneratorMoveNextEmitter)] = new()
        {
            // --- Infrastructure ---
            "get_IL",
            "get_Ctx",
            "get_Types",
            "get_Resolver",
            "GetThisField",
            "GetHoistedVariableField",
            "GetFunctionDCField",   // #674: exposes <>__functionDC so the base arrow emitter threads it in
            "ResolveCaptureSourceName", // #767: pivot a captured nested-block shadow's source to its renamed storage
            // --- Genuinely different behavior ---
            // (break/continue/loop-nav/EmitBranchToLabel are inherited from StateMachineExitRoutingEmitter.)
            "EmitReturn",           // Generator return: set state -2, return false
            "EmitTryCatch",         // Generator exception handling
            "EmitForOf",            // Hoisted enumerator for yield across loop boundaries
            "EmitForIn",            // #547: hoisted key-list/index for yield across for-in iterations
            "EmitYield",            // Core: yield value + suspend
            // #1105: EmitSuper/EmitDynamicImport are NO LONGER overridden — the old overrides
            // pushed `null`, silently miscompiling `super.x`/`import()` in a generator body. The
            // base implementations (hoisted-this + GetSuperMethod / module registry) work here.
            // #674: a captured-AND-mutated generator local is lifted into a shared function display
            // class; EmitArrowFunction still rejects the residual not-yet-DC-backed write case (e.g.
            // instance generator methods) so it fails fast instead of dropping the write.
            "EmitArrowFunction",
            // #1124: the closure-mutation + block-scope-rename variable overrides (EmitVariable, EmitAssign,
            // EmitStoreVariable, EmitVarDeclaration, EmitConstDeclaration, EmitCompoundAssign,
            // EmitLogicalAssign, EmitPrefixIncrement, EmitPostfixIncrement) are now inherited from the shared
            // IteratorMoveNextEmitter base — see that entry above.
            // --- #632: a throw in a catch/finally body routes through the enclosing finally(s) ---
            // (the generator-only throw routing; the break/continue/loop scaffolding is now in the base)
            "EmitThrow",            // Route a throw in a catch/finally body through the finally(s)
        },
        [typeof(AsyncGeneratorMoveNextEmitter)] = new()
        {
            // --- Infrastructure ---
            "get_IL",
            "get_Ctx",
            "get_Types",
            "get_Resolver",
            "GetThisField",
            "GetHoistedVariableField",
            // --- Genuinely different behavior ---
            // (break/continue/loop-nav/EmitBranchToLabel are inherited from StateMachineExitRoutingEmitter.)
            "EmitReturn",           // Async generator return: store in CurrentField + state -2
            "EmitTryCatch",         // Suspension-aware exception handling (flag-based)
            "EmitForOf",            // for-await-of + hoisted enumerator for yield/await in loops
            "EmitForAwaitOf",       // #430/#645: async-generator consumer keeps its own error-propagating lowering instead of the shared base one
            "EmitYield",            // Core: yield value + suspend
            "EmitAwait",            // Core: suspend/resume state machine
            "EmitArrowFunction",    // Display class in async generator context
            // #1105: EmitSuper is NO LONGER overridden — the old override pushed `null`, silently
            // miscompiling `super.x` in an async generator body. The base (hoisted-this +
            // GetSuperMethod) works here.
            // --- #725: route a captured-and-mutated local through the function display class ---
            "GetFunctionDCField",   // Exposes <>__functionDC so a capturing arrow threads it in
            // #1124: the closure-mutation + block-scope-rename variable overrides (EmitVariable, EmitAssign,
            // EmitStoreVariable, EmitVarDeclaration, EmitConstDeclaration, EmitCompoundAssign,
            // EmitLogicalAssign, EmitPrefixIncrement, EmitPostfixIncrement) are now inherited from the shared
            // IteratorMoveNextEmitter base — see that entry above.
            // --- #632: a throw in a catch/finally body routes through the enclosing finally(s) ---
            "EmitThrow",            // Route a throw in a catch/finally body through the finally(s)
        },
    };

    [Fact]
    public void StateMachineEmitters_OnlyOverrideAllowedMethods()
    {
        var baseTypes = new[] { typeof(ExpressionEmitterBase), typeof(StatementEmitterBase) };

        // Collect all virtual/abstract methods from the base classes
        var baseMethods = new HashSet<string>();
        foreach (var baseType in baseTypes)
        {
            foreach (var method in baseType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.IsVirtual && method.DeclaringType == baseType)
                    baseMethods.Add(method.Name);
            }
        }

        var failures = new List<string>();

        foreach (var (emitterType, allowedMethods) in AllowedOverrides)
        {
            var overriddenMethods = emitterType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.IsVirtual && baseMethods.Contains(m.Name))
                .Select(m => m.Name)
                .ToHashSet();

            // Check for overrides that aren't in the allowlist
            var unexpectedOverrides = overriddenMethods.Except(allowedMethods).ToList();
            foreach (var method in unexpectedOverrides)
            {
                failures.Add($"{emitterType.Name} overrides '{method}' without justification. " +
                    $"Either add it to AllowedOverrides with a comment explaining why, " +
                    $"or remove the override and use the base class implementation.");
            }

            // Log allowed overrides for visibility
            foreach (var method in overriddenMethods.Intersect(allowedMethods))
            {
                _output.WriteLine($"  OK: {emitterType.Name}.{method} (allowed override)");
            }
        }

        if (failures.Count > 0)
        {
            _output.WriteLine("\nUnexpected overrides found:");
            foreach (var failure in failures)
                _output.WriteLine($"  FAIL: {failure}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void AllowedOverrides_AreActuallyOverridden()
    {
        // Ensure the allowlist doesn't contain stale entries for methods that were
        // already removed (cleaned up). This prevents the allowlist from growing stale.
        var baseTypes = new[] { typeof(ExpressionEmitterBase), typeof(StatementEmitterBase) };
        var baseMethods = new HashSet<string>();
        foreach (var baseType in baseTypes)
        {
            foreach (var method in baseType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.IsVirtual && method.DeclaringType == baseType)
                    baseMethods.Add(method.Name);
            }
        }

        var staleEntries = new List<string>();

        foreach (var (emitterType, allowedMethods) in AllowedOverrides)
        {
            var actualOverrides = emitterType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.IsVirtual && baseMethods.Contains(m.Name))
                .Select(m => m.Name)
                .ToHashSet();

            var stale = allowedMethods.Except(actualOverrides).ToList();
            foreach (var method in stale)
            {
                staleEntries.Add($"{emitterType.Name} allowlist contains '{method}' but it is not actually overridden. Remove it from AllowedOverrides.");
            }
        }

        if (staleEntries.Count > 0)
        {
            _output.WriteLine("\nStale allowlist entries:");
            foreach (var entry in staleEntries)
                _output.WriteLine($"  STALE: {entry}");
        }

        Assert.Empty(staleEntries);
    }
}
