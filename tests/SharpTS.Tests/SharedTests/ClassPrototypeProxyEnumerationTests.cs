using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassPrototypeProxyEnumerationTests
{
    public const string IssueSource = """
        class C {}
        const prototype: any = C.prototype;
        prototype.first = 1; prototype.last = 2;
        const proxy: any = new Proxy(prototype, {});
        console.log(Object.values(prototype).join(","), Object.entries(prototype).map(entry => entry.join(":")).join(","));
        console.log(Object.keys(proxy).join(","));
        console.log(Object.values(proxy).join(","), Object.entries(proxy).map(entry => entry.join(":")).join(","));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["neutral-class-prototype-issue", IssueSource,
            "1,2 first:1,last:2\nfirst,last\n1,2 first:1,last:2\n"];
        yield return ["ordinary-record-proxy-positive", """
            const symbol = Symbol('ignored'), target: any = { '2': 2, first: 1 };
            Object.defineProperty(target, 'hidden', { value: 9, enumerable: false, configurable: true });
            target[symbol] = 7;
            const proxy: any = new Proxy(target, {});
            console.log(Object.keys(proxy).join(','), Object.values(proxy).join(','));
            console.log(Object.entries(proxy).map(entry => entry.join(':')).join('|'));
            console.log(Object.getOwnPropertySymbols(proxy)[0] === symbol,
                Object.getOwnPropertyDescriptor(proxy, 'hidden')!.enumerable);
            """, "2,first 2,1\n2:2|first:1\ntrue false\n"];
        yield return ["fixed-trap-order-descriptors-and-getter-receiver", """
            const symbol = Symbol('ignored'), events: string[] = [];
            let proxy: any;
            class Owner {}
            const target: any = Owner.prototype;
            target.first = 1; target.last = 2;
            Object.defineProperty(target, 'hidden', {
                get() { events.push('wrong:hidden'); return 9; }, enumerable: false, configurable: true
            });
            Object.defineProperty(target, 'access', {
                get() { events.push('body:' + (this === proxy)); return 3; }, enumerable: true, configurable: true
            });
            Object.defineProperty(target, symbol, {
                get() { events.push('wrong:symbol'); return 7; }, enumerable: true, configurable: true
            });
            proxy = new Proxy(target, {
                ownKeys() { events.push('keys'); return ['last', 'hidden', 'access', 'first', symbol]; },
                getOwnPropertyDescriptor(target: any, key: string | symbol) {
                    events.push('desc:' + String(key)); return Reflect.getOwnPropertyDescriptor(target, key);
                },
                get(target: any, key: string | symbol, receiver: any) {
                    events.push('get:' + String(key)); return Reflect.get(target, key, receiver);
                }
            });
            console.log(Object.keys(proxy).join(','), events.join(','));
            events.length = 0;
            console.log(Object.values(proxy).join(','), events.join(','));
            events.length = 0;
            console.log(Object.entries(proxy).map(entry => entry.join(':')).join('|'), events.join(','));
            """, "last,access,first keys,desc:last,desc:hidden,desc:access,desc:first\n2,3,1 keys,desc:last,get:last,desc:hidden,desc:access,get:access,body:true,desc:first,get:first\nlast:2|access:3|first:1 keys,desc:last,get:last,desc:hidden,desc:access,get:access,body:true,desc:first,get:first\n"];
        yield return ["abrupt-getter-descriptor-and-ownkeys-stop-enumeration", """
            const marker = { tag: 'marker' }, events: string[] = [];
            let receiver: any;
            class Owner {}
            const target: any = Owner.prototype;
            Object.defineProperty(target, 'first', {
                get() { events.push('first:' + (this === receiver)); return 1; }, enumerable: true, configurable: true
            });
            Object.defineProperty(target, 'boom', {
                get() { events.push('boom'); throw marker; }, enumerable: true, configurable: true
            });
            Object.defineProperty(target, 'after', {
                get() { events.push('wrong:after'); return 3; }, enumerable: true, configurable: true
            });
            function observe(proxy: any) {
                receiver = proxy;
                for (const entries of [false, true]) {
                    events.length = 0;
                    try { if (entries) Object.entries(proxy); else Object.values(proxy); }
                    catch (error) { console.log(error === marker, events.join(',')); }
                }
            }
            observe(new Proxy(target, {}));
            observe(new Proxy(target, {
                ownKeys() { events.push('keys'); return ['first', 'boom', 'after']; },
                getOwnPropertyDescriptor(target: any, key: string | symbol) {
                    events.push('desc:' + String(key));
                    if (key === 'boom') throw marker;
                    return Reflect.getOwnPropertyDescriptor(target, key);
                }
            }));
            observe(new Proxy(target, {
                ownKeys() { events.push('keys'); throw marker; },
                getOwnPropertyDescriptor() { events.push('wrong:descriptor'); return undefined; }
            }));
            """, "true first:true,boom\ntrue first:true,boom\ntrue keys,desc:first,first:true,desc:boom\ntrue keys,desc:first,first:true,desc:boom\ntrue keys\ntrue keys\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    public static IEnumerable<object[]> SavedCases() => Cases().Concat(
        ClassPrototypeOwnKeyReflectionTests.Cases().Where(row => (string)row[0] == "transparent-proxy-reflection"));

    [Theory, MemberData(nameof(ModeCases))]
    public void ProxyEnumerationUsesOwnDescriptorsAndPreservesGetReceivers(
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
