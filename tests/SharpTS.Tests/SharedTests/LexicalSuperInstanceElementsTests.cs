using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class LexicalSuperInstanceElementsTests
{
    [Theory, ModeData]
    public void LexicalSuperInitializesFieldsBeforeReturningToTheArrow(ExecutionMode mode)
    {
        Assert.Equal("1\n9 1\n", TestHarness.RunUnchecked("""
            let calls = 0;
            const C = class extends class { constructor() { calls++; } } {
                x = 1;
                constructor() {
                    const run = () => { super(); console.log(this.x); this.x = 9; };
                    run();
                }
            };
            const value = new C();
            console.log(value.x, calls);
            """, mode));
    }

    [Theory, ModeData]
    public void InitializersRetainTheirClassEnvironmentAndBoundReceiver(ExecutionMode mode)
    {
        Assert.Equal("8 8 true\n", TestHarness.RunUnchecked("""
            function create() {
                let outer = 4;
                return class extends class { constructor() {} } {
                    x = outer;
                    read = () => this.x;
                    target = new.target;
                    constructor() {
                        const outer = 99;
                        const run = () => () => super();
                        run()();
                        this.x = 8;
                    }
                };
            }
            const C = create();
            const value = new C();
            console.log(value.x, value.read(), value.target === undefined);
            """, mode));
    }

    [Theory, ModeData]
    public void ReplacementReceiversAndMixedSuperCallsInitializeOnlyOnce(ExecutionMode mode)
    {
        Assert.Equal("true\ntrue 7 parent,field,parent\n", TestHarness.RunUnchecked("""
            const events = [];
            const replacement = {};
            const C = class extends class {
                constructor() { events.push('parent'); return replacement; }
            } {
                x = (events.push('field'), 7);
                constructor() {
                    const run = () => super();
                    run();
                    try { super(); } catch (error) { console.log(error instanceof ReferenceError); }
                }
            };
            const value = new C();
            console.log(value === replacement, value.x, events.join(','));
            """, mode));
    }

    [Theory, ModeData]
    public void ThrowingInitializersLeaveThisBoundWithoutReinitializingFields(ExecutionMode mode)
    {
        Assert.Equal("true\ntrue\n2 1 false\n", TestHarness.RunUnchecked("""
            let calls = 0, initializers = 0;
            const failure = {};
            function initialize() { initializers++; throw failure; }
            const C = class extends class { constructor() { calls++; } } {
                x = initialize();
                constructor() {
                    const run = () => super();
                    try { run(); } catch (error) { console.log(error === failure); }
                    try { run(); } catch (error) { console.log(error instanceof ReferenceError); }
                }
            };
            const value = new C();
            console.log(calls, initializers, Object.hasOwn(value, 'x'));
            """, mode));
    }
}
