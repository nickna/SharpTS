using System.Text.Json;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Reports;

namespace SharpTS.Benchmarks;

/// <summary>
/// Writes the BenchmarkDotNet descriptor data that its built-in JSON exporter omits.
/// The public snapshot transformer joins this file to the built-in structured report
/// by summary title and report order, verifying each type and method; no presentation
/// report or display-formatted parameter value is parsed.
/// </summary>
internal static class StructuredBenchmarkMetadata
{
    private const string SourceFormat = "sharpts-benchmarkdotnet-metadata-v1";

    public static void Write(IEnumerable<Summary> summaries)
    {
        foreach (Summary summary in summaries)
        {
            if (summary.Reports.IsDefaultOrEmpty)
                continue;

            var payload = new
            {
                sourceFormat = SourceFormat,
                title = summary.Title,
                benchmarks = summary.Reports.Select(report => new
                {
                    fullName = GetDescriptiveFullName(report),
                    type = report.BenchmarkCase.Descriptor.Type.Name,
                    method = report.BenchmarkCase.Descriptor.WorkloadMethod.Name,
                    categories = report.BenchmarkCase.Descriptor.Categories,
                    operationsPerInvoke = report.BenchmarkCase.Descriptor.OperationsPerInvoke,
                    parameters = report.BenchmarkCase.Parameters.Items.Select(parameter => new
                    {
                        name = parameter.Name,
                        value = parameter.Value,
                    }),
                }),
            };

            Directory.CreateDirectory(summary.ResultsDirectoryPath);
            string stableTitle = Regex.Replace(summary.Title, @"-\d{8}-\d{6}$", string.Empty);
            string safeTitle = string.Concat(stableTitle.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            string path = Path.Combine(
                summary.ResultsDirectoryPath,
                $"{safeTitle}-sharpts-metadata.json");
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
            }) + Environment.NewLine);
        }
    }

    private static string GetDescriptiveFullName(BenchmarkReport report)
    {
        string fullName = $"{report.BenchmarkCase.Descriptor.Type.FullName}.{report.BenchmarkCase.Descriptor.WorkloadMethod.Name}";
        if (report.BenchmarkCase.Parameters.Count == 0)
            return fullName;

        string parameters = string.Join(", ", report.BenchmarkCase.Parameters.Items.Select(parameter =>
            $"{parameter.Name}: {Convert.ToString(parameter.Value, System.Globalization.CultureInfo.InvariantCulture)}"));
        return $"{fullName}({parameters})";
    }
}
