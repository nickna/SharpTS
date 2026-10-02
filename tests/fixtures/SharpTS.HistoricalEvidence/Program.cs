using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.Tests.Compilation;

if (args.Length != 2)
    throw new ArgumentException("Usage: console|timing <report path>");
if (args[0] == "timing")
{
    TimingEvidence.Collect(args[1]);
    return;
}
if (args[0] != "console") throw new ArgumentException("Unknown evidence mode.");

var tests = new CompilationServiceTests(); // Runs the original test module initializer.
var observations = new List<object>();
for (int invocation = 1; invocation <= 2; invocation++)
{
    var priorOut = Console.Out;
    var priorErr = Console.Error;
    string? failure = null;
    try { tests.Execute_RestoresConsoleAfterRun(); }
    catch (Exception error) { failure = error.ToString(); }
    observations.Add(new
    {
        invocation, failure,
        originalOutType = priorOut.GetType().FullName,
        restoredOutType = Console.Out.GetType().FullName,
        originalErrType = priorErr.GetType().FullName,
        restoredErrType = Console.Error.GetType().FullName,
        sameOut = ReferenceEquals(priorOut, Console.Out),
        sameErr = ReferenceEquals(priorErr, Console.Error),
    });
}

// Ordinary public Console writers are already synchronized. Check restoration,
// real output, and repeatability without modifying the original assertions.
using var productOut = new StringWriter();
using var productErr = new StringWriter();
Console.SetOut(productOut);
Console.SetError(productErr);
for (int invocation = 1; invocation <= 2; invocation++)
{
    var priorOut = Console.Out;
    var priorErr = Console.Error;
    var compile = CompilationService.Compile("console.log(\"x\");");
    using var output = new StringWriter();
    var run = CompilationService.Execute(compile.AssemblyBytes!, output);
    observations.Add(new
    {
        control = "public-console-writers", invocation, compile.Success,
        runSuccess = run.Success, run.Error, output = output.ToString().Replace("\r\n", "\n"),
        sameOut = ReferenceEquals(priorOut, Console.Out),
        sameErr = ReferenceEquals(priorErr, Console.Error),
    });
}

var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
start.ArgumentList.Add("rev-parse");
start.ArgumentList.Add("HEAD");
using var git = Process.Start(start)!;
string baseline = git.StandardOutput.ReadToEnd().Trim();
git.WaitForExit();
string path = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path, JsonSerializer.Serialize(new
{
    baseline, recordedAtUtc = DateTimeOffset.UtcNow,
    runtime = Environment.Version.ToString(), observations,
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
