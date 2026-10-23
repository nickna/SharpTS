using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ReflectClassPrototypeDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["fresh-string", """
            class Owner {}
            const source: any = Owner.prototype;
            const accepted = Reflect.defineProperty(source, 'fresh', { value: 7, writable: true, enumerable: true, configurable: true });
            console.log(accepted, Object.hasOwn(source, 'fresh'), Reflect.get(source, 'fresh'));
            """ + "\n", "true true 7\n"];
        yield return ["present-string", """
            class Owner { method() { return 7; } }
            const source: any = Owner.prototype;
            const original = source.method;
            const accepted = Reflect.defineProperty(source, 'method', { value: original, writable: true, enumerable: true, configurable: true });
            const after = Object.getOwnPropertyDescriptor(source, 'method')!;
            console.log(accepted, source.method === original, after.writable, after.enumerable, after.configurable);
            """ + "\n", "true true true true true\n"];
        yield return ["symbol-control", """
            const key = Symbol('fresh');
            class Owner {}
            const source: any = Owner.prototype;
            const accepted = Reflect.defineProperty(source, key, { value: 7, writable: true, enumerable: true, configurable: true });
            console.log(accepted, Object.hasOwn(source, key), Reflect.get(source, key));
            """ + "\n", "true true 7\n"];
        yield return ["object-control", """
            class Owner {}
            const source: any = Owner.prototype;
            const returned = Object.defineProperty(source, 'fresh', { value: 7, writable: true, enumerable: true, configurable: true });
            console.log(returned === source, Object.hasOwn(source, 'fresh'), Reflect.get(source, 'fresh'));
            """ + "\n", "true true 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void StringDefinitionsUseClassPrototypeOwnStorage(
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
