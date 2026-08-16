using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace SharpTS.Packaging;

/// <summary>
/// Publishes NuGet packages to a NuGet feed.
/// </summary>
/// <param name="apiKey">API key for authentication.</param>
/// <param name="source">NuGet source URL (defaults to nuget.org).</param>
public class NuGetPublisher(string apiKey, string? source = null)
{
    private const string DefaultNuGetSource = "https://api.nuget.org/v3/index.json";

    private readonly string _source = source ?? DefaultNuGetSource;
    private readonly ILogger _logger = new ConsoleLogger();

    /// <summary>
    /// Pushes a package to the NuGet feed.
    /// </summary>
    /// <param name="packagePath">Path to the .nupkg file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if push was successful.</returns>
    public async Task<bool> PushAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            Console.Error.WriteLine($"Package not found: {packagePath}");
            return false;
        }

        try
        {
            var packageSource = new PackageSource(_source);
            var sourceRepository = Repository.Factory.GetCoreV3(packageSource);
            var updateResource = await sourceRepository.GetResourceAsync<PackageUpdateResource>(cancellationToken);

            await updateResource.Push(
                [packagePath],
                symbolSource: null,
                timeoutInSecond: 300,
                disableBuffering: false,
                getApiKey: _ => apiKey,
                getSymbolApiKey: null,
                noServiceEndpoint: false,
                skipDuplicate: false,
                symbolPackageUpdateResource: null,
                log: _logger);

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to push package: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Pushes both the main package and symbol package (if present).
    /// </summary>
    /// <param name="packagePath">Path to the .nupkg file.</param>
    /// <param name="symbolPackagePath">Optional path to the .snupkg file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if all pushes were successful.</returns>
    public async Task<bool> PushWithSymbolsAsync(
        string packagePath,
        string? symbolPackagePath,
        CancellationToken cancellationToken = default)
    {
        // Push main package
        if (!await PushAsync(packagePath, cancellationToken))
            return false;

        // Push symbol package if present
        if (!string.IsNullOrEmpty(symbolPackagePath) && File.Exists(symbolPackagePath))
        {
            // Symbols go to the same source for nuget.org
            if (!await PushAsync(symbolPackagePath, cancellationToken))
            {
                Console.Error.WriteLine("Warning: Main package pushed successfully, but symbol package push failed.");
            }
        }

        return true;
    }

    /// <summary>
    /// Simple console logger for NuGet operations.
    /// </summary>
    private class ConsoleLogger : ILogger
    {
        public void Log(LogLevel level, string data)
        {
            if (level >= LogLevel.Warning)
            {
                Console.WriteLine(data);
            }
        }

        public void Log(ILogMessage message) => Log(message.Level, message.Message);
        public Task LogAsync(LogLevel level, string data) { Log(level, data); return Task.CompletedTask; }
        public Task LogAsync(ILogMessage message) => LogAsync(message.Level, message.Message);
        public void LogDebug(string data) => Log(LogLevel.Debug, data);
        public void LogError(string data) => Log(LogLevel.Error, data);
        public void LogInformation(string data) => Log(LogLevel.Information, data);
        public void LogInformationSummary(string data) => Log(LogLevel.Information, data);
        public void LogMinimal(string data) => Log(LogLevel.Minimal, data);
        public void LogVerbose(string data) => Log(LogLevel.Verbose, data);
        public void LogWarning(string data) => Log(LogLevel.Warning, data);
    }
}