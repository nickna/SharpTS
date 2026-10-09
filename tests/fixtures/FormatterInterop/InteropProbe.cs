namespace SharpTS.FormatterInteropEvidence;

/// <summary>A real referenced CLR type, rather than an unresolved import accepted as any.</summary>
public sealed class InteropProbe
{
    public static string Join(string left, string right) => left + ":" + right;
}
