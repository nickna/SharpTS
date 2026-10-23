using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class MathOwnKeyReflectionTests
{
    [Theory, ModeData]
    public void OwnKeyReflectionIncludesIntrinsicAndDefinedStringProperties(ExecutionMode mode)
    {
        var source = """
            console.log(Reflect.ownKeys(Math).includes('max'), Object.getOwnPropertyNames(Math).includes('max'));
            const key = '__mathOwnKeyProbe';
            Object.defineProperty(Math, key, { value: 7, enumerable: true, configurable: true });
            try {
                console.log((Math as any)[key], Object.getOwnPropertyDescriptor(Math, key)!.value);
                console.log(Reflect.ownKeys(Math).includes(key), Object.getOwnPropertyNames(Math).includes(key));
            } finally { delete (Math as any)[key]; }
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "true true\n7 7\ntrue true\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
