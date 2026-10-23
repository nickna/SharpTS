using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GeneratorDefaultGlobalCaptureTests
{
    public const string IssueSource = """
        const value = "outside";
        function* make(read: any = () => value) {
            var value = "body";
            yield [read, () => value];
        }
        try {
            const pair: any = make().next().value;
            console.log(pair[0](), pair[1]());
        } catch (error: any) {
            console.log("caught", error.name, error.message);
        }
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-eager-generator-default-global-capture", IssueSource, "outside body\n"];
        yield return ["expression-and-public-methods-retain-live-global-reference", """
            let value = "outside";
            {
                const tag = "retained";
                const make = function*(read: any = () => value) {
                    var value = "expression";
                    yield [read, () => value, tag];
                };
                const iterator = make();
                value = "changed";
                const pair: any = iterator.next().value;
                console.log(pair[0](), pair[1](), pair[2]);
                value = "later";
                console.log(pair[0](), pair[1](), pair[2]);
            }
            class Owner {
                *make(read: any = () => value) { var value = "instance"; yield [read, () => value]; }
                static *make(read: any = () => value) { var value = "static"; yield [read, () => value]; }
            }
            const instancePair: any = new Owner().make().next().value;
            const staticPair: any = Owner.make().next().value;
            console.log("instance", instancePair[0](), instancePair[1]());
            console.log("static", staticPair[0](), staticPair[1]());
            """, "changed expression retained\nlater expression retained\ninstance later instance\nstatic later static\n"];
        yield return ["async-generator-declaration-and-expression-global-captures", """
            const value = "outside";
            async function* declaration(read: any = () => value) {
                var value = "declaration";
                await Promise.resolve();
                yield [read, () => value];
            }
            {
                const tag = "retained";
                const expression = async function*(read: any = () => value) {
                    var value = "expression";
                    await Promise.resolve();
                    yield [read, () => value, tag];
                };
                declaration().next().then(first => {
                    const pair: any = first.value;
                    console.log("declaration", pair[0](), pair[1]());
                    return expression().next();
                }).then(second => {
                    const pair: any = second.value;
                    console.log("expression", pair[0](), pair[1](), pair[2]);
                }).then(undefined, (error: any) => console.log("rejected", error.name, error.message));
            }
            """, "declaration outside declaration\nexpression outside expression retained\n"];
        yield return ["eager-default-effects-and-supplied-callback", """
            const value = "outside";
            const trace: string[] = [];
            function* make(read: any = (trace.push("default"), () => value)) {
                var value = "body";
                trace.push("body");
                yield [read, () => value];
            }
            const iterator = make();
            console.log("created", trace.join(","));
            const pair: any = iterator.next().value;
            console.log(pair[0](), pair[1](), trace.join(","));
            trace.length = 0;
            const supplied = make(() => "supplied");
            console.log("supplied-created", trace.join(",") || "none");
            const suppliedPair: any = supplied.next().value;
            console.log(suppliedPair[0](), suppliedPair[1](), trace.join(","));
            trace.length = 0;
            const asynchronous = async function*(read: any = (trace.push("async-default"), () => value)) {
                var value = "async-body";
                trace.push("async-body");
                yield [read, () => value];
            };
            const asyncIterator = asynchronous();
            console.log("async-created", trace.join(","));
            asyncIterator.next().then(result => {
                const pair: any = result.value;
                console.log(pair[0](), pair[1](), trace.join(","));
            }).then(undefined, (error: any) => console.log("rejected", error.name, error.message));
            """, "created default\noutside body default,body\nsupplied-created none\nsupplied body body\nasync-created async-default\noutside async-body async-default,async-body\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void EagerGeneratorDefaultsRetainGlobalEnvironmentReferences(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
