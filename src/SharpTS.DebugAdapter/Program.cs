using System.Text;
using System.Reflection;
using SharpTS.DebugAdapter.Adapter;
using SharpTS.DebugAdapter.Protocol;

if (args is ["--version"])
{
    string version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";
    Console.WriteLine(version);
    return 0;
}

string? logPath = null;
if (args.Length > 0)
{
    if (args is ["--log", var path] && !string.IsNullOrWhiteSpace(path))
        logPath = Path.GetFullPath(path);
    else
    {
        await Console.Error.WriteLineAsync("Usage: sharpts-dap [--version | --log <path>]");
        return 2;
    }
}

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

StreamWriter? fileLog = logPath is null
    ? null
    : new StreamWriter(new FileStream(
        logPath, FileMode.Append, FileAccess.Write, FileShare.Read,
        bufferSize: 4096, FileOptions.Asynchronous)) { AutoFlush = true };
TextWriter diagnosticLog = fileLog ?? Console.Error;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

try
{
    await using var connection = new DapProtocolConnection(
        Console.OpenStandardInput(), Console.OpenStandardOutput());
    await using var session = new DapAdapterSession(connection, diagnosticLog);
    await session.RunAsync(shutdown.Token);
    return 0;
}
catch (DapProtocolException exception)
{
    await diagnosticLog.WriteLineAsync($"sharpts-dap protocol error: {exception.Message}");
    return 2;
}
catch (Exception exception)
{
    await diagnosticLog.WriteLineAsync($"sharpts-dap failure: {exception}");
    return 1;
}
finally
{
    if (fileLog is not null)
        await fileLog.DisposeAsync();
}
