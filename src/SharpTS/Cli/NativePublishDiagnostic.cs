using System.Text.RegularExpressions;

namespace SharpTS.Cli;

/// <summary>Preserves the original SDK/ILC diagnostic code and text without remapping it.</summary>
internal sealed partial record NativePublishDiagnostic(string Code, string Severity, string Message, string Origin, string RawText)
{
    [GeneratedRegex(@"^(?<origin>.+?)\s*:\s*(?:(?:Trim|AOT) analysis\s+)?(?<severity>warning|error)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ToolDiagnostic();

    [GeneratedRegex(@"^\s*ILC:\s+Method\s+.+\s+will always throw because:\s*(?<message>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex InvalidMethodBody();

    internal static NativePublishDiagnostic? Parse(string line)
    {
        var match = ToolDiagnostic().Match(line);
        if (match.Success) return new(match.Groups["code"].Value, match.Groups["severity"].Value,
            match.Groups["message"].Value, match.Groups["origin"].Value, line);
        // ILC can return success after replacing an invalid method with a throw.
        // This uncoded report is a publish failure; retain its original text.
        return InvalidMethodBody().IsMatch(line) ? new("SHARPTS408", "error", line.Trim(), "ILC", line) : null;
    }
}
