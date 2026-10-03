namespace SharpTS.Tests.SharedTests;

public sealed class WindowsProcessShutdownTests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        yield return new object[] { "global_receiver", """
            const probe:any=Function("return this")();console.log(probe===globalThis,probe.Object===Object);const root:any=globalThis;root.__sharptsGlobalReceiver=6;const fn:any=Function("return this.__sharptsGlobalReceiver");console.log(fn(),fn.call(root));delete root.__sharptsGlobalReceiver;
            """, "true true\n6 6\n" };
        yield return new object[] { "indirect_dynamic", """
            const holder:any={evaluate:eval};const text=String("1+2");console.log(holder.evaluate(text));
            """, "3\n" };
    }
}
