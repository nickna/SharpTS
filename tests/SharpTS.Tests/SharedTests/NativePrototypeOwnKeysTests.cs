using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NativePrototypeOwnKeysTests
{
    [Theory, ModeData]
    public void ObjectKeysAcceptsNativePrototypeReceivers(ExecutionMode mode)
    {
        var source = """
            const names = ['String', 'Number', 'Boolean', 'Array', 'Object', 'Function', 'Error', 'Promise', 'BigInt', 'Symbol'];
            const prototypes: any[] = [String.prototype, Number.prototype, Boolean.prototype, Array.prototype, Object.prototype, Function.prototype, Error.prototype, Promise.prototype, BigInt.prototype, Symbol.prototype];
            for (let i = 0; i < prototypes.length; i++) {
                try {
                    console.log(names[i], Object.keys(prototypes[i]).includes('constructor'));
                } catch (e) {
                    console.log(names[i], 'threw');
                }
            }
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "String false\nNumber false\nBoolean false\nArray false\nObject false\nFunction false\nError false\nPromise false\nBigInt false\nSymbol false\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
