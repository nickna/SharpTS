namespace SharpTS.Runtime.Types;

/// <summary>
/// Maps legacy IANA timezone aliases to their canonical IDs.
/// .NET handles canonical IANA IDs natively; this covers legacy aliases.
/// </summary>
public static class IanaTimeZoneAliases
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // US aliases
        ["US/Eastern"] = "America/New_York",
        ["US/Central"] = "America/Chicago",
        ["US/Mountain"] = "America/Denver",
        ["US/Pacific"] = "America/Los_Angeles",
        ["US/Alaska"] = "America/Anchorage",
        ["US/Hawaii"] = "Pacific/Honolulu",
        ["US/Arizona"] = "America/Phoenix",
        ["US/Samoa"] = "Pacific/Pago_Pago",

        // European aliases
        ["GB"] = "Europe/London",
        ["GB-Eire"] = "Europe/London",
        ["Eire"] = "Europe/Dublin",
        ["Portugal"] = "Europe/Lisbon",
        ["Poland"] = "Europe/Warsaw",
        ["Turkey"] = "Europe/Istanbul",

        // Asian aliases
        ["Japan"] = "Asia/Tokyo",
        ["Hongkong"] = "Asia/Hong_Kong",
        ["Singapore"] = "Asia/Singapore",
        ["ROK"] = "Asia/Seoul",
        ["ROC"] = "Asia/Taipei",
        ["PRC"] = "Asia/Shanghai",
        ["Iran"] = "Asia/Tehran",
        ["Israel"] = "Asia/Jerusalem",

        // Other aliases
        ["Egypt"] = "Africa/Cairo",
        ["Libya"] = "Africa/Tripoli",
        ["Cuba"] = "America/Havana",
        ["Jamaica"] = "America/Jamaica",
        ["Brazil/East"] = "America/Sao_Paulo",
        ["Canada/Eastern"] = "America/Toronto",
        ["Canada/Central"] = "America/Winnipeg",
        ["Canada/Pacific"] = "America/Vancouver",
        ["Australia/ACT"] = "Australia/Sydney",
        ["NZ"] = "Pacific/Auckland",
        ["NZ-CHAT"] = "Pacific/Chatham",

        // Generic
        ["UCT"] = "Etc/UTC",
        ["Universal"] = "Etc/UTC",
        ["Zulu"] = "Etc/UTC",
        ["GMT"] = "Etc/GMT",
        ["GMT+0"] = "Etc/GMT",
        ["GMT-0"] = "Etc/GMT",
        ["GMT0"] = "Etc/GMT",
        ["Greenwich"] = "Etc/GMT",
    };

    /// <summary>
    /// Resolves a timezone ID, mapping legacy aliases to canonical IANA IDs.
    /// Returns the input unchanged if not a known alias.
    /// </summary>
    public static string Resolve(string id) =>
        Aliases.TryGetValue(id, out var canonical) ? canonical : id;
}
