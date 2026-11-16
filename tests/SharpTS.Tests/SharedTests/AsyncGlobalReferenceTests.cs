using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncGlobalReferenceTests
{
    [Theory, ModeData]
    public void StrictAssignmentKeepsReferenceSelectedBeforeAwaitedRightHandSide(ExecutionMode mode)
    {
        // Assignment evaluates leftRef before the RHS; PutValue must retain its
        // unresolvable status. Node 22 accepts this write, contrary to these steps:
        // https://tc39.es/ecma262/#sec-assignment-operators-runtime-semantics-evaluation
        // https://tc39.es/ecma262/#sec-putvalue
        const string source = """
            const run = async () => {
                'use strict';
                try { reviewCreated = (globalThis.reviewCreated = 1, await Promise.resolve(2)); }
                catch (error) { console.log(error instanceof ReferenceError, globalThis.reviewCreated); }
            };
            run();
            """;
        Assert.Equal("true 1\n", TestHarness.RunUnchecked(source, mode));
    }
}
