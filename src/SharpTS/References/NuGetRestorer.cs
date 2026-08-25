using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using SharpTS.Runtime;

namespace SharpTS.References;

/// <summary>One runtime-asset DLL from a restored package.</summary>
internal sealed record PackageAsset(string Path, string PackageId);

/// <summary>
/// Result of a package restore: every runtime DLL in the resolved graph, plus the
/// per-package transitive closure used by the compiled-output copy step.
/// </summary>
internal sealed record RestoreResult(
    IReadOnlyList<PackageAsset> RuntimeAssets,
    Dictionary<string, IReadOnlyList<string>> PackageClosures);

/// <summary>
/// Restores sharpts.json "packages" by shelling out to <c>dotnet restore</c> on a
/// generated minimal project under <c>.sharpts/</c> next to the manifest, then
/// reading the resulting <c>project.assets.json</c>. Deliberately NOT the NuGet
/// client libraries: programmatic restore would add NuGet.Commands/ProjectModel/
/// DependencyResolver to SharpTS.dll's dependency closure (which gets co-located
/// next to compiled output), and the CLI honors nuget.config discovery, credential
/// providers, and offline cache behavior for free.
/// </summary>
internal static class NuGetRestorer
{
    /// <summary>Target framework for the restore graph; matches the SharpTS runtime.</summary>
    private const string TargetFramework = "net10.0";

    public static RestoreResult Restore(SharpTsManifest manifest)
    {
        string restoreDir = Path.Combine(manifest.ManifestDirectory, ".sharpts");
        string projectPath = Path.Combine(restoreDir, "restore.csproj");
        string assetsPath = Path.Combine(restoreDir, "obj", "project.assets.json");
        string hashPath = Path.Combine(restoreDir, "restore.hash");

        string hash = ComputeHash(manifest.Packages!);

        // Hash gate: when the package set hasn't changed since the last successful
        // restore, skip the dotnet invocation entirely (fast startup, works offline).
        bool upToDate = File.Exists(assetsPath) && File.Exists(hashPath) &&
                        File.ReadAllText(hashPath).Trim() == hash;

        if (!upToDate)
        {
            RunRestore(manifest, restoreDir, projectPath);
            if (!File.Exists(assetsPath))
            {
                throw new Exception(
                    $"Error: NuGet restore for sharpts.json ('{manifest.ManifestPath}') completed but " +
                    $"produced no assets file at '{assetsPath}'.");
            }
            File.WriteAllText(hashPath, hash);
        }

        return ProjectAssetsReader.Read(assetsPath, manifest.ManifestPath, TargetFramework);
    }

    private static string ComputeHash(Dictionary<string, string> packages)
    {
        var lines = packages
            .Select(kv => $"{kv.Key}@{kv.Value}")
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(TargetFramework + "\n" + string.Join("\n", lines)));
        return Convert.ToHexString(digest);
    }

    private static void RunRestore(SharpTsManifest manifest, string restoreDir, string projectPath)
    {
        Directory.CreateDirectory(restoreDir);
        WriteRestoreProject(manifest, projectPath);

        // Isolation stubs: without these, MSBuild walks up from .sharpts/ and imports
        // the host repo's Directory.Build.props / Directory.Packages.props — Central
        // Package Management there would fail our versioned PackageReferences (NU1008).
        // nuget.config is intentionally NOT stubbed: discovery from the manifest
        // directory is a feature (custom sources, hermetic tests).
        const string emptyProject = "<Project></Project>";
        File.WriteAllText(Path.Combine(restoreDir, "Directory.Build.props"), emptyProject);
        File.WriteAllText(Path.Combine(restoreDir, "Directory.Build.targets"), emptyProject);
        File.WriteAllText(Path.Combine(restoreDir, "Directory.Packages.props"), emptyProject);

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = manifest.ManifestDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("restore");
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add("--nologo");

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new Exception("Error: failed to start 'dotnet restore'.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new Exception(
                "Error: the 'dotnet' CLI was not found on PATH; it is required to restore " +
                $"sharpts.json packages ('{manifest.ManifestPath}').");
        }

        using var processLifetime = process;
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
        {
            ProcessTreeTermination.Terminate(process);
            throw new Exception(
                $"Error: NuGet restore for sharpts.json ('{manifest.ManifestPath}') timed out after 10 minutes.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            string output = Tail(string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + "\n" + stderr, 25);
            throw new Exception(
                $"Error: NuGet restore failed for sharpts.json packages ('{manifest.ManifestPath}'):\n{output}");
        }
    }

    private static void WriteRestoreProject(SharpTsManifest manifest, string projectPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <TargetFramework>{TargetFramework}</TargetFramework>");
        sb.AppendLine("    <EnableDefaultItems>false</EnableDefaultItems>");
        sb.AppendLine("    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>");
        // Only the package graph matters — no compilation happens against this project.
        // Without this, restore may try to download Microsoft.*.App.Ref targeting packs
        // (when not installed with the SDK), breaking offline and custom-source setups.
        sb.AppendLine("    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");
        foreach (var (id, version) in manifest.Packages!.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"    <PackageReference Include=\"{SecurityElement.Escape(id)}\" Version=\"{SecurityElement.Escape(version)}\" />");
        }
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("</Project>");
        File.WriteAllText(projectPath, sb.ToString());
    }

    private static string Tail(string text, int maxLines)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        return string.Join("\n", lines.TakeLast(maxLines));
    }
}
