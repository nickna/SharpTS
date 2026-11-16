using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EvalTemplateIdentityTests
{
    [Theory, ModeData]
    public void EachEvalExecutionOwnsFreshSitesWhileOrdinarySitesStayStable(ExecutionMode mode)
    {
        const string source = """
            function site() { return eval('((strings) => strings)`value`'); }
            function ordinary() { return ((strings) => strings)`value`; }
            console.log(site() === site(), ordinary() === ordinary());
            function strictSite() { return eval('"use strict"; ((strings) => strings)`value`'); }
            console.log(strictSite() === strictSite());
            """;
        Assert.Equal("false true\nfalse\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void EscapingClosuresKeepTheirOriginalCacheAcrossLoopEvaluations(ExecutionMode mode)
    {
        const string source = """
            const functions = [];
            for (let i = 0; i < 2; i++) functions.push(eval('() => ((strings) => strings)`value`'));
            const first = functions[0]();
            const second = functions[1]();
            console.log(first === functions[0](), second === functions[1](), first === second);
            console.log(first.raw === functions[0]().raw, first.raw === second.raw);
            console.log(Object.isFrozen(first), Object.isFrozen(first.raw), first[0], first.raw[0]);
            """;
        Assert.Equal("true true false\ntrue false\ntrue true value value\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void NestedFunctionsForwardTheEvalActivationCache(ExecutionMode mode)
    {
        const string source = """
            function make() { return eval('(function () { return function () { return ((s) => s)`x`; }; })'); }
            const a = make(); const b = make();
            console.log(a()() === a()(), a()() === b()());
            function declared() { return eval('function outer() { function inner() { return ((s) => s)`x`; } return inner; } outer;'); }
            const c = declared(); const d = declared();
            console.log(c()() === c()(), c()() === d()());
            """;
        Assert.Equal("true false\ntrue false\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ReentrantEvalDoesNotReplaceOlderTemplateObjects(ExecutionMode mode)
    {
        const string source = """
            let nested;
            let reenter = true;
            function make() { return eval('() => tag`value`'); }
            function tag(strings) {
                if (reenter) { reenter = false; nested = make(); nested(); }
                return strings;
            }
            const first = make();
            const before = first();
            console.log(before === first(), before === nested());
            """;
        Assert.Equal("true false\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void RepeatedVisitsInsideOneEvalShareOnlyTheirOwnSite(ExecutionMode mode)
    {
        const string source = """
            function make() { return eval('var results = []; for (var i = 0; i < 2; i++) { results.push(((s) => s)`x`); } results;'); }
            const a = make(); const b = make();
            console.log(a[0] === a[1], b[0] === b[1], a[0] === b[0]);
            const sites = eval('[((s) => s)`x`, ((s) => s)`x`]');
            console.log(sites[0] === sites[1]);
            """;
        Assert.Equal("true true false\nfalse\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AsyncClosuresRetainTheCacheAcrossSuspension(ExecutionMode mode)
    {
        const string source = """
            function make() { return eval('async () => { await Promise.resolve(); return ((s) => s)`x`; }'); }
            async function run() {
                const a = make(); const b = make();
                const first = await a();
                console.log(first === await a(), first === await b());
            }
            run();
            """;
        Assert.Equal("true false\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorAndClassClosuresRetainTheirEvalCache(ExecutionMode mode)
    {
        const string source = """
            function generator() { return eval('(function* () { yield ((s) => s)`x`; })'); }
            const a = generator(); const b = generator();
            console.log(a().next().value === a().next().value, a().next().value === b().next().value);
            function klass() { return eval('(class { get() { return ((s) => s)`x`; } })'); }
            const ClassA = klass(); const ClassB = klass();
            console.log(new ClassA().get() === new ClassA().get(), new ClassA().get() === new ClassB().get());
            """;
        Assert.Equal("true false\ntrue false\n", TestHarness.RunUnchecked(source, mode));
    }
}
