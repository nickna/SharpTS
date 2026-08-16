using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the async_hooks module AsyncLocalStorage class.
/// </summary>
public class AsyncHooksTests
{
    #region Import Patterns

    [Theory, ModeData]
    public void AsyncHooks_NamedImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                console.log(typeof als === 'object');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void AsyncHooks_NamespaceImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as async_hooks from 'async_hooks';
                const als = new async_hooks.AsyncLocalStorage();
                console.log(typeof als === 'object');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void AsyncHooks_NodePrefixImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'node:async_hooks';
                const als = new AsyncLocalStorage();
                console.log(typeof als === 'object');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Constructor

    [Theory, ModeData]
    public void AsyncLocalStorage_Constructor(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                console.log(als !== null);
                console.log(als !== undefined);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region getStore

    [Theory, ModeData]
    public void AsyncLocalStorage_GetStore_ReturnsUndefined_Outside(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                console.log(als.getStore() == undefined);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region run

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_SetsStore(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.run("hello", () => {
                    console.log(als.getStore());
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_RestoresAfter(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.run("inside", () => {});
                console.log(als.getStore() == undefined);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_ReturnsCallbackResult(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                const result = als.run("store", () => 42);
                console.log(result);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_NestedRuns(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.run("outer", () => {
                    console.log(als.getStore());
                    als.run("inner", () => {
                        console.log(als.getStore());
                    });
                    console.log(als.getStore());
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("outer\ninner\nouter\n", output);
    }

    #endregion

    #region enterWith

    [Theory, ModeData]
    public void AsyncLocalStorage_EnterWith_SetsStore(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.enterWith("entered");
                console.log(als.getStore());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("entered\n", output);
    }

    #endregion

    #region exit

    [Theory, ModeData]
    public void AsyncLocalStorage_Exit_ClearsStore(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.run("active", () => {
                    als.exit(() => {
                        console.log(als.getStore() == undefined);
                    });
                    console.log(als.getStore());
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\nactive\n", output);
    }

    #endregion

    #region disable

    [Theory, ModeData]
    public void AsyncLocalStorage_Disable_PreventsAccess(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.run("data", () => {
                    console.log(als.getStore());
                    als.disable();
                    console.log(als.getStore() == undefined);
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("data\ntrue\n", output);
    }

    #endregion

    #region Multiple Instances

    [Theory, ModeData]
    public void AsyncLocalStorage_MultipleInstances_Independent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als1 = new AsyncLocalStorage();
                const als2 = new AsyncLocalStorage();
                als1.run("first", () => {
                    als2.run("second", () => {
                        console.log(als1.getStore());
                        console.log(als2.getStore());
                    });
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("first\nsecond\n", output);
    }

    #endregion

    #region Store with Object Values

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_WithObjectStore(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.run({ requestId: "abc-123" }, () => {
                    const store = als.getStore();
                    console.log(store.requestId);
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("abc-123\n", output);
    }

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_WithNullStore(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.run(null, () => {
                    console.log(als.getStore() === null);
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_WithNumberStore(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.run(42, () => {
                    console.log(als.getStore());
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Async Context Propagation

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_AsyncCallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.run("async-value", async () => {
                    console.log(als.getStore());
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("async-value\n", output);
    }

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_AsyncCallback_AcrossAwait(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();

                async function getStoreValue(): Promise<string> {
                    return als.getStore() as string;
                }

                als.run("across-await", async () => {
                    const result = await getStoreValue();
                    console.log(result);
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("across-await\n", output);
    }

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_AsyncCallback_NestedAwait(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();

                async function delay(): Promise<void> {
                    await Promise.resolve();
                }

                als.run("nested-await", async () => {
                    await delay();
                    await delay();
                    console.log(als.getStore());
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("nested-await\n", output);
    }

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_PromiseThen(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.run("in-then", () => {
                    Promise.resolve().then(() => {
                        console.log(als.getStore());
                    });
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("in-then\n", output);
    }

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_NestedAsyncRuns(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();

                async function innerRun() {
                    console.log(als.getStore());
                }

                als.run("outer", async () => {
                    console.log(als.getStore());
                    als.run("inner", innerRun);
                    console.log(als.getStore());
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("outer\ninner\nouter\n", output);
    }

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_AsyncWithObjectStore(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();

                async function getRequestId(): Promise<string> {
                    const store = als.getStore() as any;
                    return store.requestId;
                }

                als.run({ requestId: "req-456" }, async () => {
                    const id = await getRequestId();
                    console.log(id);
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("req-456\n", output);
    }

    #endregion

    #region Cross-Module Access

    [Theory, ModeData]
    public void AsyncLocalStorage_CrossModule_SharedInstance(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["storage.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                export const als = new AsyncLocalStorage();
                """,
            ["helper.ts"] = """
                import { als } from './storage';
                export function getStore(): any {
                    return als.getStore();
                }
                """,
            ["main.ts"] = """
                import { als } from './storage';
                import { getStore } from './helper';
                als.run("shared", () => {
                    console.log(getStore());
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("shared\n", output);
    }

    #endregion

    #region enterWith Advanced

    [Theory, ModeData]
    public void AsyncLocalStorage_EnterWith_OverwritesPrevious(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                als.enterWith("first");
                console.log(als.getStore());
                als.enterWith("second");
                console.log(als.getStore());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("first\nsecond\n", output);
    }

    #endregion

    #region Run Restores on Exception

    [Theory, ModeData]
    public void AsyncLocalStorage_Run_RestoresOnException(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als = new AsyncLocalStorage();
                try {
                    als.run("will-throw", () => {
                        throw new Error("oops");
                    });
                } catch (e) {
                    // Store should be restored even after exception
                    console.log(als.getStore() == undefined);
                }
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Multiple Instances Advanced

    [Theory, ModeData]
    public void AsyncLocalStorage_MultipleInstances_NestedRunsDifferentInstances(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { AsyncLocalStorage } from 'async_hooks';
                const als1 = new AsyncLocalStorage();
                const als2 = new AsyncLocalStorage();
                als1.run("a1", () => {
                    als2.run("a2", () => {
                        als1.run("b1", () => {
                            console.log(als1.getStore());
                            console.log(als2.getStore());
                        });
                        console.log(als1.getStore());
                        console.log(als2.getStore());
                    });
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("b1\na2\na1\na2\n", output);
    }

    #endregion
}
