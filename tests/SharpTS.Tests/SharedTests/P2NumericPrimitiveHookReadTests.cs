using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2NumericPrimitiveHookReadTests
{
    [Theory, ModeData]
    public void NumericCoercionReadsAbsentExoticHookOncePerWrapper(ExecutionMode mode)
    {
        const string source = """
            for (const box of [new Number(8), new String("8"), new Boolean(true)] as any[]) {
                for (const missing of [undefined, null]) {
                    let reads = 0;
                    Object.defineProperty(box, Symbol.toPrimitive, { get() {
                        reads++;
                        if (reads > 1) throw "extra read";
                        return missing;
                    }, configurable: true });
                    console.log(box * 2, reads);
                }
            }
            const hooked: any = new Number(8);
            hooked[Symbol.toPrimitive] = (hint: string) => { console.log(hint); return 3; };
            console.log(hooked * 2);
            """;
        Assert.Equal("16 1\n16 1\n16 1\n16 1\n2 1\n2 1\nnumber\n6\n", TestHarness.Run(source, mode));
    }
}
