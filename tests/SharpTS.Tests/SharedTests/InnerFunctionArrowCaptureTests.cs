using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #307: inner function declarations nested inside
/// arrow functions / function expressions must capture enclosing locals by
/// live reference, not by value-snapshot taken at hoist time. Function
/// declarations hoist to the top of the enclosing body — before the
/// statements that assign the captured variables run — so snapshots read
/// null/stale. Compiled mode routes these captures through a live
/// $arrowScopeDC reference to the enclosing arrow's scope display class
/// (threaded through intermediate scopes, with extra per-source references
/// when captures span multiple ancestor arrow scopes). Root cause of the
/// lodash init failure (#302).
/// </summary>
public class InnerFunctionArrowCaptureTests
{
    [Theory, ModeData]
    public void FunctionDecl_InArrow_ReadsConstAssignedAfterHoist(ExecutionMode mode)
    {
        // Minimal repro from #307: bgt is hoisted before `const Obj = 42` runs.
        var source = """
            const ric = () => {
              const Obj = 42;
              function bgt() { return Obj; }
              return bgt;
            };
            console.log(ric()());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void FunctionDecl_GrandparentArrowCapture_ThroughIntermediateArrow(ExecutionMode mode)
    {
        var source = """
            const outer = () => {
              const g = "grand";
              const mid = () => {
                function leaf() { return g; }
                return leaf;
              };
              return mid();
            };
            console.log(outer()());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("grand\n", output);
    }

    [Theory, ModeData]
    public void FunctionDecl_GrandparentArrowCapture_ThroughIntermediateFunctionDecl(ExecutionMode mode)
    {
        // The intermediate scope is a function DECLARATION: the live reference
        // must pass through its display class ($arrowScopeDC pass-through).
        var source = """
            const outer = () => {
              const g = "grand2";
              function mid() {
                function leaf() { return g; }
                return leaf;
              }
              return mid();
            };
            console.log(outer()());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("grand2\n", output);
    }

    [Theory, ModeData]
    public void FunctionDecl_InArrow_MutationsVisibleBothWays(ExecutionMode mode)
    {
        // Writes from the arrow body must be visible inside the function
        // declaration, and the function's writes must be visible outside.
        var source = """
            const outer = () => {
              let counter = 0;
              function bump() { counter = counter + 1; return counter; }
              counter = 10;
              bump();
              bump();
              return counter;
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("12\n", output);
    }

    [Theory, ModeData]
    public void FunctionDecls_InFunctionExpression_PeerReferences(ExecutionMode mode)
    {
        // lodash idiom: hoisted peers referencing each other before their
        // textual position, inside a function-expression wrapper.
        var source = """
            const outer = function() {
              function getValue(o: any, k: string) { return o == null ? undefined : o[k]; }
              function getNative(o: any, k: string) { const v = getValue(o, k); return v; }
              var nativeCreate = getNative({create: 99}, 'create');
              return nativeCreate;
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    [Theory, ModeData]
    public void FunctionDecl_HoistedBeforeVarAssignment_CalledAfter(ExecutionMode mode)
    {
        var source = """
            const outer = function() {
              var dep: any;
              function reader() { return dep; }
              dep = "assigned-later";
              return reader();
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("assigned-later\n", output);
    }

    [Theory, ModeData]
    public void Closure_CapturesFromTwoAncestorArrowScopes(ExecutionMode mode)
    {
        // lodash shortOut shape: the returned closure captures `tag` from the
        // OUTER wrapper and `nativeNow` from the inner function expression —
        // two distinct ancestor arrow scopes, both assigned after the points
        // where snapshots would have been taken. Requires the extra
        // (multi-source) scope DC references.
        var source = """
            const result = (function() {
              var tag: any;
              var run = (function run(context: any) {
                var nativeNow: any;
                function shortOut(func: any) {
                  return function() {
                    return func(nativeNow()) + "/" + tag;
                  };
                }
                var wrapped = shortOut(function(x: any) { return "v" + x; });
                nativeNow = function() { return 7; };
                return wrapped;
              });
              tag = "T";
              return run(null)();
            })();
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("v7/T\n", output);
    }

    [Theory, ModeData]
    public void CapturedParameter_ReassignedInBody_ReadsStayCurrent(ExecutionMode mode)
    {
        // lodash runInContext shape: a captured parameter is reassigned at the
        // top of the body; later reads in the SAME body must see the new value
        // (stores go to the scope DC — the arg slot must stay in sync).
        var source = """
            const run = function(context: any) {
              context = context == null ? { Date: "real" } : context;
              var d = context.Date;
              function reader() { return context.Date; }
              return d + "/" + reader();
            };
            console.log(run(null));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("real/real\n", output);
    }

    [Theory, ModeData]
    public void ValueForm_ObjectCall_Coerces(ExecutionMode mode)
    {
        // lodash overArg(Object.keys, Object): `Object` held as a value and
        // invoked as a function (ToObject). Objects pass through unchanged.
        var source = """
            var transform: any = Object;
            var keysFn: any = Object.keys;
            function overArg(func: any, t: any) { return function(arg: any) { return func(t(arg)); }; }
            var nativeKeys = overArg(keysFn, transform);
            console.log(nativeKeys({a: 1, b: 2}).length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void ValueForm_ObjectCreate_SingleArg(ExecutionMode mode)
    {
        // Value-form Object.create(proto) under-application: the padded props
        // argument must read as ABSENT/undefined, not as JS null (which throws
        // per ECMA-262 §20.1.2.2 step 3).
        var source = """
            var oc: any = Object.create;
            var p = { x: 1 };
            var o = oc(p);
            console.log(o.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void ValueForm_DateNow_AsValue(ExecutionMode mode)
    {
        // lodash: `var nativeNow = Date.now;` then nativeNow() — value-form
        // static member access on the Date constructor.
        var source = """
            var d0 = new Date();
            var nativeNow: any = Date.now;
            console.log(typeof nativeNow);
            console.log(nativeNow() > 0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\ntrue\n", output);
    }

    // #321: ++/-- on a parameter captured into a scope display class must
    // dual-write the arg slot, not just the DC field. Same-body reads resolve
    // the arg slot before the DC, so a DC-only store leaves the direct read
    // seeing the stale original argument while the closure read sees the new
    // value. Mirrors the assignment-path dual-write from #307/#313.
    [Theory, ModeData]
    public void CapturedParam_PostfixIncrement_SyncsArgSlotAndDC(ExecutionMode mode)
    {
        var source = """
            const outer = () => {
              function counter2(n: number) {
                n++;
                const read = () => n;
                return n * 1000 + read();   // direct (arg slot) + closure (DC)
              }
              return counter2(7);
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8008\n", output);
    }

    [Theory, ModeData]
    public void CapturedParam_PrefixDecrement_SyncsArgSlotAndDC(ExecutionMode mode)
    {
        // Prefix form + decrement + arrow parameter (arrow scope DC branch).
        var source = """
            const a1 = (n: number) => {
              --n;
              const read = () => n;
              return n * 1000 + read();
            };
            console.log(a1(7));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6006\n", output);
    }

    [Theory, ModeData]
    public void CapturedUntypedParam_PostfixIncrement_SyncsArgSlotAndDC(ExecutionMode mode)
    {
        // Untyped (any) parameter: arg slot is object, so the dual-write must
        // not emit an unbox/castclass conversion (EmitConvertForParamSlot no-op).
        var source = """
            function g1(n) {
              n++;
              const read = () => n;
              return n * 1000 + read();
            }
            console.log(g1(7));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8008\n", output);
    }

    // #421: a `const`/`let` whose initializer creates a closure capturing that
    // same variable (self-reference). The closure's display-class field was
    // populated with a snapshot of the local taken BEFORE the assignment, so it
    // saw the stale/previous value — null on the first loop iteration. The
    // declaration now writes the freshly-assigned value back into the closure's
    // DC field after the store, while keeping per-iteration fresh-binding.
    [Theory, ModeData]
    public void SelfReferentialConst_SingleDeclaration_ClosureSeesValue(ExecutionMode mode)
    {
        var source = """
            function make(cb: any) { return { cb: cb, val: 42 }; }
            const thing = make(() => thing);
            console.log(thing.cb() === thing);
            console.log(thing.cb().val);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n42\n", output);
    }

    [Theory, ModeData]
    public void SelfReferentialConst_InLoop_EachClosureSeesOwnBinding(ExecutionMode mode)
    {
        // The first iteration's closure captured null (snapshot before the
        // assignment); every other iteration captured the PREVIOUS iteration's
        // value (off-by-one). Each closure must return its OWN object — the
        // per-iteration fresh binding — so the self-reference identity holds.
        var source = """
            function make(id: number, cb: any) { return { id: id, cb: cb }; }
            const things: any[] = [];
            for (let i = 0; i < 5; i++) {
              const thing = make(i, () => thing);
              things.push(thing);
            }
            let wrong = 0;
            for (let k = 0; k < things.length; k++) {
              if (things[k].cb() !== things[k]) wrong++;
            }
            console.log(wrong);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }
}
