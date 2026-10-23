using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

// Exercises interpreter-owned SharpTSInstance accessor storage and dispatch.
[Collection("CompilationService")]
public sealed class ClassInstanceUndefinedAccessorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["explicit-undefined-class-accessor-presence", """
            class C {}
            const target: any = new C();
            Object.defineProperty(target, 'empty', { get: undefined, configurable: true });
            console.log(Object.hasOwn(target, 'empty'), Object.getOwnPropertyDescriptor(target, 'empty') !== undefined);
            """.ReplaceLineEndings("\n") + "\n", "true true\n"];
        yield return ["class-instance-undefined-accessor-shape-and-updates", """
            let reads = 0, writes = 0;
            class C { stable = 10; }
            class StrictOps {
              write(value: any) { value.pair = 9; }
              remove(value: any, key: string) { return delete value[key]; }
            }
            const target: any = new C();
            for (const name of ['get', 'set', 'both']) {
              Object.defineProperty(C.prototype, name, { configurable: true, get() { reads++; return 99; } });
              const descriptor: any = { configurable: true, enumerable: true };
              if (name !== 'set') descriptor.get = undefined;
              if (name !== 'get') descriptor.set = undefined;
              Object.defineProperty(target, name, descriptor);
              target[name] = 1;
              const own: any = Object.getOwnPropertyDescriptor(target, name);
              console.log(name, Object.hasOwn(target, name), typeof target[name], own.get === undefined, own.set === undefined, own.enumerable, own.configurable);
            }
            Object.defineProperty(target, 'constructor', { get: undefined, configurable: true });
            console.log('keys', Object.keys(target).join(','), typeof target.constructor, reads, writes);
            Object.defineProperty(target, 'pair', { configurable: true, get() { reads++; return 5; }, set(value: any) { writes += value; } });
            Object.defineProperty(target, 'pair', { set: undefined });
            target.pair = 1;
            console.log('partial-set', target.pair, reads, writes);
            Object.defineProperty(target, 'pair', { set(value: any) { writes += value; } });
            Object.defineProperty(target, 'pair', { get: undefined });
            target.pair = 2;
            const pairDescriptor: any = Object.getOwnPropertyDescriptor(target, 'pair');
            console.log('partial-get', target.pair === undefined, reads, writes, typeof pairDescriptor.set === 'function');
            Object.defineProperty(target, 'pair', { set: undefined });
            Object.defineProperty(target, 'data', { value: 7, writable: true, enumerable: true, configurable: true });
            Object.defineProperty(target, 'data', { get: undefined });
            const converted: any = Object.getOwnPropertyDescriptor(target, 'data');
            console.log('converted', Object.hasOwn(target, 'data'), typeof target.data, 'value' in converted, converted.get === undefined);
            Object.defineProperty(target, 'data', { value: 8, writable: true });
            console.log('data', target.data, delete target.data, Object.hasOwn(target, 'data'));
            console.log('deleted', delete target.both, Object.hasOwn(target, 'both'));
            Object.defineProperty(target, 'locked', { get: undefined, set: undefined });
            console.log('locked', Object.hasOwn(target, 'locked'), delete target.locked, Reflect.deleteProperty(target, 'locked'));
            try { new StrictOps().write(target); console.log('unexpected-write'); }
            catch (error) { console.log('strict-write', error instanceof TypeError, reads, writes); }
            try { new StrictOps().remove(target, 'locked'); console.log('unexpected-delete'); }
            catch (error) { console.log('strict-delete', error instanceof TypeError); }
            """.ReplaceLineEndings("\n") + "\n", "get true undefined true true true true\nset true undefined true true true true\nboth true undefined true true true true\nkeys stable,get,set,both undefined 0 0\npartial-set 5 1 0\npartial-get true 1 2 true\nconverted true undefined false true\ndata 8 true false\ndeleted true false\nlocked true false false\nstrict-write true 1 2\nstrict-delete true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterRetainsOwnAccessorsWithoutCallableHalves(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        var result = SourceExecutionService.Interpret(source);
        Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
    }
}
