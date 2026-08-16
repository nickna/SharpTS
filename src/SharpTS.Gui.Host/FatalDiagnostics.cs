using System.Runtime.InteropServices;

namespace SharpTS.Gui.Host;

internal static class FatalDiagnostics
{
    private static readonly IPlatformFatalDiagnostics Platform = OperatingSystem.IsMacOS()
        ? new MacOsFatalDiagnostics()
        : new WindowsFatalDiagnostics();

    public static void Report(Exception exception, bool allowDialog)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            Console.Error.WriteLine(exception);
        }
        catch
        {
            // A Windows-subsystem application may not have inherited stderr.
        }

        string? logPath = Platform.TryWriteLog(exception);
        if (allowDialog)
            Platform.TryShowDialog(exception, logPath);
    }
}

internal interface IPlatformFatalDiagnostics
{
    string? TryWriteLog(Exception exception);
    void TryShowDialog(Exception exception, string? logPath);
}

internal sealed partial class WindowsFatalDiagnostics : IPlatformFatalDiagnostics
{
    private const uint MbOk = 0;
    private const uint MbIconError = 0x10;

    private readonly string? _defaultErrorDirectory;

    public WindowsFatalDiagnostics(string? defaultErrorDirectory = null)
    {
        _defaultErrorDirectory = defaultErrorDirectory;
    }

    public string? TryWriteLog(Exception exception)
        => PlatformDiagnosticLog.TryWrite(exception, _defaultErrorDirectory);

    public void TryShowDialog(Exception exception, string? logPath)
    {
        if (!OperatingSystem.IsWindows() || GetConsoleWindow() != 0)
            return;

        string detail = string.IsNullOrWhiteSpace(logPath)
            ? exception.Message
            : $"{exception.Message}{Environment.NewLine}{Environment.NewLine}Details were written to:{Environment.NewLine}{logPath}";
        _ = MessageBox(0, detail, "SharpTS GUI application error", MbOk | MbIconError);
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint window, string text, string caption, uint type);
}

internal sealed class MacOsFatalDiagnostics(string? defaultErrorDirectory = null) : IPlatformFatalDiagnostics
{
    public string? TryWriteLog(Exception exception)
        => PlatformDiagnosticLog.TryWrite(exception, defaultErrorDirectory);

    public void TryShowDialog(Exception exception, string? logPath)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            System.Diagnostics.Process.Start(CreateDialogStartInfo(exception, logPath))?.Dispose();
        }
        catch
        {
            // The durable error log remains available when the desktop session cannot show UI.
        }
    }

    internal static System.Diagnostics.ProcessStartInfo CreateDialogStartInfo(
        Exception exception,
        string? logPath)
    {
        string detail = string.IsNullOrWhiteSpace(logPath)
            ? exception.Message
            : $"{exception.Message}\n\nDetails were written to:\n{logPath}";
        const string script = "on run argv\n"
            + "display alert \"SharpTS GUI application error\" message (item 1 of argv) as critical\n"
            + "end run";
        var start = new System.Diagnostics.ProcessStartInfo("/usr/bin/osascript")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(detail);
        return start;
    }
}

internal static class PlatformDiagnosticLog
{
    public static string? TryWrite(Exception exception, string? defaultErrorDirectory)
    {
        try
        {
            string? configuredPath = Environment.GetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG");
            bool hostManaged = string.IsNullOrWhiteSpace(configuredPath);
            string directory = defaultErrorDirectory ?? HostDiagnosticPaths.ErrorDirectory;
            string path = hostManaged
                ? HostDiagnosticPaths.CreateErrorPath(directory, HostDiagnosticPaths.GetApplicationName())
                : Path.GetFullPath(configuredPath!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}");
            if (hostManaged)
            {
                HostDiagnosticPaths.Prune(
                    directory,
                    "sharpts-gui-error-*.log",
                    HostDiagnosticPaths.RetainedDefaultErrorCount);
            }
            return path;
        }
        catch
        {
            return null;
        }
    }
}
