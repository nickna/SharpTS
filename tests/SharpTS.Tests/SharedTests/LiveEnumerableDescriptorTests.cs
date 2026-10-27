using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class LiveEnumerableDescriptorTests
{
    [Theory, ModeData]
    public void EarlierGettersChangeLaterDescriptorAdmission(ExecutionMode mode)
    {
        const string source = """
            function run(kind: string, owner: any): void {
                Object.defineProperty(owner, 'hidden', { value: 4, enumerable: false, configurable: true });
                Object.defineProperty(owner, 'first', { get() {
                    delete owner.removed;
                    Object.defineProperty(owner, 'later', { enumerable: false });
                    Object.defineProperty(owner, 'hidden', { enumerable: true });
                    owner.added = 5;
                    return 7;
                }, enumerable: true, configurable: true });
                const result: any = kind === 'values' ? Object.values(owner)
                    : kind === 'entries' ? Object.entries(owner) : Object.assign({}, owner);
                console.log(kind, JSON.stringify(result));
            }
            for (const kind of ['values', 'entries', 'assign']) {
                run(kind, { first: 1, removed: 2, later: 3 });
                class Owner { static first = 1; static removed = 2; static later = 3; }
                run(kind, Owner);
            }
            """;
        Assert.Equal("values [7,4]\nvalues [7,4]\nentries [[\"first\",7],[\"hidden\",4]]\nentries [[\"first\",7],[\"hidden\",4]]\nassign {\"first\":7,\"hidden\":4}\nassign {\"first\":7,\"hidden\":4}\n",
            TestHarness.Run(source, mode));
    }
}
