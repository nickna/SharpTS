using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassInstanceEnumerabilityTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void ForInSkipsHiddenInstanceFieldsBeforeAndAfterSuspension(ExecutionMode mode)
    {
        const string source = """
            class Box { visible = 1; }
            const box = new Box();
            Object.defineProperty(box, 'hidden', { value: 2 });
            const keys = [];
            for (const key in box) keys.push(key);
            console.log(keys.join(','));
            async function run() {
              const asyncKeys = [];
              for (const key in box) {
                await Promise.resolve();
                asyncKeys.push(key);
              }
              console.log(asyncKeys.join(','));
            }
            run();
            """;
        Assert.Equal("visible\nvisible\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void JsonReviverVisitsOnlyEnumerableFieldsOfAReplacementInstance(ExecutionMode mode)
    {
        const string source = """
            class Box { visible = 1; }
            const box = new Box();
            Object.defineProperty(box, 'hidden', { value: 2 });
            const keys = [];
            JSON.parse('{"first":0,"last":0}', function (key, value) {
              if (key === 'first') this.last = box;
              if (key) keys.push(key);
              return value;
            });
            console.log(keys.join(','));
            """;
        Assert.Equal("first,visible,last\n", TestHarness.RunUnchecked(source, mode));
    }

    [Fact]
    public void CompiledJsonReviverUpdatesReplacementInstanceOwnProperties()
    {
        const string source = """
            class Box { removed = 1; later = 2; }
            const box = new Box();
            const trace = [];
            Object.defineProperty(box, 'locked', { value: 3, enumerable: true });
            Object.defineProperty(box, 'access', {
                get() { trace.push(this === box ? 'get' : 'wrong getter'); return 4; },
                set() { trace.push('set'); },
                enumerable: true, configurable: true
            });
            JSON.parse('{"first":0,"last":0}', function (key, value) {
                if (key === 'first') this.last = box;
                if (this === box) {
                    trace.push(key + ':' + value);
                    if (key === 'removed') {
                        this.later = 8;
                        this.added = 9;
                        return undefined;
                    }
                    if (key === 'locked') return 99;
                    if (key === 'access') return null;
                }
                return value;
            });
            console.log(trace.join(','));
            console.log(Object.hasOwn(box, 'removed'), box.later, box.locked, box.access, box.added);
            """;
        Assert.Equal("removed:1,later:8,locked:3,get,access:4\nfalse 8 3 null 9\n",
            TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void JsonStringifyIncludesHiddenFieldsOnlyWhenTheReplacerSelectsThem(ExecutionMode mode)
    {
        const string source = """
            class Box { visible = 1; }
            const box = new Box();
            let hiddenReads = 0;
            Object.defineProperty(box, 'hidden', { get() { hiddenReads++; return this === box ? 2 : 99; } });
            Object.defineProperty(box, 'computed', { get() { return this === box ? 3 : 99; }, enumerable: true });
            console.log(JSON.stringify(box), hiddenReads);
            console.log(JSON.stringify(box, ['hidden', 'computed', 'visible']), hiddenReads);
            """;
        Assert.Equal("{\"visible\":1,\"computed\":3} 0\n{\"hidden\":2,\"computed\":3,\"visible\":1} 1\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
