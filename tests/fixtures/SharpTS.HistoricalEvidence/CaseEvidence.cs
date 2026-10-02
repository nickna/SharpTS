#pragma warning disable SHARPTS_HOSTING001

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using SharpTS.Execution;
using SharpTS.Hosting;
using SharpTS.Tests.Hosting;
using SharpTS.Tests.Infrastructure;

internal static class CaseEvidence
{
    public static void Collect(string sourceDirectory, string artifactDirectory)
    {
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        artifactDirectory = Path.GetFullPath(artifactDirectory);
        Directory.CreateDirectory(artifactDirectory);
        string cli = typeof(Interpreter).Assembly.Location;
        var cases = JsonSerializer.Deserialize<Case[]>(File.ReadAllText(Path.Combine(sourceDirectory, "cases.json")))!;
        var results = new List<object>();
        foreach (var item in cases)
        {
            string sourcePath = Path.Combine(sourceDirectory, item.File);
            string source = File.ReadAllText(sourcePath);
            string caseDirectory = Path.Combine(artifactDirectory, Path.GetFileNameWithoutExtension(item.File));
            Directory.CreateDirectory(caseDirectory);
            bool modules = Path.GetExtension(sourcePath) == ".cjs";
            var files = new Dictionary<string, string> { [item.File] = source };
            var observations = new List<object>
            {
                Observe("node-reference", () => RunProcess("node", sourcePath)),
                Observe("interpreted-api", () => modules
                    ? TestHarness.RunModules(files, item.File, ExecutionMode.Interpreted, TimeSpan.FromSeconds(30))
                    : TestHarness.RunInterpreted(source, TimeSpan.FromSeconds(30))),
                Observe("compiled-api", () => modules
                    ? TestHarness.RunModules(files, item.File, ExecutionMode.Compiled, TimeSpan.FromSeconds(30))
                    : TestHarness.RunCompiled(source, TimeSpan.FromSeconds(30))),
                Observe("interpreted-cli-default", () => RunProcess("dotnet", cli, "--no-tsconfig", sourcePath)),
                Observe("interpreted-cli-noLib", () => RunProcess("dotnet", cli, "--no-tsconfig", "--noLib", sourcePath)),
                Observe("interpreted-cli-esnext", () => RunProcess("dotnet", cli, "--no-tsconfig", "--lib", "esnext,dom", sourcePath)),
            };
            string defaultDll = Path.Combine(caseDirectory, "default.dll");
            ProcessResult defaultCompilation = RunProcess("dotnet", cli, "--no-tsconfig", "--compile", sourcePath,
                "-o", defaultDll, "--verify", "--standalone");
            observations.Add(new { mode = "default-cli-compilation", result = defaultCompilation });
            if (defaultCompilation.ExitCode == 0)
                observations.Add(Observe("default-standalone-execution", () => RunProcess("dotnet", defaultDll)));
            foreach (bool hosted in new[] { false, true })
            {
                string mode = hosted ? "hosted" : "standalone";
                string directory = Path.Combine(caseDirectory, mode);
                Directory.CreateDirectory(directory);
                string dll = Path.Combine(directory, "guest.dll");
                var arguments = new List<string> { cli, "--no-tsconfig", "--noLib", "--compile", sourcePath,
                    "-o", dll, "--verify", "--standalone" };
                if (hosted) arguments.AddRange(["--target", "dll", "--hosted"]);
                ProcessResult compilation = RunProcess("dotnet", arguments.ToArray());
                observations.Add(new { mode = mode + "-cli-compilation", result = compilation });
                if (compilation.ExitCode != 0) continue;
                using (var stream = File.OpenRead(dll))
                using (var pe = new PEReader(stream))
                {
                    var metadata = pe.GetMetadataReader();
                    observations.Add(new
                    {
                        mode = mode + "-metadata",
                        references = metadata.AssemblyReferences.Select(handle =>
                            metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray(),
                        sharpTsDllPresent = File.Exists(Path.Combine(directory, "SharpTS.dll")),
                    });
                }
                observations.Add(hosted
                    ? Observe("hosted-runtime-initialization", () => RunHosted(dll))
                    : Observe("standalone-execution", () => RunProcess("dotnet", dll)));
            }
            results.Add(new { item.File, item.Provenance, item.ExpectedStdout,
                sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourcePath))), observations });
            // Keep each completed case if a later collection fails.
            File.WriteAllText(Path.Combine(artifactDirectory, "results.json"), JsonSerializer.Serialize(new
            {
                baseline = RunProcess("git", "rev-parse", "HEAD").StandardOutput.Trim(),
                recordedAtUtc = DateTimeOffset.UtcNow,
                runtime = Environment.Version.ToString(), dotnet = RunProcess("dotnet", "--version"),
                node = RunProcess("node", "--version"), deadlineSeconds = 30, results,
            }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
    }

    private static object Observe(string mode, Func<object> action)
    {
        try { return new { mode, result = action() }; }
        catch (Exception error)
        {
            while (error is TargetInvocationException { InnerException: { } inner }) error = inner;
            return new { mode, exceptionType = error.GetType().FullName, error = error.ToString() };
        }
    }

    private static ProcessResult RunProcess(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start " + command);
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            Task.WhenAll(process.WaitForExitAsync(), output, error).WaitAsync(TimeSpan.FromSeconds(30))
                .GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return new(process.ExitCode, Normalize(output.Result), Normalize(error.Result));
    }

    private static string RunHosted(string dll)
    {
        var dispatcher = new DeterministicHostDispatcher();
        var sink = new RecordingErrorSink();
        var priorOut = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            using var runtime = SharpTSHostedAssembly.CreateRuntime(
                Assembly.Load(File.ReadAllBytes(dll)), dispatcher, new RecordingLifetime(), sink);
            Task initialization = runtime.InitializeAsync();
            dispatcher.RunUntil(() => initialization.IsCompleted, timeout: TimeSpan.FromSeconds(30));
            initialization.GetAwaiter().GetResult();
            if (sink.Errors.Count != 0) throw new InvalidOperationException(JsonSerializer.Serialize(sink.Errors));
            return Normalize(output.ToString());
        }
        finally { Console.SetOut(priorOut); }
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    private sealed record Case(string File, string Provenance, string ExpectedStdout);
}
