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

    [Fact]
    public void JsonReviverVisitsOnlyEnumerableFieldsOfAReplacementInstance()
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
        Assert.Equal("first,visible,last\n", TestHarness.RunUnchecked(source, ExecutionMode.Interpreted));
    }
}
