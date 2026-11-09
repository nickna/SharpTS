using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArgumentsLengthDeletionTests
{
    [Theory, ModeData]
    public void ArgumentsLengthCanBeDeletedAndRecreated(ExecutionMode mode)
    {
        const string source = """
            function inspect() {
                "use strict";
                const args = arguments;
                console.log(delete args["length"], Object.hasOwn(args, "length"), args.length);
                console.log(arguments.length, Object.getOwnPropertyDescriptor(args, "length"));
                args.length = 2;
                const descriptor = Object.getOwnPropertyDescriptor(args, "length");
                console.log(args.length, descriptor.writable, descriptor.enumerable, descriptor.configurable);
                console.log(delete args.length, args.length);
                Object.seal(args);
                console.log(delete args["length"], Object.hasOwn(args, "length"));
                try { args.length = 3; }
                catch (error) { console.log(error instanceof TypeError); }
            }
            inspect(1);
            """;

        Assert.Equal("true false undefined\nundefined undefined\n2 true true true\ntrue undefined\ntrue false\ntrue\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void OwnNamesReflectThePresenceOfLength(ExecutionMode mode)
    {
        const string source = """
            function inspect() {
                "use strict";
                console.log(Object.getOwnPropertyNames(arguments).filter(k => k === "length").length);
                delete arguments["length"];
                console.log(Object.hasOwn(arguments, "length"), Object.getOwnPropertyNames(arguments).includes("length"));
                arguments.length = 2;
                console.log(Object.getOwnPropertyNames(arguments).filter(k => k === "length").length);
            }
            inspect(1);
            """;

        Assert.Equal("1\nfalse false\n1\n", TestHarness.RunUnchecked(source, mode));
    }
}
