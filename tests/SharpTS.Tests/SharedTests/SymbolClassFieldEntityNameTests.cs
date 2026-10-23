using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class SymbolClassFieldEntityNameTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["inferred-static", """
            const key = Symbol('key');
            class C { static [key] = 1; }
            console.log((C as any)[key]);
            """ + "\n", "1\n"];
        yield return ["inferred-instance", """
            const key = Symbol('key');
            class C { [key] = 1; }
            console.log((new C() as any)[key]);
            """ + "\n", "1\n"];
        yield return ["explicit-static", """
            const key: unique symbol = Symbol('key');
            class C { static [key] = 1; }
            console.log((C as any)[key]);
            """ + "\n", "1\n"];
        yield return ["full-original-inferred-symbol-own-updates", """
            const fieldKey = Symbol('field'), methodKey = Symbol('method'), expandoKey = Symbol('expando');
            class C { static [fieldKey] = 1; static [methodKey]() { return 2; } }
            const owner: any = C;
            owner[expandoKey] = 3;
            function sloppyWrite(target: any, key: symbol, value: any) { return target[key] = value; }
            function strictWrite(target: any, key: symbol, value: any) { 'use strict'; return target[key] = value; }
            Object.preventExtensions(owner);
            sloppyWrite(owner, fieldKey, 4);
            sloppyWrite(owner, methodKey, () => 5);
            sloppyWrite(owner, expandoKey, 6);
            console.log(owner[fieldKey], owner[methodKey](), owner[expandoKey],
                Object.hasOwn(owner, fieldKey), Object.hasOwn(owner, methodKey), Object.hasOwn(owner, expandoKey));
            strictWrite(owner, fieldKey, 7);
            strictWrite(owner, methodKey, () => 8);
            strictWrite(owner, expandoKey, 9);
            console.log(owner[fieldKey], owner[methodKey](), owner[expandoKey],
                Object.hasOwn(owner, fieldKey), Object.hasOwn(owner, methodKey), Object.hasOwn(owner, expandoKey));
            """ + "\n", "4 5 6 true true true\n7 8 9 true true true\n"];
        yield return ["qualified-keyword-property-symbol-field", """
            const holder = { default: Symbol('key') };
            class C { static [holder.default] = 1; }
            console.log((C as any)[holder.default]);
            """ + "\n", "1\n"];
        yield return ["annotated-let-symbol-instance-field", """
            let key: symbol = Symbol('key');
            class C { [key] = 1; }
            console.log((new C() as any)[key]);
            """ + "\n", "1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void EntityNameSymbolKeysRemainValidClassFields(
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
