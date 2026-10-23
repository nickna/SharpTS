using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassInstancePrototypeDescriptorReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["no-delete-instance-prototype-data", """
            class C { stable = 10; }
            const prototype: any = C.prototype;
            prototype.shared = 9;
            const target: any = new C();
            console.log(prototype.shared, Object.hasOwn(prototype, 'shared'), target.shared, Object.hasOwn(target, 'shared'));
            """ + "\n", "9 true 9 false\n"];
        yield return ["class-instance-prototype-descriptor-controls", """
            class Values { field = 5; }
            const prototype: any = Values.prototype;
            prototype.shared = 9;
            Object.defineProperty(prototype, 'explicit', { value: 13, configurable: true });
            prototype.nil = null;
            Object.defineProperty(prototype, 'empty', { value: undefined, configurable: true });
            Object.defineProperty(prototype, 'toString', { value: undefined, configurable: true });
            Object.defineProperty(prototype, 'valueOf', { get: undefined, set(value: any) {}, configurable: true });
            const target: any = new Values();
            let reads = 0, receiver = true;
            Object.defineProperty(prototype, 'observed', {
                configurable: true,
                get: function(this: any) { reads++; receiver = receiver && this === target; return this.field; }
            });
            console.log('data', target.shared, target.explicit, Object.hasOwn(target, 'shared'), Object.hasOwn(target, 'explicit'));
            console.log('nullish', target.nil === null, target.empty === undefined, Object.hasOwn(prototype, 'empty'), Object.hasOwn(target, 'empty'));
            console.log('masks', target.toString === undefined, target.valueOf === undefined);
            console.log('getter', target.observed, reads, receiver);
            prototype.field = 99;
            console.log('own', target.field, Object.hasOwn(target, 'field'));
            class Ancestor {}
            (Ancestor.prototype as any).read = function() { return 'ancestor'; };
            class Child extends Ancestor {
                read() { return 'child'; }
                toString() { return 'child-string'; }
                hasOwnProperty(key: string) { return key === 'mine'; }
            }
            const child: any = new Child();
            console.log('methods', child.read(), child.toString(), child.hasOwnProperty('mine'));
            """ + "\n", "data 9 13 false false\nnullish true true true false\nmasks true true\ngetter 5 1 true\nown 5 true\nmethods child child-string true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OrdinaryInstancesReadLivePrototypeDescriptors(
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
