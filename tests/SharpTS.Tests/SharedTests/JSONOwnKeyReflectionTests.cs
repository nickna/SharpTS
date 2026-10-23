using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class JSONOwnKeyReflectionTests
{
    [Theory, ModeData]
    public void OwnReflectionIncludesIntrinsicJSONMethods(ExecutionMode mode)
    {
        var source = """
            console.log(Reflect.ownKeys(JSON).includes('parse'), Object.getOwnPropertyNames(JSON).includes('parse'));
            const descriptors = Object.getOwnPropertyDescriptors(JSON);
            console.log(Object.hasOwn(descriptors, 'parse'), descriptors.parse?.value === JSON.parse);
            console.log(Object.hasOwn(JSON, 'parse'), typeof JSON.parse);
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "true true\ntrue true\ntrue function\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
