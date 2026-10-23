using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayPrototypeOwnDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-inherited-index-own-descriptor-and-push", """
            const prototype: any = Object.prototype;
            Object.defineProperty(prototype, '2', {
                value: 2, writable: true, enumerable: true, configurable: true
            });
            try {
                console.log(Object.hasOwn(Array.prototype, '2'),
                    Object.getOwnPropertyDescriptor(Array.prototype, '2') === undefined);
                const values = ['a', 'b'];
                values.push('c');
                console.log(values.join(','));
            } finally {
                delete prototype[2];
            }
            """ + "\n", "false true\na,b,c\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void InheritedIndicesAreAbsentFromOwnDescriptorsAndAllowPush(
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
