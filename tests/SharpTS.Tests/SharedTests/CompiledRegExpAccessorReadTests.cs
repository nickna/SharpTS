using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class CompiledRegExpAccessorReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["regexp-own-getterless-read", """
            const target: any = /a/;
            Object.defineProperty(target, 'slot', { set(value: any) {} });
            console.log(target.slot === undefined, target.slot === null);
            """ + "\n", "true false\n"];
        yield return ["regexp-own-accessor-read-controls", """
            const target: any = /a/g;
            let writes = 0, reads = 0, receiver = false;
            Object.defineProperty(target, 'omitted', {
                set: function(this: any, value: any) { writes++; receiver = this === target; },
                configurable: true
            });
            target.omitted = 7;
            console.log('omitted', target.omitted === undefined, target['omitted'] === undefined, writes, receiver);
            Object.defineProperty(target, 'explicit', {
                get: undefined,
                set: function(this: any, value: any) { writes++; receiver = this === target; },
                configurable: true
            });
            target.explicit = 8;
            console.log('explicit', target.explicit === undefined, target['explicit'] === undefined, writes, receiver);
            Object.defineProperty(target, 'nil', { value: null, configurable: true });
            Object.defineProperty(target, 'empty', { value: undefined, configurable: true });
            console.log('data', target.nil === null, target.nil !== undefined, target.empty === undefined, target.empty !== null);
            Object.defineProperty(target, 'observed', {
                get: function(this: any) { reads++; receiver = this === target; return null; },
                configurable: true
            });
            console.log('getter', target.observed === null, reads, receiver);
            const token = {};
            Object.defineProperty(target, 'abrupt', {
                get: function(this: any) { reads++; receiver = this === target; throw token; },
                configurable: true
            });
            let caught = false;
            try { target.abrupt; } catch (error) { caught = error === token; }
            console.log('abrupt', caught, reads, receiver);
            console.log('native', target.source, target.global, target.lastIndex, target.test('a'));
            """ + "\n", "omitted true true 1 true\nexplicit true true 2 true\ndata true true true true\ngetter true 1 true\nabrupt true 2 true\nnative a true 0 true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OwnRegExpAccessorsRetainUndefinedAndDataValues(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
