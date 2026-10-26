using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class P3InterpreterMetadataTests
{
    [Theory, ModeData]
    public void BoundFunctionDeletedMetadataAgreesWithOwnDescriptors(ExecutionMode mode)
    {
        AssertOutput("""
            function f() {}
            const bound: any = f.bind(null);
            for (const key of ["length", "name"]) {
                console.log(Object.hasOwn(bound, key));
                console.log(delete bound[key], Object.hasOwn(bound, key),
                    Object.prototype.hasOwnProperty.call(bound, key),
                    Object.getOwnPropertyDescriptor(bound, key) === undefined);
                Object.defineProperty(bound, key, { value: 7, configurable: true });
                console.log(Object.hasOwn(bound, key), bound[key]);
            }
            """, "true\ntrue false false true\ntrue 7\ntrue\ntrue false false true\ntrue 7\n", mode);
    }

    [Theory, ModeData]
    public void ObjectConstructorMetadataCanBeDeletedAndRestored(ExecutionMode mode)
    {
        AssertOutput("""
            const O: any = Object;
            console.log(delete O.name, Object.hasOwn(O, "name"), JSON.stringify(O.name),
                Object.getOwnPropertyDescriptor(O, "name") === undefined);
            console.log(delete O.length, Object.hasOwn(O, "length"), O.length,
                Object.getOwnPropertyDescriptor(O, "length") === undefined);
            Object.defineProperty(O, "name", { value: "Restored", configurable: true });
            Object.defineProperty(O, "length", { value: 3, configurable: true });
            console.log(O.name, O.length, Object.hasOwn(O, "name"), Object.hasOwn(O, "length"));
            """, "true false \"\" true\ntrue false 0 true\nRestored 3 true true\n", mode);
    }

    [Theory, ModeData]
    public void BuiltInConstructorNamesAreOwnConfigurableMetadata(ExecutionMode mode)
    {
        AssertOutput("""
            for (const ctor of [AbortController, Map]) {
                const value: any = ctor;
                const descriptor: any = Object.getOwnPropertyDescriptor(value, "name");
                console.log(value.name, Reflect.get(value, "name"), Object.hasOwn(value, "name"),
                    descriptor.writable, descriptor.enumerable, descriptor.configurable);
                console.log(delete value.name, Object.hasOwn(value, "name"), JSON.stringify(value.name),
                    JSON.stringify(Reflect.get(value, "name")),
                    Object.getOwnPropertyDescriptor(value, "name") === undefined);
                Object.defineProperty(value, "name", { value: "replacement", configurable: true });
                console.log(value.name, Reflect.get(value, "name"), Object.hasOwn(value, "name"));
            }
            """, "AbortController AbortController true false false true\ntrue false \"\" \"\" true\nreplacement replacement true\nMap Map true false false true\ntrue false \"\" \"\" true\nreplacement replacement true\n", mode);
    }

    private static void AssertOutput(string source, string expected, ExecutionMode mode)
    {
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
