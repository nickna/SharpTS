using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NullRootDescriptorPresenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["direct-define-property-control", """
            const target: any = {}, empty: any = Object.create(null), prototype: any = Object.prototype;
            const state = { reads: 0 }, marker = {};
            Object.defineProperty(target, 'entry', { value: 7, writable: true, enumerable: true, configurable: true });
            Object.defineProperty(prototype, 'value', { configurable: true, get() { state.reads++; throw marker; } });
            try { Object.defineProperty(target, 'entry', empty); }
            finally { delete prototype.value; }
            const entry = Object.getOwnPropertyDescriptor(target, 'entry')!;
            console.log(target.entry, state.reads, entry.value, entry.writable, entry.enumerable, entry.configurable);
            """, "7 0 7 true true true\n"];
        yield return ["independent-getter-return-99-control", """
            const empty: any = Object.create(null), target: any = {};
            const prototype: any = Object.prototype;
            let reads = 0, caught = false;
            Object.defineProperty(target, 'entry', { value: 7, writable: true, configurable: true });
            Object.defineProperty(prototype, 'value', {
                get() { reads++; return 99; }, configurable: true
            });
            try { Object.defineProperties(target, { entry: empty }); }
            catch (error) { caught = true; }
            finally { delete prototype.value; }
            console.log(caught, reads, target.entry);
            """ + "\n", "false 0 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AbsentDescriptorFieldsDoNotCrossNullPrototype(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }
}
