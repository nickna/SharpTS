using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrivateMemberAwaitSpillTests
{
    // Preserve AwaitStackSpillTests.PrivateMembers_InlineAwait unchanged; these are independent controls.
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[]
        {
            "field-set-fulfilled",
            CanonicalSource("""
            class W {
                #p: any = 1;
                async go() {
                    this.#p = await Promise.resolve(5);
                    return this.#p;
                }
            }
            new W().go().then(
                value => { console.log('value', value); console.log('fulfilled'); },
                (error: any) => console.log('rejected', error.name, error.message));
            """),
            "value 5\nfulfilled\n"
        };
        yield return new object[]
        {
            "field-set-pending-receiver-once",
            CanonicalSource("""
            const trace: string[] = [];
            let release: (value: any) => void = (_value: any) => {};
            const gate = new Promise<any>(resolve => { release = resolve; });
            function rhs(): Promise<any> { trace.push('rhs'); return gate; }
            class W {
                #p: any = 1;
                receiver() { trace.push('receiver'); return this; }
                async go() {
                    this.receiver().#p = await rhs();
                    return this.#p;
                }
            }
            const observed = new W().go();
            observed.then(
                value => { console.log('value', value, trace.join(',')); console.log('fulfilled'); },
                (error: any) => console.log('rejected', error.name, error.message));
            console.log('waiting', trace.join(','));
            release(5);
            """),
            "waiting receiver,rhs\nvalue 5 receiver,rhs\nfulfilled\n"
        };
        yield return new object[]
        {
            "private-call-fulfilled",
            CanonicalSource("""
            class W {
                #p: any = 1;
                #m(first: any, second: any) { return first + second + this.#p; }
                async go() { return this.#m(2, await Promise.resolve(3)); }
            }
            new W().go().then(
                value => { console.log('value', value); console.log('fulfilled'); },
                (error: any) => console.log('rejected', error.name, error.message));
            """),
            "value 6\nfulfilled\n"
        };
        yield return new object[]
        {
            "private-call-pending",
            CanonicalSource("""
            let release: (value: any) => void = (_value: any) => {};
            const gate = new Promise<any>(resolve => { release = resolve; });
            class W {
                #p: any = 1;
                #m(first: any, second: any) { return first + second + this.#p; }
                async go() { return this.#m(2, await gate); }
            }
            const observed = new W().go();
            observed.then(
                value => { console.log('value', value); console.log('fulfilled'); },
                (error: any) => console.log('rejected', error.name, error.message));
            console.log('waiting');
            release(3);
            """),
            "waiting\nvalue 6\nfulfilled\n"
        };
        yield return new object[]
        {
            "pending-call-receiver-argument-order",
            CanonicalSource("""
            const trace: string[] = [];
            let release: (value: any) => void = (_value: any) => {};
            const gate = new Promise<any>(resolve => { release = resolve; });
            function argument(label: string, value: any) { trace.push(label); return value; }
            class W {
                #p: any = 1;
                receiver() { trace.push('receiver'); return this; }
                #m(first: any, second: any, last: any) {
                    trace.push('body');
                    return first + second + last + this.#p;
                }
                async go() {
                    return this.receiver().#m(argument('first', 2), await gate, argument('last', 4));
                }
            }
            const observed = new W().go();
            observed.then(
                value => { console.log('value', value, trace.join(',')); console.log('fulfilled'); },
                (error: any) => console.log('rejected', error.name, error.message));
            console.log('waiting', trace.join(','));
            release(3);
            """),
            "waiting receiver,first\nvalue 10 receiver,first,last,body\nfulfilled\n"
        };
        yield return new object[]
        {
            "rejected-operands-no-write-or-call",
            CanonicalSource("""
            class W {
                #p: any = 1;
                #m(value: any) { console.log('unexpected-call'); return value; }
                async write() {
                    try { this.#p = await Promise.reject('stop'); }
                    catch (error) { console.log('write', error === 'stop', this.#p); }
                }
                async invoke() {
                    try { this.#m(await Promise.reject('stop')); }
                    catch (error) { console.log('call', error === 'stop', this.#p); }
                }
            }
            async function run() {
                const receiver = new W();
                await receiver.write();
                await receiver.invoke();
            }
            run().then(
                () => console.log('fulfilled'),
                (error: any) => console.log('rejected', error.name, error.message));
            """),
            "write true 1\ncall true 1\nfulfilled\n"
        };
    }

    private static string CanonicalSource(string source) =>
        source.Replace("\r\n", "\n") + "\n";

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void PrivateOperandsSurviveSuspensionAndPreserveOrder(
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
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
