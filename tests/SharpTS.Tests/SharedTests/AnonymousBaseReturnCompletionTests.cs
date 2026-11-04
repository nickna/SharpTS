using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AnonymousBaseReturnCompletionTests
{
    [Theory, ModeData]
    public void PrototypeWritesRetainAnonymousSuperclassThroughFinally(ExecutionMode mode)
    {
        Assert.Equal("object\n", TestHarness.RunUnchecked("""
            function Other() {}
            Other.prototype.toString = function() {};
            class C extends class {} {
                constructor() {
                    try { throw null; }
                    catch (error) { return; }
                    finally { super(); }
                }
            }
            console.log(typeof new C());
            """, mode));
    }

    [Theory, ModeData]
    public void LexicalSuperPreservesNewTargetAndRejectsRepeatedBindingAfterEffects(ExecutionMode mode)
    {
        Assert.Equal("true\n2 true true\n", TestHarness.RunUnchecked("""
            let calls = 0;
            let seen;
            const Alt = function Alternate() {};
            const C = class extends class {
                constructor() { calls++; seen = new.target; }
            } {
                constructor() {
                    const invoke = () => super();
                    invoke();
                    try { invoke(); } catch (error) { console.log(error instanceof ReferenceError); }
                }
            };
            const value = Reflect.construct(C, [], Alt);
            console.log(calls, seen === Alt, Object.getPrototypeOf(value) === Alt.prototype);
            """, mode));
    }
}
