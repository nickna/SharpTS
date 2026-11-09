using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArgumentsLengthAssignmentTests
{
    [Theory, ModeData]
    public void AssignmentUsesTheOwnLengthDescriptor(ExecutionMode mode)
    {
        const string source = """
            function inspect() {
                const args = arguments;
                let assigned;
                Object.defineProperty(args, "length", {
                    get() { return assigned; },
                    set(value) { assigned = value; },
                    configurable: true
                });
                args.length = 7;
                console.log(assigned, args.length);
                Object.defineProperty(args, "length", { value: 3, writable: true });
                args.length = 8;
                console.log(args.length);
                Object.defineProperty(args, "length", { writable: false });
                args.length = 9;
                console.log(args.length);
            }
            inspect(1);
            """;

        Assert.Equal("7 7\n8\n8\n", TestHarness.RunUnchecked(source, mode));
    }
}
