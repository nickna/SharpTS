using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed partial class WithStatementTests
{
    public const string SuspendedLookupSource = """
        const marker = {};
        const throwing = {
            get slice() { throw marker; }, get concat() { throw marker; },
            get indexOf() { throw marker; }, get includes() { throw marker; }
        };
        function* slice() { try { return (yield 0).slice(1); } catch(e) { return e === marker; } }
        function* concat() { try { return (yield 0).concat(1); } catch(e) { return e === marker; } }
        function* indexOf() { try { return (yield 0).indexOf(1); } catch(e) { return e === marker; } }
        function* includes() { try { return (yield 0).includes(1); } catch(e) { return e === marker; } }
        for (const factory of [slice,concat,indexOf,includes]) {
            const g=factory(); g.next(); console.log(g.next(throwing).value);
        }
        function* nullReceiver() { try { return (yield 0).slice(1); } catch(e) { return e.name; } }
        const nullGenerator=nullReceiver(); nullGenerator.next(); console.log(nullGenerator.next(null).value);
        function* beforeArgument(receiver) {
            try { return receiver.concat(yield 'argument'); } catch(e) { return e === marker; }
        }
        console.log(beforeArgument(throwing).next().value);
        function* retain(receiver) {
            with ({x:3}) { try { return receiver.concat((x += yield 'argument')); } catch(e) { return 'wrong catch'; } }
        }
        const events=[];
        const receiver={get concat() { events.push('get'); return function(value) { return this === receiver ? value : -1; }; }};
        const g=retain(receiver);
        console.log(g.next().value);
        Object.defineProperty(receiver,'concat',{value:function(){return -2;},configurable:true});
        console.log(g.next(4).value, events.join(','));
        async function awaited(receiver) {
            try { return receiver.concat(await Promise.resolve(1)); } catch(e) { return e === marker; }
        }
        awaited(throwing).then(value => console.log(value));
        """;

    public const string SuspendedLookupExpected = "true\ntrue\ntrue\ntrue\nTypeError\ntrue\nargument\n7 get\ntrue\n";

    [Theory, ModeData]
    public void With_SuspendingCallsGuardLookupAndRetainTheSelectedMethod(ExecutionMode mode) =>
        Assert.Equal(SuspendedLookupExpected, TestHarness.RunUnchecked(SuspendedLookupSource, mode));
}
