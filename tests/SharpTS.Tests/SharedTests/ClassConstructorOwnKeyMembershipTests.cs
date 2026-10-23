using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassConstructorOwnKeyMembershipTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["constructor-own-intrinsic-and-static-field-membership", """
            class C { static value = 1; }
            const owner: any = C;
            const names = Object.getOwnPropertyNames(owner);
            console.log(owner.value, names.includes('name'), names.includes('prototype'), names.includes('value'),
                Object.keys(owner).includes('value'));
            """.ReplaceLineEndings("\n") + "\n", "1 true true true true\n"];
        yield return ["constructor-string-and-symbol-method-membership", """
            const key = Symbol("method");
            class C { static value = 1; static method() {} static [key]() {} }
            const owner: any = C;
            const names = Object.getOwnPropertyNames(owner);
            const symbols = Object.getOwnPropertySymbols(owner);
            const all = Reflect.ownKeys(owner);
            console.log(Object.getOwnPropertyDescriptor(owner, "method") !== undefined,
                Object.getOwnPropertyDescriptor(owner, key) !== undefined);
            console.log(names.includes("length"), names.includes("name"), names.includes("prototype"),
                names.includes("value"), names.includes("method"));
            console.log(symbols.includes(key), all.includes("method"), all.includes(key),
                Object.keys(owner).includes("value"));
            """.ReplaceLineEndings("\n") + "\n", "true true\ntrue true true true true\ntrue true true true\n"];
        yield return ["generic-constructor-method-membership", """
            class Owner<T> { static method() {} }
            const owner: any = Owner;
            console.log(typeof owner.method, Object.getOwnPropertyDescriptor(owner, 'method') !== undefined,
                Object.getOwnPropertyNames(owner).includes('method'), Reflect.ownKeys(owner).includes('method'));
            """.ReplaceLineEndings("\n") + "\n", "function true true true\n"];
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [row[0], row[1], row[2], mode];
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ConstructorReflectionIncludesOwnIntrinsicAndDeclaredKeys(
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
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
