using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalIdentifierAssignmentTests
{
    // Compiled compound/logical global stores are tracked separately in #3721.
    [Theory, InterpretedOnlyData]
    public void SloppyWritesCreateGlobalsAndPreserveLexicalBindings(ExecutionMode mode)
    {
        const string source = """
            function run() { implicit = 3; implicit += 2; implicit ||= 9; console.log(implicit); }
            run();
            console.log(globalThis.implicit);
            { let implicit = 20; implicit = 21; console.log(implicit); }
            console.log(implicit);
            function evalBinding() { created = (eval('var created = 2;'), 3); console.log(created); }
            evalBinding();
            console.log(globalThis.created);
            """;
        Assert.Equal("5\n5\n21\n5\n2\n3\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void StrictSynchronousWritesUseExistingGlobalProperties(ExecutionMode mode)
    {
        const string source = """
            globalThis.existing = 1;
            function run() {
                'use strict';
                console.log(existing = 7, globalThis.existing);
                try { missing = (console.log('rhs'), 2); } catch(e) { console.log(e instanceof ReferenceError); }
                console.log(Object.hasOwn(globalThis, 'missing'));
            }
            run();
            """;
        Assert.Equal("7 7\nrhs\ntrue\nfalse\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void StrictGlobalWritesHonorDescriptorsAndTheSelectedReference(ExecutionMode mode)
    {
        const string source = """
            Object.defineProperty(globalThis, 'fixed', {value: 1});
            Object.defineProperty(globalThis, 'setter', {
                get(){ throw 'read'; }, set(value){ console.log(this === globalThis, value); }
            });
            function run() {
                'use strict';
                console.log(setter = 7);
                try { fixed = 2; } catch(e) { console.log(e instanceof TypeError); }
                try { created = (globalThis.created = 1, 2); }
                catch(e) { console.log(e instanceof ReferenceError, globalThis.created); }
                globalThis.deleted = 3;
                try { deleted = (delete globalThis.deleted, 4); }
                catch(e) { console.log(e instanceof ReferenceError, Object.hasOwn(globalThis, 'deleted')); }
            }
            run();
            """;
        Assert.Equal("true 7\n7\ntrue\ntrue 1\ntrue false\n", TestHarness.RunUnchecked(source, mode));
    }
}
