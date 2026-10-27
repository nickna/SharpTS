using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class WritableFunctionPrototypeIntegrityTests
{
    [Theory, ModeData]
    public void SealedAndNonextensibleFunctionsKeepWritableOwnPrototypeSlots(ExecutionMode mode)
    {
        const string source = """
            function* sealedGenerator() {}
            function* preventedGenerator() {}
            function sealedOrdinary() {}
            function preventedOrdinary() {}
            const functions: any[] = [sealedGenerator, preventedGenerator, sealedOrdinary, preventedOrdinary];
            for (let index = 0; index < functions.length; index++) {
                const fn = functions[index];
                if (index % 2 === 0) Object.seal(fn); else Object.preventExtensions(fn);
                const replacement = {};
                console.log(Reflect.set(fn, "prototype", replacement), fn.prototype === replacement);
                const descriptor: any = Object.getOwnPropertyDescriptor(fn, "prototype");
                console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);
                const next = {};
                console.log(Reflect.defineProperty(fn, "prototype", { value: next }), fn.prototype === next);
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true true\ntrue false false\ntrue true\n", 4)), TestHarness.Run(source, mode));
    }
}
