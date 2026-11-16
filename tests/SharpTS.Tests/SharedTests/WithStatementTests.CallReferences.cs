using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed partial class WithStatementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void With_NestedCallsAndTagsHaveLinearLoweredSize(bool tagged)
    {
        string expression = "0";
        for (int i = 0; i < 12; i++)
            expression = tagged ? "f`${" + expression + "}`" : "f(" + expression + ")";
        var statements = TestHarness.ParseOrThrow("function f(x) { return x; } with ({}) { " + expression + "; }");
        var counter = new NodeCounter();
        foreach (var statement in statements) counter.Visit(statement);
        Assert.InRange(counter.Count, 1, 400);
    }

    private sealed class NodeCounter : AstVisitorBase
    {
        public int Count { get; private set; }
        public override void Visit(Expr expression) { Count++; base.Visit(expression); }
        public override void Visit(Stmt statement) { Count++; base.Visit(statement); }
    }

    [Theory]
    [InlineData("'use strict'\n+ ''; with ({}) {}")]
    [InlineData("'use strict'\n[0]; with ({}) {}")]
    [InlineData("'use strict'\n.length; with ({}) {}")]
    [InlineData("'use strict'\n('argument'); with ({}) {}")]
    [InlineData("'use strict'\n`template`; with ({}) {}")]
    public void With_StringExpressionContinuationsAreNotStrictDirectives(string source) =>
        Assert.NotEmpty(TestHarness.ParseOrThrow(source));

    [Theory, ModeData]
    public void With_CallCapturePreservesLookupOrderReceiverAndOptionalArguments(ExecutionMode mode)
    {
        const string source = """
            const log = [];
            const selected = function (value) { log.push(this === object); return value; };
            Object.defineProperty(selected, 'name', { get() { throw 'name'; } });
            Object.defineProperty(selected, 'length', { get() { throw 'length'; } });
            const object = { get f() { log.push('get'); return selected; }, absent: null };
            function argument() { log.push('arg'); delete object.f; return 7; }
            with (object) { console.log(f(argument())); absent?.(log.push('bad')); }
            let f = function (value) { return 'original ' + value; };
            with ({}) console.log(f((f = function () { return 'replacement'; }, 8)));
            console.log(log.join(','));
            """;
        Assert.Equal("7\noriginal 8\nget,arg,true\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_NestedArgumentsAndTagsExecuteOnce(ExecutionMode mode)
    {
        string nested = "0";
        for (int i = 0; i < 12; i++) nested = "f(" + nested + ")";
        const string prefix = """
            let count = 0;
            function f(value) { count++; return value + 1; }
            with ({}) console.log(
            """;
        const string suffix = """
            );
            console.log(count);
            const object = { tag: function (strings, value) {
                console.log(this === object, strings.raw[0]); return value + 1;
            } };
            with (object) console.log(tag`outer${tag`inner${1}`}`);
            """;
        Assert.Equal("12\n12\ntrue inner\ntrue outer\n3\n", TestHarness.RunUnchecked(prefix + nested + suffix, mode));
    }

    public const string DirectEvalFallbackSource = """
        function run() {
            const local = 7;
            let extras = 0;
            with ({}) console.log(eval('local', ++extras));
            console.log(extras);
        }
        run();
        """;

    [Theory, ModeData]
    public void With_DirectEvalFallbackRetainsCallerBindings(ExecutionMode mode) =>
        Assert.Equal("7\n1\n", TestHarness.RunUnchecked(DirectEvalFallbackSource, mode));

    public const string CapturedCallSuspensionSource = """
        const object = { f: function (value) { return this === object ? value : -1; }, x: 1 };
        function* run(receiver) {
            with (object) {
                try { const sliced = (yield 0, receiver.slice(x)); yield sliced; return f(yield 5); }
                catch (error) { return 'bad'; }
            }
        }
        const generator = run('abc');
        console.log(generator.next().value, generator.next().value, generator.next().value);
        delete object.f;
        console.log(generator.next(7).value);
        """;

    [Theory, ModeData]
    public void With_CallReferenceAndSliceArgumentsSurviveGeneratorTrySuspension(ExecutionMode mode) =>
        Assert.Equal("0 bc 5\n7\n", TestHarness.RunUnchecked(CapturedCallSuspensionSource, mode));
}
