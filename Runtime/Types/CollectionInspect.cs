namespace SharpTS.Runtime.Types;

/// <summary>
/// Shared inspect-style value formatting for collection ToString output.
/// One implementation replaces the byte-identical FormatValue copies in
/// SharpTSMap / SharpTSSet (2026-07 cleanup).
/// </summary>
internal static class CollectionInspect
{
    public static string FormatValue(object? value) => value switch
    {
        null => "undefined",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        SharpTSArray arr => arr.ToString(),
        SharpTSObject obj => obj.ToString(),
        _ => value.ToString() ?? "undefined"
    };
}
