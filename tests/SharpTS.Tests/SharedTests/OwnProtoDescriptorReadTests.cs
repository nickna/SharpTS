using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class OwnProtoDescriptorReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["class-prototype-own-data", """
            const replacement = { marker: 7 };
            class Owner {}
            const source: any = Owner.prototype;
            Object.defineProperty(source, '__proto__', { value: replacement, writable: true, enumerable: true, configurable: true });
            console.log(source.__proto__ === replacement, Reflect.get(source, '__proto__') === replacement,
                Object.getOwnPropertyDescriptor(source, '__proto__')!.value === replacement,
                Object.getPrototypeOf(source) === Object.prototype);
            """, "true true true true\n"];
        yield return ["ordinary-record-own-data", """
            const replacement = { marker: 7 }, source: any = {};
            Object.defineProperty(source, '__proto__', { value: replacement, writable: true, enumerable: true, configurable: true });
            console.log(source.__proto__ === replacement, Reflect.get(source, '__proto__') === replacement,
                Object.getOwnPropertyDescriptor(source, '__proto__')!.value === replacement,
                Object.getPrototypeOf(source) === Object.prototype);
            """, "true true true true\n"];
        yield return ["ordinary-record-literal-own-data", """
            const replacement = { marker: 7 }, source: any = { ['__proto__']: replacement };
            console.log(source.__proto__ === replacement, Reflect.get(source, '__proto__') === replacement,
                Object.getOwnPropertyDescriptor(source, '__proto__')!.value === replacement,
                Object.getPrototypeOf(source) === Object.prototype);
            """, "true true true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OwnDataPropertyShadowsInheritedProtoAccessor(
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
