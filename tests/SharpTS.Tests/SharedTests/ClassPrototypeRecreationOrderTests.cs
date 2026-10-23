using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassPrototypeRecreationOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["prototype", """
            class C { first() {} last() {} }
            const prototype: any = C.prototype;
            const first = prototype.first;
            delete prototype.first;
            Object.defineProperty(prototype, 'first', { value: first, writable: true, enumerable: false, configurable: true });
            console.log(Object.getOwnPropertyNames(prototype).join(','));
            console.log(Reflect.ownKeys(prototype).join(','));
            console.log(prototype.first === first);
            """ + "\n", "constructor,last,first\nconstructor,last,first\ntrue\n"];
        yield return ["ordinary-control", """
            const target: any = {};
            Object.defineProperty(target, 'first', { value: 1, configurable: true });
            Object.defineProperty(target, 'last', { value: 2, configurable: true });
            delete target.first;
            Object.defineProperty(target, 'first', { value: 3, configurable: true });
            console.log(Object.getOwnPropertyNames(target).join(','));
            console.log(Reflect.ownKeys(target).join(','));
            console.log(target.first, target.last);
            """ + "\n", "last,first\nlast,first\n3 2\n"];
        yield return ["existing-property-redefinition-retains-order", """
            class C { first() {} last() {} }
            const prototype: any = C.prototype;
            const first = prototype.first;
            Object.defineProperty(prototype, 'first', { value: first, writable: true, enumerable: false, configurable: true });
            console.log(Object.getOwnPropertyNames(prototype).join(','));
            console.log(Reflect.ownKeys(prototype).join(','));
            console.log(prototype.first === first);
            """ + "\n", "constructor,first,last\nconstructor,first,last\ntrue\n"];
    }

    public static IEnumerable<object[]> StandaloneCases() => Cases().Concat(
        new[] { new object[] { "full-deletion-recreation-original",
            ClassPrototypeOwnKeyReflectionTests.RecreationOrderSource,
            ClassPrototypeOwnKeyReflectionTests.RecreationOrderExpected } });

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void RecreationAppendsOnlyDeletedProperties(
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
