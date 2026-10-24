using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ComputedLiteralMethodOverloadTests
{
    public const string Source = """
        class C {
            ['read'](value: number): number;
            ['read'](value: string): string;
            ['read'](value: number | string): number | string { return value; }
            static ['read'](value: number): number;
            static ['read'](value: string): string;
            static ['read'](value: number | string): number | string { return value; }
        }
        const E = class {
            ['read'](value: number): number;
            ['read'](value: string): string;
            ['read'](value: number | string) { return value; }
        };
        const n: number = new C().read(1);
        const s: string = new C().read('ok');
        const sn: number = C.read(2);
        const ss: string = C.read('static');
        const en: number = new E().read(3);
        const es: string = new E().read('expression');
        console.log(n, s, sn, ss, en, es);
        """;

    [Theory, ModeData]
    public void PublicOverloadsRetainRuntimeImplementation(ExecutionMode mode)
        => Assert.Equal("1 ok 2 static 3 expression\n", TestHarness.Run(Source, mode));
}
