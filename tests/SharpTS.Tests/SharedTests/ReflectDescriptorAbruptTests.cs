using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ReflectDescriptorAbruptTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public-reflect-descriptor-abrupt", """
            const marker = {};
            function check(label: string, descriptor: any) {
                try { console.log(label, "returned", Reflect.defineProperty({}, "x", descriptor)); }
                catch (error) { console.log(label, "threw", error === marker, error instanceof TypeError); }
            }
            check("value getter", { get value() { throw marker; } });
            check("get getter", { get get() { throw marker; } });
            check("mixed", { value: 1, get() { return 2; } });
            check("noncallable get", { get: 1 });
            check("noncallable set", { set: 1 });
            """ + "\n", "value getter threw true false\nget getter threw true false\nmixed threw false true\nnoncallable get threw false true\nnoncallable set threw false true\n"];
        yield return ["descriptor-conversion-and-refusal-lifecycle", """
            const marker = {};
            for (const field of ["enumerable", "configurable", "value", "writable", "get", "set"]) {
                const descriptor: any = {};
                Object.defineProperty(descriptor, field, { get() { throw marker; }, configurable: true });
                let same = false;
                try { Reflect.defineProperty({}, "x", descriptor); }
                catch (error) { same = error === marker; }
                console.log(field, same);
            }
            for (const payload of [undefined, null, 0, false, ""]) {
                const descriptor: any = { get value() { throw payload; } };
                let caught = false, same = false;
                try { Reflect.defineProperty({}, "x", descriptor); }
                catch (error) { caught = true; same = error === payload; }
                console.log("payload", typeof payload, caught, same);
            }
            let reads = 0;
            const target: any = {};
            const descriptor: any = {
                get value() { reads++; return 41; }, writable: true, enumerable: true, configurable: true
            };
            console.log("valid", Reflect.defineProperty(target, "x", descriptor), target.x, reads);
            const sealed: any = {};
            Object.preventExtensions(sealed);
            console.log("refused", Reflect.defineProperty(sealed, "x", { value: 1 }), Object.hasOwn(sealed, "x"));
            let abrupt = false;
            try { Reflect.defineProperty(sealed, "x", { get value() { throw marker; } }); }
            catch (error) { abrupt = error === marker; }
            console.log("refused-abrupt", abrupt, Object.hasOwn(sealed, "x"));
            const fixed: any = {};
            Object.defineProperty(fixed, "x", { value: 1, writable: false, configurable: false });
            console.log("fixed", Reflect.defineProperty(fixed, "x", { value: 2 }), fixed.x);
            """ + "\n", "enumerable true\nconfigurable true\nvalue true\nwritable true\nget true\nset true\npayload undefined true true\npayload object true true\npayload number true true\npayload boolean true true\npayload string true true\nvalid true 41 1\nrefused false false\nrefused-abrupt true false\nfixed false 1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DescriptorConversionThrowsBeforeOrdinaryDefinitionRefusal(
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
