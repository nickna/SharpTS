using System.Diagnostics;
using SharpTS.HostedInitializationFixture;
using SharpTS.Testing;
using SharpTS.Tests.IntegrationTests;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    public static IEnumerable<object[]> MapGroupByDeploymentPrograms =>
        from program in GroupByTests.MapAliasPrograms
        from hosted in new[] { false, true }
        select new object[] { program[0], program[1], hosted };

    [Theory]
    [MemberData(nameof(MapGroupByDeploymentPrograms))]
    public void Isolated_Issue1928MapGroupBy_ExecutesStandaloneAndHosted(string name, string source, bool hosted)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath(name + ".dll");
        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{path}\" -o \"{output}\" --standalone --verify" +
            (hosted ? " --target dll --hosted" : ""), directory.Path, TimeSpan.FromSeconds(30));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Empty(compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(output);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.All(references, reference => Assert.True(reference.StartsWith("System", StringComparison.Ordinal) ||
            reference is "mscorlib" or "SharpTS.Hosting.Abstractions", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));

        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory.Path };
        if (hosted)
        {
            // Copy only the host and its ABI dependency into the guest's isolated directory.
            var fixture = typeof(HostedInitializationFixtureMarker).Assembly.Location;
            foreach (string extension in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
            {
                var file = Path.ChangeExtension(fixture, extension);
                File.Copy(file, directory.GetPath(Path.GetFileName(file)));
            }
            var abi = Path.Combine(Path.GetDirectoryName(fixture)!, "SharpTS.Hosting.Abstractions.dll");
            File.Copy(abi, directory.GetPath(Path.GetFileName(abi)), overwrite: true);
            start.ArgumentList.Add(directory.GetPath(Path.GetFileName(fixture)));
        }
        start.ArgumentList.Add(output);
        var result = TestProcess.Run(start, TimeSpan.FromSeconds(30), $"Map.groupBy {name}, hosted={hosted}");
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Empty(result.StandardError);
        Assert.Equal("2\n", result.StandardOutput.Replace("\r\n", "\n"));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
    }
}
