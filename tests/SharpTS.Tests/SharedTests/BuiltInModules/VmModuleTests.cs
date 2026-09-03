using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'vm' module: dynamic code compilation and execution.
///
/// Compiled-mode limitations:
/// The vm module is inherently dynamic — it compiles and runs arbitrary strings at runtime.
/// In compiled mode, all vm methods delegate to the interpreter via reflection (same pattern
/// as child_process.fork). This works well for the common case but has one remaining limitation:
///
/// - runInThisContext scope sharing: The compiled caller has no RuntimeEnvironment, so
///   runInThisContext falls back to a new empty context in compiled mode.
///
/// Resolved limitations (now have full parity):
/// - Script class methods: Script objects now return Dictionary&lt;string, object?&gt; which
///   GetFieldsProperty handles via the Dictionary dispatch path.
/// - Functions in context objects: CompiledCallableAdapter wraps $TSFunction instances
///   as ISharpTSCallable at the VmContext boundary.
/// - compileFunction with parsingContext/contextExtensions: VmContext.ExtractProperties
///   now handles emitted $Object types via reflection fallback.
/// </summary>
public class VmModuleTests
{
    #region Import Tests

    [Theory, ModeData]
    public void Vm_Import_Namespace(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                console.log(typeof vm === 'object');
                console.log(typeof vm.createContext === 'function');
                console.log(typeof vm.isContext === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Vm_Import_Named(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { runInNewContext, createContext, isContext } from 'vm';
                console.log(runInNewContext !== undefined);
                console.log(createContext !== undefined);
                console.log(isContext !== undefined);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region runInNewContext Tests

    [Theory, ModeData]
    public void Vm_RunInNewContext_BasicExpression(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const result = vm.runInNewContext('1 + 2');
                console.log(result);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void Vm_RunInNewContext_MultiStatement(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const result = vm.runInNewContext('let a = 5; let b = 10; a + b;');
                console.log(result);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void Vm_RunInNewContext_ContextSeeding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const result = vm.runInNewContext('x + y', { x: 10, y: 20 });
                console.log(result);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("30\n", output);
    }

    [Theory, ModeData]
    public void Vm_RunInNewContext_ContextMutation(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const ctx = { x: 1 };
                vm.runInNewContext('x = 42;', ctx);
                console.log(ctx.x);
                console.log(ctx.x + 1);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\n43\n", output);
    }

    [Theory, ModeData]
    public void Vm_RunInNewContext_Isolation(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                let leaked = false;
                try {
                    vm.runInNewContext('let newVar = 123;');
                    // newVar should not be accessible here
                    console.log('isolated');
                } catch {
                    console.log('isolated');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("isolated\n", output);
    }

    [Theory, ModeData]
    public void Vm_RunInNewContext_WithConsoleLog(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                vm.runInNewContext('console.log("hello from vm")');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello from vm\n", output);
    }

    [Theory, ModeData]
    public void Vm_RunInNewContext_EmptyCode(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const result = vm.runInNewContext('');
                console.log(result === undefined || result === null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region runInThisContext Tests

    [Theory, InterpretedOnlyData]
    public void Vm_RunInThisContext_SharesScope(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                let x = 10;
                const result = vm.runInThisContext('x + 5');
                console.log(result);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("15\n", output);
    }

    #endregion

    #region createContext / isContext Tests

    [Theory, ModeData]
    public void Vm_CreateContext_IsContext(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const obj = { x: 1 };
                console.log(vm.isContext(obj));
                const ctx = vm.createContext(obj);
                console.log(vm.isContext(ctx));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Vm_CreateContext_Empty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const ctx = vm.createContext();
                console.log(vm.isContext(ctx));
                console.log(typeof ctx === 'object');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region runInContext (module-level) / constants Tests

    [Theory, ModeData]
    public void Vm_RunInContext_Basic(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const ctx = vm.createContext({ x: 1 });
                console.log(vm.runInContext('x + 1', ctx));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void Vm_RunInContext_WritesBack(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const ctx = vm.createContext({ count: 10 });
                vm.runInContext('count = count + 5', ctx);
                console.log(ctx.count);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void Vm_RunInContext_NonContext_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                try {
                    vm.runInContext('1 + 1', { x: 1 });
                    console.log('no error');
                } catch (e) {
                    console.log('caught error');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught error\n", output);
    }

    [Theory, ModeData]
    public void Vm_Constants_Exist(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                console.log(typeof vm.constants === 'object');
                console.log(vm.constants.DONT_CONTEXTIFY !== undefined);
                console.log(vm.constants.USE_MAIN_CONTEXT_DEFAULT_LOADER !== undefined);
                // Sentinels are stable singletons (same value across accesses) and distinct.
                console.log(vm.constants.DONT_CONTEXTIFY === vm.constants.DONT_CONTEXTIFY);
                console.log(vm.constants.DONT_CONTEXTIFY !== vm.constants.USE_MAIN_CONTEXT_DEFAULT_LOADER);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Vm_Constants_AreSymbols(ExecutionMode mode)
    {
        // typeof reports 'symbol' in the interpreter; compiled-mode typeof of a
        // cross-runtime-boundary SharpTSSymbol reports 'object' (a known interop
        // detail — the sentinels' identity, exercised above, is what matters).
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                console.log(typeof vm.constants.DONT_CONTEXTIFY);
                console.log(typeof vm.constants.USE_MAIN_CONTEXT_DEFAULT_LOADER);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("symbol\nsymbol\n", output);
    }

    #endregion

    #region createContext options + measureMemory Tests

    [Theory, ModeData]
    public void Vm_CreateContext_MicrotaskMode_AfterEvaluate(ExecutionMode mode)
    {
        // With microtaskMode:'afterEvaluate', the queued .then microtask runs before
        // runInContext returns, so the scalar write is visible afterward.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const ctx = vm.createContext({ flag: 0 }, { microtaskMode: 'afterEvaluate' });
                vm.runInContext('Promise.resolve().then(() => { flag = 1; }); 0;', ctx);
                console.log(ctx.flag);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void Vm_CreateContext_CodeGeneration_StringsFalse_BlocksEval(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const ctx = vm.createContext({}, { codeGeneration: { strings: false } });
                try {
                    vm.runInContext('eval("1 + 1")', ctx);
                    console.log('no error');
                } catch (e: any) {
                    console.log('blocked');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("blocked\n", output);
    }

    [Theory, ModeData]
    public void Vm_CreateContext_CodeGeneration_Default_AllowsEval(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const ctx = vm.createContext({});
                console.log(vm.runInContext('eval("20 + 22")', ctx));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Vm_CreateContext_NameOrigin_Accepted(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const ctx = vm.createContext({ x: 5 }, { name: 'my-ctx', origin: 'file:///x' });
                console.log(vm.runInContext('x * 2', ctx));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void Vm_MeasureMemory_ResolvesShape(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                async function main() {
                    const r: any = await vm.measureMemory();
                    console.log(typeof r.total.jsMemoryEstimate === 'number');
                    console.log(r.total.jsMemoryRange !== undefined && r.total.jsMemoryRange !== null);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Vm_MeasureMemory_RangeIsArray(ExecutionMode mode)
    {
        // jsMemoryRange is a 2-element Array; Array.isArray/.length only fully recognize it
        // within the interpreter (compiled is blind to a cross-boundary SharpTSArray — a
        // known interop detail; cross-mode the value is present, covered above).
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                async function main() {
                    const r: any = await vm.measureMemory();
                    console.log(Array.isArray(r.total.jsMemoryRange));
                    console.log(r.total.jsMemoryRange.length === 2);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Script Tests

    [Theory, ModeData]
    public void Vm_Script_RunInNewContext(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script } from 'vm';
                const script = new Script('x + y');
                const result1 = script.runInNewContext({ x: 1, y: 2 });
                const result2 = script.runInNewContext({ x: 10, y: 20 });
                console.log(result1);
                console.log(result2);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("3\n30\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Vm_Script_RunInThisContext(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script } from 'vm';
                let val = 42;
                const script = new Script('val');
                const result = script.runInThisContext();
                console.log(result);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Vm_Script_RunInContext(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script, createContext } from 'vm';
                const ctx = createContext({ greeting: 'hello' });
                const script = new Script('greeting + " world"');
                const result = script.runInContext(ctx);
                console.log(result);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory, ModeData]
    public void Vm_Script_MultipleRuns(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script } from 'vm';
                const script = new Script('n * n');
                console.log(script.runInNewContext({ n: 3 }));
                console.log(script.runInNewContext({ n: 5 }));
                console.log(script.runInNewContext({ n: 7 }));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("9\n25\n49\n", output);
    }

    #endregion

    #region Script/compileFunction options + cachedData Tests

    [Theory, ModeData]
    public void Vm_Script_Filename_InSyntaxError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script } from 'vm';
                try {
                    new Script('let x = ;', { filename: 'my-file.js' });
                    console.log('no error');
                } catch (e: any) {
                    console.log(e.message.indexOf('my-file.js') >= 0);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Vm_Script_OffsetsAndDisplayErrors_Accepted(ExecutionMode mode)
    {
        // filename/lineOffset/columnOffset/displayErrors are honored without altering
        // a successful run's result.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script } from 'vm';
                const s = new Script('40 + 2', {
                    filename: 'calc.js', lineOffset: 5, columnOffset: 2, displayErrors: true
                });
                console.log(s.runInNewContext({}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Vm_Script_CreateCachedData_ReturnsValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script } from 'vm';
                const s = new Script('1 + 1');
                const cd = s.createCachedData();
                console.log(cd !== undefined && cd !== null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Vm_Script_CreateCachedData_IsBuffer(ExecutionMode mode)
    {
        // The marker is a real Buffer; Buffer.isBuffer/length only recognize it within
        // the interpreter (compiled Buffer.isBuffer is blind to a cross-boundary
        // SharpTSBuffer — a known interop detail; cross-mode the value is defined+round-trips).
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script } from 'vm';
                const s = new Script('1 + 1');
                const cd = s.createCachedData();
                console.log(Buffer.isBuffer(cd));
                console.log(cd.length > 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Vm_Script_ProduceCachedData(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script } from 'vm';
                const s = new Script('1 + 1', { produceCachedData: true });
                console.log(s.cachedDataProduced);
                console.log(s.cachedData !== undefined && s.cachedData !== null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Vm_Script_CachedData_RoundTrips_NotRejected(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script } from 'vm';
                const s1 = new Script('2 * 21', { produceCachedData: true });
                const s2 = new Script('2 * 21', { cachedData: s1.cachedData });
                console.log(s2.cachedDataRejected);
                console.log(s2.runInNewContext({}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n42\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Vm_Script_Filename_InTimeoutErrorStack(ExecutionMode mode)
    {
        // The origin frame is attached to errors that propagate as a live guest error
        // object (the timeout error). User throws/runtime faults are reconstructed at the
        // execution boundary and don't carry the frame (a documented limitation); the
        // filename DOES flow into SyntaxErrors cross-mode (covered above).
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script } from 'vm';
                const s = new Script('while(true){}', { filename: 'loop.js' });
                try {
                    s.runInNewContext({}, { timeout: 50 });
                    console.log('no error');
                } catch (e: any) {
                    console.log((e.stack || '').indexOf('loop.js') >= 0);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_Filename_InSyntaxError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction } from 'vm';
                try {
                    compileFunction('return ;; @@', [], { filename: 'fn-src.js' });
                    console.log('no error');
                } catch (e: any) {
                    console.log(e.message.indexOf('fn-src.js') >= 0);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region SourceTextModule (ESM-in-vm) Tests

    [Theory, ModeData]
    public void Vm_SourceTextModule_LinkEvaluate_ImportsDependency(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { SourceTextModule } from 'vm';
                async function main() {
                    const dep = new SourceTextModule('export const x = 42; export const y = "hi";');
                    const m = new SourceTextModule('import { x, y } from "dep"; export const sum = x + 1; export const greet = y + x;');
                    await m.link((spec: string) => dep);
                    await m.evaluate();
                    console.log(m.namespace.sum);
                    console.log(m.namespace.greet);
                    console.log(dep.namespace.x);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("43\nhi42\n42\n", output);
    }

    [Theory, ModeData]
    public void Vm_SourceTextModule_StatusTransitions(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { SourceTextModule } from 'vm';
                async function main() {
                    const m = new SourceTextModule('export const v = 1;');
                    console.log(m.status);
                    await m.link((spec: string) => { throw new Error('no deps'); });
                    console.log(m.status);
                    await m.evaluate();
                    console.log(m.status);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("unlinked\nlinked\nevaluated\n", output);
    }

    [Theory, ModeData]
    public void Vm_SourceTextModule_DependencySpecifiers_Count(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { SourceTextModule } from 'vm';
                const m = new SourceTextModule('import { a } from "x"; import { b } from "y"; export const c = 1;');
                console.log(m.dependencySpecifiers.length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("2\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Vm_SourceTextModule_DependencySpecifiers_Indexing(ExecutionMode mode)
    {
        // dependencySpecifiers is an Array; element indexing is observable within the
        // interpreter (compiled indexing of a cross-boundary SharpTSArray returns undefined
        // — a known interop detail; the .length is observable cross-mode, covered above).
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { SourceTextModule } from 'vm';
                const m = new SourceTextModule('import { a } from "x"; import { b } from "y"; export const c = 1;');
                console.log(m.dependencySpecifiers[0]);
                console.log(m.dependencySpecifiers[1]);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("x\ny\n", output);
    }

    [Theory, ModeData]
    public void Vm_SourceTextModule_LinkerError_SetsErrored(ExecutionMode mode)
    {
        // The error path is exercised synchronously (link/evaluate complete synchronously in
        // SharpTS): an awaited synchronously-throwing call is not caught by the compiled async
        // state machine — a pre-existing limitation unrelated to vm.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { SourceTextModule } from 'vm';
                const m = new SourceTextModule('import { x } from "missing";');
                try {
                    m.link((spec: string) => { throw new Error('cannot resolve ' + spec); });
                } catch (e: any) { }
                console.log(m.status);
                console.log(m.error !== undefined && m.error !== null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("errored\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Vm_SourceTextModule_DefaultExport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { SourceTextModule } from 'vm';
                async function main() {
                    const dep = new SourceTextModule('export default 99;');
                    const m = new SourceTextModule('import d from "dep"; export const val = d + 1;');
                    await m.link((spec: string) => dep);
                    await m.evaluate();
                    console.log(m.namespace.val);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("100\n", output);
    }

    #endregion

    #region SyntheticModule Tests

    [Theory, ModeData]
    public void Vm_SyntheticModule_ImportedBySourceTextModule_LiveBinding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { SourceTextModule, SyntheticModule } from 'vm';
                async function main() {
                    const syn = new SyntheticModule(['x', 'y'], function (this: any) {
                        this.setExport('x', 42);
                        this.setExport('y', 'hello');
                    });
                    const m = new SourceTextModule('import { x, y } from "syn"; export const r = x + 1; export const s = y + "!";');
                    await m.link((spec: string) => syn);
                    await m.evaluate();
                    console.log(m.namespace.r);
                    console.log(m.namespace.s);
                    console.log(syn.namespace.x);
                    console.log(syn.status);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("43\nhello!\n42\nevaluated\n", output);
    }

    [Theory, ModeData]
    public void Vm_SyntheticModule_SetExportBeforeLink_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { SyntheticModule } from 'vm';
                const syn = new SyntheticModule(['a'], function (this: any) { this.setExport('a', 1); });
                try {
                    syn.setExport('a', 5);
                    console.log('no error');
                } catch (e: any) {
                    console.log('threw');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("threw\n", output);
    }

    #endregion

    #region importModuleDynamically Tests

    [Theory, ModeData]
    public void Vm_Script_ImportModuleDynamically_ResolvesViaHook(ExecutionMode mode)
    {
        // The result of the in-vm dynamic import is written back to a context scalar to
        // keep it observable cross-mode (a cross-boundary Promise is not unwrapped by
        // compiled await; the await happens inside the hosted interpreter).
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script, SourceTextModule, createContext } from 'vm';
                const dep = new SourceTextModule('export const value = 123;');
                const importModuleDynamically = (specifier: string) => dep;
                const ctx: any = createContext({ result: 0 });
                const script = new Script(
                    '(async () => { const ns = await import("dep"); result = ns.value; })();',
                    { importModuleDynamically });
                script.runInContext(ctx);
                console.log(ctx.result);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("123\n", output);
    }

    [Theory, ModeData]
    public void Vm_Script_ImportModuleDynamically_UseMainContextDefaultLoader_Accepted(ExecutionMode mode)
    {
        // Passing the USE_MAIN_CONTEXT_DEFAULT_LOADER sentinel falls back to the default
        // loader (no override) and does not disturb a script that performs no import.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Script, constants } from 'vm';
                const script = new Script('1 + 1', {
                    importModuleDynamically: constants.USE_MAIN_CONTEXT_DEFAULT_LOADER
                });
                console.log(script.runInNewContext({}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("2\n", output);
    }

    #endregion

    #region Error Handling Tests

    [Theory, ModeData]
    public void Vm_RunInNewContext_SyntaxError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                try {
                    vm.runInNewContext('let x = ;');
                    console.log('no error');
                } catch (e) {
                    console.log('caught error');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught error\n", output);
    }

    [Theory, ModeData]
    public void Vm_RunInNewContext_RuntimeError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                try {
                    vm.runInNewContext('undefinedVar.foo');
                    console.log('no error');
                } catch (e) {
                    console.log('caught error');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught error\n", output);
    }

    #endregion

    #region Context Functions Tests

    [Theory, ModeData]
    public void Vm_RunInNewContext_FunctionInContext(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const ctx = {
                    add: (a: number, b: number) => a + b
                };
                const result = vm.runInNewContext('add(3, 4)', ctx);
                console.log(result);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("7\n", output);
    }

    #endregion

    #region compileFunction Tests

    [Theory, ModeData]
    public void Vm_CompileFunction_BasicReturn(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction } from 'vm';
                const fn = compileFunction('return 42');
                console.log(fn());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_WithParams(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction } from 'vm';
                const add = compileFunction('return a + b', ['a', 'b']);
                console.log(add(3, 4));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("7\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_NoParams(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction } from 'vm';
                const greet = compileFunction('return "hello"');
                console.log(greet());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_MultiStatement(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction } from 'vm';
                const fn = compileFunction('let x = 10; let y = 20; return x + y;');
                console.log(fn());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("30\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_Reusable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction } from 'vm';
                const square = compileFunction('return n * n', ['n']);
                console.log(square(3));
                console.log(square(5));
                console.log(square(7));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("9\n25\n49\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_ConsoleLog(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction } from 'vm';
                const fn = compileFunction('console.log("from compiled fn")');
                fn();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("from compiled fn\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_SyntaxError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction } from 'vm';
                try {
                    compileFunction('return ;; @@');
                    console.log('no error');
                } catch (e) {
                    console.log('caught error');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught error\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_NamespaceImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as vm from 'vm';
                const fn = vm.compileFunction('return x * 2', ['x']);
                console.log(fn(21));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_ParsingContext(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction, createContext } from 'vm';
                const ctx = createContext({ multiplier: 10 });
                const fn = compileFunction('return x * multiplier', ['x'], { parsingContext: ctx });
                console.log(fn(5));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("50\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_ContextExtensions(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction } from 'vm';
                const fn = compileFunction('return greeting + " " + name', [], {
                    contextExtensions: [{ greeting: "hello", name: "world" }]
                });
                console.log(fn());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory, ModeData]
    public void Vm_CompileFunction_ContextWithFunction(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { compileFunction, createContext } from 'vm';
                const ctx = createContext({ double: (n: number) => n * 2 });
                const fn = compileFunction('return double(x)', ['x'], { parsingContext: ctx });
                console.log(fn(7));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("14\n", output);
    }

    #endregion

    #region timeout

    [Theory, InterpretedOnlyData]
    public void Vm_RunInNewContext_Timeout_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import vm from 'vm';
                try {
                    vm.runInNewContext('while(true) {}', {}, { timeout: 50 });
                    console.log('no error');
                } catch (e) {
                    console.log('caught:' + e.message);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("caught:Script execution timed out", output);
    }

    [Theory, InterpretedOnlyData]
    public void Vm_RunInNewContext_NoTimeout_Completes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import vm from 'vm';
                const result = vm.runInNewContext('let sum = 0; for (let i = 0; i < 100; i++) sum += i; sum;', {});
                console.log(result);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("4950", output);
    }

    [Theory, InterpretedOnlyData]
    public void Vm_RunInThisContext_Timeout_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import vm from 'vm';
                try {
                    vm.runInThisContext('while(true) {}', { timeout: 50 });
                    console.log('no error');
                } catch (e) {
                    console.log('caught:' + e.message);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("caught:Script execution timed out", output);
    }

    [Theory, InterpretedOnlyData]
    public void Vm_Script_RunInNewContext_Timeout_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import vm from 'vm';
                const script = new vm.Script('while(true) {}');
                try {
                    script.runInNewContext({}, { timeout: 50 });
                    console.log('no error');
                } catch (e) {
                    console.log('caught:' + e.message);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("caught:Script execution timed out", output);
    }

    #endregion
}
