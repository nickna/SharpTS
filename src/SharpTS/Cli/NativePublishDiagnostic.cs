using System.Text.RegularExpressions;

namespace SharpTS.Cli;

/// <summary>Preserves the original SDK/ILC diagnostic code and text without remapping it.</summary>
internal sealed partial record NativePublishDiagnostic(string Code, string Severity, string Message, string Origin, string RawText)
{
    [GeneratedRegex(@"^(?<origin>.+?)\s*:\s*(?:(?:Trim|AOT) analysis\s+)?(?<severity>warning|error)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ToolDiagnostic();

    internal static NativePublishDiagnostic? Parse(string line)
    {
        var match = ToolDiagnostic().Match(line);
        return match.Success ? new(match.Groups["code"].Value, match.Groups["severity"].Value,
            match.Groups["message"].Value, match.Groups["origin"].Value, line) : null;
    }
}
