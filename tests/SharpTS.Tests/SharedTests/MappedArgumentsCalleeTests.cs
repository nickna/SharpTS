using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class MappedArgumentsCalleeTests
{
    [Theory, ModeData]
    public void MappedArgumentsExposeAMutableCalleeDescriptor(ExecutionMode mode)
    {
        const string source = """
            function mapped(value) {
                const args = arguments;
                const descriptor = Object.getOwnPropertyDescriptor(args, "callee");
                console.log(args.callee === mapped, descriptor.value === mapped,
                    descriptor.writable, descriptor.enumerable, descriptor.configurable);
                args.callee = "updated";
                console.log(args["callee"], Object.keys(args).join(","));
                console.log(delete args["callee"], Object.hasOwn(args, "callee"));
            }
            mapped(1);
            """;
        Assert.Equal("true true true false true\nupdated 0\ntrue false\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ReceiverViewsUseTheOriginalCallableAsCallee(ExecutionMode mode)
    {
        const string source = """
            function declaration(value) { return arguments.callee; }
            const expression = function (value) { return arguments.callee; };
            const noParameters = function () { return arguments.callee; };
            const receiver = { method: declaration };
            const bound = expression.bind(receiver);
            console.log(receiver.method(1) === declaration,
                declaration.call(receiver, 2) === declaration,
                bound(3) === expression,
                bound(4) === bound, noParameters.call(receiver) === noParameters);
            """;
        Assert.Equal("true true true false true\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ConstructorsAndSuspendedFunctionsKeepTheirCallee(ExecutionMode mode)
    {
        const string source = """
            function Constructor() { return { callee: arguments.callee, target: new.target }; }
            const instance = new Constructor();
            console.log(instance.callee === Constructor, instance.target === Constructor);

            async function asyncFunction() { return arguments.callee; }
            function* generator() { yield arguments.callee; }
            async function* asyncGenerator() { yield arguments.callee; }
            async function inspect() {
                console.log(await asyncFunction() === asyncFunction,
                    generator().next().value === generator,
                    (await asyncGenerator().next()).value === asyncGenerator);
            }
            inspect();
            """;

        Assert.Equal("true true\ntrue true true\n", TestHarness.RunUnchecked(source, mode));
    }
}
