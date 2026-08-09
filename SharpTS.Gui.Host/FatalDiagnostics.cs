using System.Runtime.InteropServices;

namespace SharpTS.Gui.Host;

internal static class FatalDiagnostics
{
    private static readonly WindowsFatalDiagnostics Platform = new();

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
internal sealed partial class WindowsFatalDiagnostics
{
    private const uint MbOk = 0;
    private const uint MbIconError = 0x10;

    private readonly string? _defaultErrorDirectory;

    public WindowsFatalDiagnostics(string? defaultErrorDirectory = null)
    {
        _defaultErrorDirectory = defaultErrorDirectory;
    }

    public string? TryWriteLog(Exception exception)
    {
        try
        {
            string? configuredPath = Environment.GetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG");
            bool hostManaged = string.IsNullOrWhiteSpace(configuredPath);
            string directory = _defaultErrorDirectory ?? HostDiagnosticPaths.ErrorDirectory;
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
