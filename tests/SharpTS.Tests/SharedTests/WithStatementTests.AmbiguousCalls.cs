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

    public const string ParentOperandSource = """
        function* arrayValue(receiver, x) { try { return (yield 0, [1, receiver.concat(x)]); } catch(e) { return 'caught'; } }
        function* binaryValue(receiver, x) { try { return (yield 0, 1 + receiver.indexOf(x)); } catch(e) { return 'caught'; } }
        function* objectValue(receiver, x) { try { return (yield 0, { value: receiver.concat(x) }); } catch(e) { return 'caught'; } }
        function* parentArgument(receiver, x) { try { return (yield 0, receiver.startsWith(receiver.concat(x))); } catch(e) { return 'caught'; } }
        function* literalArgument(receiver, x) { try { return (yield 0, 'abc'.startsWith(receiver.concat(x))); } catch(e) { return 'caught'; } }
        function* mathArgument(receiver, x) { try { return (yield 0, Math.max(1,receiver.indexOf(x))); } catch(e) { return 'caught'; } }
        function* mathPower(receiver, x) { try { return (yield 0, Math.pow(2,receiver.indexOf(x))); } catch(e) { return 'caught'; } }
        function identity(value) { return value; }
        function* directArgument(receiver, x) { try { return (yield 0, identity(receiver.concat(x))); } catch(e) { return 'caught'; } }
        function* indexRead(receiver, x) { try { return (yield 0, [7,8][receiver.indexOf(x)]); } catch(e) { return 'caught'; } }
        function* indexWrite(receiver, x) { const a=[7,8]; try { return (yield 0, a[receiver.indexOf(x)] = 9); } catch(e) { return 'caught'; } }
        function* withArray(receiver) { with ({x:'b'}) { try { return (yield 0, [1, receiver.concat(x)]); } catch(e) { return 'caught'; } } }
        function* withBinary(receiver) { with ({x:'b'}) { try { return (yield 0, 1 + receiver.indexOf(x)); } catch(e) { return 'caught'; } } }
        function* withObject(receiver) { with ({x:'b'}) { try { return (yield 0, { value: receiver.concat(x) }); } catch(e) { return 'caught'; } } }
        function* withArgument(receiver) { with ({x:'b'}) { try { return (yield 0, receiver.startsWith(receiver.concat(x))); } catch(e) { return 'caught'; } } }
        for (const factory of [arrayValue,binaryValue,objectValue,parentArgument,literalArgument,mathArgument,mathPower,directArgument,indexRead,indexWrite,withArray,withBinary,withObject,withArgument]) {
            const generator = factory('abc','b');
            generator.next();
            console.log(JSON.stringify(generator.next().value));
        }
        """;

    public const string ParentOperandExpected = "[1,\"abcb\"]\n2\n{\"value\":\"abcb\"}\nfalse\nfalse\n"
        + "1\n2\n\"abcb\"\n8\n9\n"
        + "[1,\"abcb\"]\n2\n{\"value\":\"abcb\"}\nfalse\n";

    [Theory, ModeData]
    public void With_GuardedCallsLeaveParentOperandsOffTheStack(ExecutionMode mode) =>
        Assert.Equal(ParentOperandExpected, TestHarness.RunUnchecked(ParentOperandSource, mode));
}
