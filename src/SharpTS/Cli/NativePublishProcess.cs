using System.Diagnostics;

namespace SharpTS.Cli;

/// <summary>Owns one SDK process and drains both pipes, including cancellation cleanup.</summary>
internal static class NativePublishProcess
{
    internal sealed record Result(int ExitCode, string Output, string Error, IReadOnlyList<NativePublishDiagnostic> Diagnostics);

    internal static async Task<Result> RunAsync(ProcessStartInfo start, TextWriter output, TextWriter error,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        start.UseShellExecute = false; start.CreateNoWindow = true;
        start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        using var process = new Process { StartInfo = start };
        process.Start();
        var diagnostics = new List<NativePublishDiagnostic>();
        var gate = new object();
        async Task<string> Drain(StreamReader reader, TextWriter writer)
        {
            var text = new System.Text.StringBuilder();
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                text.AppendLine(line);
                lock (gate)
                {
                    writer.WriteLine(line); writer.Flush();
                    if (NativePublishDiagnostic.Parse(line) is { } diagnostic) diagnostics.Add(diagnostic);
                }
            }
            return text.ToString();
        }
        var stdout = Drain(process.StandardOutput, output);
        var stderr = Drain(process.StandardError, error);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            // A descendant retaining a pipe cannot hold the CLI indefinitely after SDK exit.
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), diagnostics);
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
            try { await Task.WhenAll(stdout, stderr).WaitAsync(cleanup.Token).ConfigureAwait(false); }
            catch
            {
                _ = Task.WhenAll(stdout, stderr).ContinueWith(static completed => { _ = completed.Exception; },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            throw;
        }
    }
}
