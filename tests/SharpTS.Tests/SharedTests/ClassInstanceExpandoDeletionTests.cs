using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassInstanceExpandoDeletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["controls", """
            class P extends Promise<number> {}
            const intrinsic: any = Promise.resolve(1);
            intrinsic.slot = 7;
            console.log(delete intrinsic.slot, Object.hasOwn(intrinsic, 'slot'), Object.getOwnPropertyDescriptor(intrinsic, 'slot') === undefined);
            const target: any = new P(resolve => resolve(1));
            Object.defineProperty(target, 'slot', { value: 7, writable: true, configurable: true });
            console.log(delete target.slot, Object.hasOwn(target, 'slot'), Object.getOwnPropertyDescriptor(target, 'slot') === undefined);
            class Ordinary {}
            const ordinary: any = new Ordinary();
            ordinary.slot = 7;
            console.log(delete ordinary.slot, Object.hasOwn(ordinary, 'slot'), Object.getOwnPropertyDescriptor(ordinary, 'slot') === undefined);
            """, "true false true\ntrue false true\ntrue false true\n"];
        yield return ["expando-deletion-preserves-descriptors-and-prototypes", """
            class C { stable = 10; }
            const target: any = new C();
            (C.prototype as any).shared = 9;
            target.shared = 4;
            target.first = 1;
            target.second = 2;
            console.log('deleted', delete target.shared, Object.hasOwn(target, 'shared'), target.shared);
            console.log('ordered', delete target.first, Object.keys(target).join(','));
            target.first = 3;
            console.log('recreated', Object.keys(target).join(','), target.first);
            Object.defineProperty(target, 'locked', { value: 5, writable: true, configurable: false });
            console.log('locked', Reflect.deleteProperty(target, 'locked'), Object.hasOwn(target, 'locked'), target.locked);
            class StrictDeleter { remove(value: any) { return delete value.locked; } }
            try { console.log('unexpected', new StrictDeleter().remove(target)); }
            catch (error) { console.log('strict', error instanceof TypeError, target.locked); }
            let reads = 0;
            Object.defineProperty(target, 'accessor', { configurable: true, get() { reads++; return 8; } });
            console.log('accessor', delete target.accessor, Object.hasOwn(target, 'accessor'), reads);
            const sealed: any = new C();
            sealed.slot = 8;
            Object.seal(sealed);
            console.log('sealed', Reflect.deleteProperty(sealed, 'slot'), Object.hasOwn(sealed, 'slot'), sealed.slot);
            console.log('missing', Reflect.deleteProperty(sealed, 'missing'), Object.hasOwn(sealed, 'missing'));
            class Base { base = 1; }
            class Derived extends Base { own = 2; }
            const derived: any = new Derived();
            derived.slot = 7;
            console.log('derived', delete derived.slot, Object.hasOwn(derived, 'slot'), derived.base, derived.own);
            """ + "\n", "deleted true false 9\nordered true stable,second\nrecreated stable,second,first 3\nlocked false true 5\nstrict true 5\naccessor true false 0\nsealed false true 8\nmissing true false\nderived true false 1 2\n"];
        yield return ["main", """
            class P extends Promise<number> {}
            const target: any = new P(resolve => resolve(1));
            target.slot = 7;
            console.log(target.slot, Object.hasOwn(target, 'slot'));
            console.log(delete target.slot, Object.hasOwn(target, 'slot'), Object.getOwnPropertyDescriptor(target, 'slot') === undefined);
            """, "7 true\ntrue false true\n"];
        yield return ["ordinary-class", """
            class C {}
            const target: any = new C();
            target.slot = 7;
            console.log(target.slot, Object.hasOwn(target, 'slot'));
            console.log(delete target.slot, Object.hasOwn(target, 'slot'), Object.getOwnPropertyDescriptor(target, 'slot') === undefined);
            const defined: any = new C();
            Object.defineProperty(defined, 'slot', { value: 7, writable: true, configurable: true });
            console.log(delete defined.slot, Object.hasOwn(defined, 'slot'), Object.getOwnPropertyDescriptor(defined, 'slot') === undefined);
            """, "7 true\ntrue false true\ntrue false true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AssignedClassExpandosDeleteTheirOwnStorage(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
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
