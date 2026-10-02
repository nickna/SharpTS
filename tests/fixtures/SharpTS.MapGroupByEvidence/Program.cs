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

if (args.Length != 2)
    throw new ArgumentException("Usage: <source directory> <artifact directory>");

string sourceDirectory = Path.GetFullPath(args[0]);
string artifactDirectory = Path.GetFullPath(args[1]);
Directory.CreateDirectory(artifactDirectory);
string cli = typeof(Interpreter).Assembly.Location;
var results = new List<object>();

foreach (string name in new[] { "direct", "constructor-alias", "method-alias" })
{
    string sourcePath = Path.Combine(sourceDirectory, name + ".ts");
    string source = File.ReadAllText(sourcePath);
    string caseDirectory = Path.Combine(artifactDirectory, name);
    Directory.CreateDirectory(caseDirectory);
    string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourcePath)));
    var observations = new List<object>
    {
        Observe("node-reference", () => RunProcess("node", sourcePath)),
        Observe("interpreted-api", () => TestHarness.RunInterpreted(source, TimeSpan.FromSeconds(30))),
        Observe("compiled-api", () => TestHarness.RunCompiled(source, TimeSpan.FromSeconds(30))),
        Observe("interpreted-cli", () => RunProcess("dotnet", cli, "--no-tsconfig", sourcePath)),
    };

    foreach (bool hosted in new[] { false, true })
    {
        string dll = Path.Combine(caseDirectory, hosted ? "hosted.dll" : "standalone.dll");
        var arguments = new List<string>
        {
            cli, "--no-tsconfig", "--compile", sourcePath, "-o", dll, "--verify", "--standalone",
        };
        if (hosted) arguments.AddRange(["--target", "dll", "--hosted"]);
        string mode = hosted ? "hosted" : "standalone";
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
                references = metadata.AssemblyReferences
                    .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray(),
                sharpTsDllPresent = File.Exists(Path.Combine(caseDirectory, "SharpTS.dll")),
            });
        }
        observations.Add(hosted
            ? Observe("hosted-runtime-initialization", () => RunHosted(dll))
            : Observe("standalone-execution", () => RunProcess("dotnet", dll)));
    }
    results.Add(new { name, sourceSha256 = hash, expectedStdout = "2\n", observations });
}

var report = new
{
    issue = 1911,
    baseline = RunProcess("git", "rev-parse", "HEAD").StandardOutput.Trim(),
    recordedAtUtc = DateTimeOffset.UtcNow,
    dotnet = RunProcess("dotnet", "--version"),
    node = RunProcess("node", "--version"),
    deadlineSeconds = 30,
    results,
};
string reportPath = Path.Combine(artifactDirectory, "results.json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(reportPath);

// Evidence collection records failures instead of turning them into accepted expectations.
static object Observe(string mode, Func<object> action)
{
    try { return new { mode, result = action() }; }
    catch (Exception error)
    {
        while (error is TargetInvocationException { InnerException: { } inner }) error = inner;
        return new { mode, exceptionType = error.GetType().FullName, error = error.ToString() };
    }
}

static ProcessResult RunProcess(string command, params string[] arguments)
{
    var start = new ProcessStartInfo(command)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start " + command);
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

static string RunHosted(string dll)
{
    var dispatcher = new DeterministicHostDispatcher();
    var sink = new RecordingErrorSink();
    var originalOutput = Console.Out;
    using var output = new StringWriter();
    Console.SetOut(output);
    try
    {
        using var runtime = SharpTSHostedAssembly.CreateRuntime(
            Assembly.Load(File.ReadAllBytes(dll)), dispatcher, new RecordingLifetime(), sink);
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted, timeout: TimeSpan.FromSeconds(30));
        initialization.GetAwaiter().GetResult();
        if (sink.Errors.Count != 0)
            throw new InvalidOperationException(JsonSerializer.Serialize(sink.Errors));
        return Normalize(output.ToString());
    }
    finally { Console.SetOut(originalOutput); }
}

static string Normalize(string value) => value.Replace("\r\n", "\n");

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
