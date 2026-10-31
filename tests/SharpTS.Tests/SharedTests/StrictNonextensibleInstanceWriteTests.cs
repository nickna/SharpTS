using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class StrictNonextensibleInstanceWriteTests
{
    [Theory, ModeData]
    public void StrictAdditionsRejectWhileExistingFieldsAndInheritedSettersWork(ExecutionMode mode)
        => Assert.Equal("rejected\nfalse true\n8\nset 9\n", TestHarness.Run("""
            class C { field = 1; set assigned(value: number) { console.log('set', value); } }
            const value: any = new C();
            Object.preventExtensions(value);
            function run() {
                'use strict';
                try { value.slot = 43; console.log('accepted'); } catch (error) { console.log('rejected'); }
                console.log(Object.hasOwn(value, 'slot'), value.slot === undefined);
                value.field = 8; console.log(value.field);
                value.assigned = 9;
            }
            run();
            """, mode));
}
