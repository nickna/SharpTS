namespace SharpTS.Test262;

public static class Test262ReportMode
{
    public const string EnvironmentVariable = "SHARPTS_TEST262_DIFFERENTIAL_REPORT";

    public static bool IsEnabled(string? value) => value is "1" or "true" or "TRUE";

    public static bool IsEnabled() => IsEnabled(Environment.GetEnvironmentVariable(EnvironmentVariable));
}
