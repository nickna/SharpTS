using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed partial class WithStatementTests
{
    public const string AmbiguousMethodGuardSource = """
        const marker = {};
        function* concat(receiver, scope) {
            with (scope) {
                try { return (yield 0, receiver.concat(value)); }
                catch (error) { return error === marker; }
            }
        }
        function* includes(receiver, scope) {
            with (scope) {
                try { return (yield 0, receiver.includes(value)); }
                catch (error) { return error === marker; }
            }
        }
        function* indexOf(receiver, scope) {
            with (scope) {
                try { return (yield 0, receiver.indexOf(value)); }
                catch (error) { return error === marker; }
            }
        }
        for (const kind of ['concat', 'includes', 'indexOf']) {
            const log = [];
            const receiver = {};
            Object.defineProperty(receiver, kind, { configurable: true, get() {
                log.push('method');
                return function (value) { log.push(this === receiver); return value; };
            } });
            const scope = { get value() {
                log.push('argument');
                Object.defineProperty(receiver, kind, { value: function () { return 'wrong'; } });
                return 7;
            } };
            const factory = kind === 'concat' ? concat : kind === 'includes' ? includes : indexOf;
            const generator = factory(receiver, scope);
            console.log(kind, generator.next().value, generator.next().value, log.join(','));
            const throwing = factory('abc', { get value() { throw marker; } });
            console.log(throwing.next().value, throwing.next().value);
        }
        const a = concat('abc', { value: 'x' });
        const b = includes('abc', { value: 'b' });
        const c = indexOf('abc', { value: 'b' });
        console.log(a.next().value, a.next().value, b.next().value, b.next().value, c.next().value, c.next().value);
        function* suspended(receiver) {
            with ({}) {
                try { return receiver.concat(yield 'argument'); }
                catch (error) { return error === marker; }
            }
        }
        const throwingMethod = suspended({ concat(value) { throw marker; } });
        console.log(throwingMethod.next().value, throwingMethod.next(7).value);
        """;

    public const string AmbiguousMethodGuardExpected = "concat 0 7 method,argument,true\n0 true\n"
        + "includes 0 7 method,argument,true\n0 true\nindexOf 0 7 method,argument,true\n0 true\n"
        + "0 abcx 0 true 0 1\nargument true\n";

    [Theory, ModeData]
    public void With_AmbiguousMethodArgumentsEnterGuardsWithEmptyStack(ExecutionMode mode) =>
        Assert.Equal(AmbiguousMethodGuardExpected, TestHarness.RunUnchecked(AmbiguousMethodGuardSource, mode));
}
