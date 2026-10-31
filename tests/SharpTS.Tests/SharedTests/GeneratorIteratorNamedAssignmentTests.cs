using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class GeneratorIteratorNamedAssignmentTests
{
    [Theory, ModeData]
    public void IteratorNamedAssignmentsCreateAndUpdateOwnProperties(ExecutionMode mode)
    {
        const string source = """
            function* values() {}
            async function* asyncValues() {}
            const factories: any[] = [values, asyncValues];
            for (const factory of factories) {
                const iterator: any = factory();
                iterator.count = 1;
                console.log("before", iterator.count, Object.hasOwn(iterator, "count"));
                iterator.count += 2;
                console.log(iterator.count++, ++iterator.count, iterator.count);
                iterator.name = "own";
                console.log(iterator.name, Object.hasOwn(iterator, "name"));
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("before 1 true\n3 5 5\nown true\n", 2)), TestHarness.Run(source, mode));
    }
}
