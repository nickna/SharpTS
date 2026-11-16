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

    // #3716 owns the remaining synchronous compiler path.
    [Theory, InterpretedOnlyData]
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
}
