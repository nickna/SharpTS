using System.Diagnostics;
using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Collection fixture for third-party assembly-reference tests (issue #1197): builds,
/// ONCE per test run, a small classlib DLL and two local NuGet packages (Main → Base
/// dependency) that tests reference from sharpts.json manifests / -r flags. Uses
/// `dotnet build`/`dotnet pack` — the suite already requires the SDK for its
/// subprocess runs.
/// </summary>
public sealed class ExternalAssemblyFixture : IDisposable
{
    public const string GreeterTypeName = "SharpTsFixtures.External.Greeter";
    public const string GreeterAssemblyName = "SharpTsExternalFixture";
    public const string MainPackageId = "TestPkg.Main";
    public const string BasePackageId = "TestPkg.Base";
    public const string PackageVersion = "1.0.0";
    public const string MainTypeName = "TestPkg.Main.MainInfo";

    private readonly string _root;

    /// <summary>Path to the built classlib DLL (SharpTsExternalFixture.dll).</summary>
    public string GreeterDllPath { get; }

    /// <summary>Folder containing TestPkg.Main/TestPkg.Base .nupkg files (a NuGet folder source).</summary>
    public string PackageSourceDir { get; }

    public ExternalAssemblyFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), $"sharpts_extasm_fixture_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        PackageSourceDir = Path.Combine(_root, "pkgsource");
        Directory.CreateDirectory(PackageSourceDir);

        // Classlib referenced via "references" / -r.
        string libDir = WriteProject("greeter", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{GreeterAssemblyName}</AssemblyName>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """,
            ("Greeter.cs", """
            namespace SharpTsFixtures.External;

            public class Greeter
            {
                public static string Hello(string name) => $"Hello, {name}!";
                public double Add(double a, double b) => a + b;
            }
            """));
        RunDotnet(libDir, "build -c Release --nologo -v q");
        GreeterDllPath = Path.Combine(libDir, "bin", "Release", "net10.0", $"{GreeterAssemblyName}.dll");
        if (!File.Exists(GreeterDllPath))
            throw new InvalidOperationException($"Fixture build produced no DLL at {GreeterDllPath}");

        // Two packages, Main depending on Base — exercises transitive restore + closure copy.
        string baseDir = WriteProject("pkgbase", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{BasePackageId}</AssemblyName>
                <PackageId>{BasePackageId}</PackageId>
                <Version>{PackageVersion}</Version>
              </PropertyGroup>
            </Project>
            """,
            ("BaseInfo.cs", """
            namespace TestPkg.Base;
            public class BaseInfo
            {
                public static string Version() => "base-1.0";
            }
            """));
        string mainDir = WriteProject("pkgmain", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{MainPackageId}</AssemblyName>
                <PackageId>{MainPackageId}</PackageId>
                <Version>{PackageVersion}</Version>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../pkgbase/pkgbase.csproj" />
              </ItemGroup>
            </Project>
            """,
            ("MainInfo.cs", """
            namespace TestPkg.Main;
            public class MainInfo
            {
                public static string Describe() => $"main-1.0 on {TestPkg.Base.BaseInfo.Version()}";
            }
            """));
        RunDotnet(baseDir, $"pack -c Release --nologo -v q -o \"{PackageSourceDir}\"");
        RunDotnet(mainDir, $"pack -c Release --nologo -v q -o \"{PackageSourceDir}\"");
    }

    /// <summary>
    /// Writes a nuget.config into <paramref name="dir"/> that sees ONLY the fixture's
    /// folder source and redirects the global packages folder into the test directory —
    /// restore tests touch neither the network nor the user's package cache.
    /// </summary>
    public void WriteHermeticNuGetConfig(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="fixture" value="{PackageSourceDir}" />
              </packageSources>
              <config>
                <add key="globalPackagesFolder" value="./pkgcache" />
              </config>
            </configuration>
            """);
    }

    private string WriteProject(string name, string csproj, params (string Name, string Content)[] sources)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), csproj);
        // Isolate from any ambient MSBuild configuration above the temp root.
        File.WriteAllText(Path.Combine(dir, "Directory.Build.props"), "<Project></Project>");
        File.WriteAllText(Path.Combine(dir, "Directory.Build.targets"), "<Project></Project>");
        File.WriteAllText(Path.Combine(dir, "Directory.Packages.props"), "<Project></Project>");
        foreach (var (sourceName, content) in sources)
            File.WriteAllText(Path.Combine(dir, sourceName), content);
        return dir;
    }

    private static void RunDotnet(string workingDirectory, string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"'dotnet {arguments}' timed out in {workingDirectory}");
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'dotnet {arguments}' failed ({process.ExitCode}) in {workingDirectory}:\n{stdout}\n{stderr}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

[CollectionDefinition("ExternalAssembly")]
public class ExternalAssemblyCollection : ICollectionFixture<ExternalAssemblyFixture>;
