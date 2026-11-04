using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class LexicalSuperSpecialElementTests
{
    [Theory, ModeData]
    public void ImplicitBaseConstructorStillInitializesDerivedElements(ExecutionMode mode)
    {
        Assert.Equal("1\n", TestHarness.RunUnchecked("""
            const C = class extends class {} {
                value = 1;
                constructor() { const initialize = () => super(); initialize(); }
            };
            console.log(new C().value);
            """, mode));
    }

    [Theory, ModeData]
    public void ReplacementReceiverGetsPrivateBrandAndSourceElementOrder(ExecutionMode mode)
    {
        Assert.Equal("true 1 2 parent,public,private,body\n", TestHarness.RunUnchecked("""
            const events = [];
            const C = class extends class {
                constructor() { events.push('parent'); return {}; }
            } {
                first = (events.push('public'), 2);
                #value = (events.push('private'), 1);
                static has(value) { return #value in value; }
                read() { return this.#value; }
                constructor() {
                    const initialize = () => super();
                    initialize();
                    events.push('body');
                }
            };
            const value = new C();
            console.log(C.has(value), C.prototype.read.call(value), value.first, events.join(','));
            """, mode));
    }

    [Theory, ModeData]
    public void PrivateMethodOnlyClassInitializesItsBrand(ExecutionMode mode)
    {
        Assert.Equal("true 7\n", TestHarness.RunUnchecked("""
            const C = class extends class { constructor() {} } {
                #read() { return 7; }
                static has(value) { return #read in value; }
                read() { return this.#read(); }
                constructor() { const initialize = () => super(); initialize(); }
            };
            const value = new C();
            console.log(C.has(value), value.read());
            """, mode));
    }

    [Theory, ModeData]
    public void AutoAccessorOnlyClassInitializesItsReceiverStorage(ExecutionMode mode)
    {
        Assert.Equal("1\n3\n", TestHarness.RunUnchecked("""
            const C = class extends class { constructor() {} } {
                accessor value = 1;
                constructor() { const initialize = () => super(); initialize(); }
            };
            const value = new C();
            console.log(value.value);
            value.value = 3;
            console.log(value.value);
            """, mode));
    }

    [Theory, ModeData]
    public void ComputedKeysAreCapturedOnceForBothInstances(ExecutionMode mode)
    {
        Assert.Equal("1 1 1 2 2\n", TestHarness.RunUnchecked("""
            let evaluations = 0;
            const symbol = Symbol('field');
            const C = class extends class { constructor() {} } {
                [(evaluations++, 'value')] = 1;
                [symbol] = 2;
                constructor() { const initialize = () => super(); initialize(); }
            };
            const first = new C(), second = new C();
            console.log(evaluations, first.value, second.value, first[symbol], second[symbol]);
            """, mode));
    }

    [Theory, ModeData]
    public void ThrowingPrivateInitializerPublishesOnlyCompletedBrands(ExecutionMode mode)
    {
        Assert.Equal("true\ntrue\ntrue,false,false 2 1\n", TestHarness.RunUnchecked("""
            let calls = 0, initializations = 0;
            const failure = {};
            const C = class extends class { constructor() { calls++; } } {
                #before = 1;
                #failed = (() => { initializations++; throw failure; })();
                #later = 2;
                static state(value) { return [#before in value, #failed in value, #later in value].join(','); }
                constructor() {
                    const initialize = () => super();
                    try { initialize(); } catch (error) { console.log(error === failure); }
                    try { initialize(); } catch (error) { console.log(error instanceof ReferenceError); }
                }
            };
            const value = new C();
            console.log(C.state(value), calls, initializations);
            """, mode));
    }

    [Theory, ModeData]
    public void PrivateBrandsStayWithEachEvaluatedClassDefinition(ExecutionMode mode)
    {
        Assert.Equal("true false false true\n", TestHarness.RunUnchecked("""
            function create() {
                return class extends class { constructor() {} } {
                    #value = 1;
                    static has(value) { return #value in value; }
                    constructor() { const initialize = () => super(); initialize(); }
                };
            }
            const A = create(), B = create();
            const first = new A(), second = new B();
            console.log(A.has(first), A.has(second), B.has(first), B.has(second));
            """, mode));
    }
}
