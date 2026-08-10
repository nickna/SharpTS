using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace SharpTS.Sdk.Tasks;

public sealed class PrepareGuiAssetsTask : Microsoft.Build.Utilities.Task
{
    public ITaskItem[] LocalAssets { get; set; } = [];
    public ITaskItem[] RemoteAssets { get; set; } = [];

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    public long MaximumRemoteAssetBytes { get; set; } = 25 * 1024 * 1024;

    [Output]
    public ITaskItem[] PreparedAssets { get; private set; } = [];

    public override bool Execute()
    {
        try
        {
            string project = Path.GetFullPath(ProjectDirectory);
            string output = Path.GetFullPath(OutputDirectory);
            Directory.CreateDirectory(output);
            var prepared = new List<ITaskItem>();

            foreach (ITaskItem item in LocalAssets)
            {
                string path = Path.GetFullPath(item.ItemSpec, project);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"SharpTS GUI asset does not exist: {path}");
                prepared.Add(Prepared(path, LogicalName(item, project, path)));
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            foreach (ITaskItem item in RemoteAssets)
            {
                if (!Uri.TryCreate(item.ItemSpec, UriKind.Absolute, out Uri? uri) ||
                    uri.Scheme is not ("http" or "https"))
                    throw new InvalidOperationException($"Remote GUI asset must be an HTTP(S) URL: {item.ItemSpec}");
                string logicalName = RequiredMetadata(item, "LogicalName");
                string expectedHash = RequiredMetadata(item, "Sha256").Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                if (expectedHash.Length != 64 || expectedHash.Any(character => !Uri.IsHexDigit(character)))
                    throw new InvalidOperationException($"Remote GUI asset '{item.ItemSpec}' has an invalid Sha256 value.");
                string target = SafeTarget(output, logicalName);
                if (!File.Exists(target) || !string.Equals(Hash(File.ReadAllBytes(target)), expectedHash, StringComparison.Ordinal))
                {
                    byte[] bytes = client.GetByteArrayAsync(uri).GetAwaiter().GetResult();
                    if (bytes.LongLength > MaximumRemoteAssetBytes)
                        throw new InvalidOperationException($"Remote GUI asset '{item.ItemSpec}' exceeds {MaximumRemoteAssetBytes} bytes.");
                    string actualHash = Hash(bytes);
                    if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Remote GUI asset '{item.ItemSpec}' failed SHA-256 validation. Expected {expectedHash}; got {actualHash}.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, bytes);
                }
                prepared.Add(Prepared(target, Normalize(logicalName)));
            }

            string? duplicate = prepared.GroupBy(item => item.GetMetadata("LogicalName"), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicate is not null)
                throw new InvalidOperationException($"Duplicate GUI asset logical name: {duplicate}");

            PreparedAssets = prepared.ToArray();
            return true;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
    }

    private static ITaskItem Prepared(string path, string logicalName)
    {
        var item = new TaskItem(path);
        item.SetMetadata("LogicalName", logicalName);
        return item;
    }

    private static string LogicalName(ITaskItem item, string project, string path)
    {
        string configured = item.GetMetadata("LogicalName");
        if (!string.IsNullOrWhiteSpace(configured))
            return SafeLogicalName(configured);
        string relative = Normalize(Path.GetRelativePath(project, path));
        return SafeLogicalName(relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? relative[7..] : relative);
    }

    private static string RequiredMetadata(ITaskItem item, string name)
    {
        string value = item.GetMetadata(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Remote GUI asset '{item.ItemSpec}' requires {name} metadata.")
            : value;
    }

    private static string SafeTarget(string root, string logicalName)
    {
        string normalized = SafeLogicalName(logicalName);
        string target = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"GUI asset logical name escapes the asset directory: {logicalName}");
        return target;
    }

    private static string SafeLogicalName(string logicalName)
    {
        string normalized = Normalize(logicalName);
        if (Path.IsPathRooted(logicalName) || normalized.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidOperationException($"GUI asset logical name is unsafe: {logicalName}");
        return normalized;
    }

    private static string Normalize(string value) => value.Replace('\\', '/').TrimStart('/');
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
