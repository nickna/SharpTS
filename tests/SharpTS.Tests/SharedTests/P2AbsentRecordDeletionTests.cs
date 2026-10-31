using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2AbsentRecordDeletionTests
{
    [Theory, ModeData]
    public void SealedAndFrozenRecordsAllowDeletingAbsentOwnNames(ExecutionMode mode)
    {
        const string source = """
            for (const kind of ["seal", "freeze"]) {
                const target: any = Object.create({ inherited: 3 });
                target.own = 1;
                if (kind === "freeze") Object.freeze(target); else Object.seal(target);
                console.log(kind, delete target.missing, delete target.inherited, target.inherited, delete target.own);
                function strictDelete(value: any) { "use strict"; return delete value.missing; }
                console.log("strict", strictDelete(target));
                function strictOwn(value: any) { "use strict"; return delete value.own; }
                try { strictOwn(target); }
                catch (error: any) { console.log(error instanceof TypeError); }
            }
            """;
        Assert.Equal("seal true true 3 false\nstrict true\ntrue\nfreeze true true 3 false\nstrict true\ntrue\n", TestHarness.Run(source, mode));
    }
}
