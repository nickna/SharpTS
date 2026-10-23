using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class CompiledGlobalReflectValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["bare-and-qualified-reflect-identity", """
            const bare: any = Reflect;
            const global: any = globalThis.Reflect;
            console.log(typeof bare, typeof global, bare === global);
            """ + "\n", "object object true\n"];
        yield return ["qualified-reflect-value-lifecycle", """
            const root: any = globalThis;
            const indexed: any = root['Reflect'];
            const first: any = globalThis.Reflect;
            console.log('acquired', typeof first, first === indexed, first === root.Reflect);
            const get: any = first.get;
            const target: any = { value: 7 };
            console.log('call', get(target, 'value'), typeof indexed.defineProperty);
            function lexical(globalThis: any) { return globalThis.Reflect; }
            const local: any = { Reflect: { marker: 11 } };
            console.log('lexical', lexical(local).marker, first === root.Reflect);
            try {
                root.Reflect = { marker: 13 };
                console.log('override', globalThis.Reflect === root.Reflect, (globalThis.Reflect as any).marker);
                delete root.Reflect;
                console.log('deleted', globalThis.Reflect === undefined, root.Reflect === undefined);
            } finally {
                root.Reflect = first;
            }
            console.log('restored', globalThis.Reflect === first, root.Reflect === first, get(target, 'value'));
            """ + "\n", "acquired object true true\ncall 7 function\nlexical 11 true\noverride true 13\ndeleted true true\nrestored true true 7\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void QualifiedReflectValuesRetainCanonicalIdentityAndLiveBindings(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
