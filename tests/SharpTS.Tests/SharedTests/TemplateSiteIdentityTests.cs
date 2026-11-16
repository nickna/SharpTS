using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TemplateSiteIdentityTests
{
    [Theory, ModeData]
    public void OneSiteRetainsItsObjectAcrossClosuresAndDifferentTags(ExecutionMode mode)
    {
        const string source = """
            function first(strings, value) {
                console.log(value, Object.isFrozen(strings), Object.isFrozen(strings.raw));
                return strings;
            }
            function second(strings, value) { return strings; }
            function factory() { return (tag, value) => tag`same${value}text`; }
            const left = factory();
            const right = factory();
            const original = left(first, 1);
            console.log(original === right(first, 2));
            console.log(original === left(second, 3));
            console.log(original.raw === right(second, 4).raw);
            console.log(original === second`same${5}text`);
            console.log(second`same${6}text` === second`same${6}text`);
            """;
        Assert.Equal("1 true true\n2 true true\ntrue\ntrue\ntrue\nfalse\nfalse\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void SuspendedAndWithBoundTagsRetainTheirSiteAndReceiver(ExecutionMode mode)
    {
        const string source = """
            const receiver = { tag(strings, value) { console.log(this === receiver, value); return strings; } };
            function* generate() { return receiver.tag`value${yield 0}`; }
            const first = generate(); first.next();
            const firstTemplate = first.next(1).value;
            const second = generate(); second.next();
            console.log(firstTemplate === second.next(2).value);
            async function run() { return receiver.tag`value${await 3}`; }
            async function main() { console.log(await run() === await run()); }
            const pending = main();
            function bound() { with (receiver) { return tag`bound`; } }
            console.log(bound() === bound());
            """;
        Assert.Equal("true 1\ntrue 2\ntrue\ntrue undefined\ntrue undefined\ntrue\ntrue 3\ntrue 3\ntrue\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void InvalidCookedSegmentAndRawIdentityArePreserved(ExecutionMode mode)
    {
        const string source = """
            function tag(strings) { return strings; }
            function run() { return tag`\unicode`; }
            const first = run();
            const second = run();
            console.log(first === second, first.raw === second.raw);
            console.log(first[0] === undefined, first.raw[0] === '\\unicode');
            """;
        Assert.Equal("true true\ntrue true\n", TestHarness.RunUnchecked(source, mode));
    }
}
