namespace SharpTS.Gui;

/// <summary>
/// Returned to the GUI runtime only after a failed native commit has been restored completely.
/// Application event and asynchronous errors never use this path.
/// </summary>
public sealed class RecoverableNativeCommitException : Exception
{
    internal RecoverableNativeCommitException(NativeCommitException failure)
        : base($"[SharpTSRecoverableCommit:{failure.BoundaryPath}] {failure.Message}", failure.InnerException)
    {
        BoundaryPath = failure.BoundaryPath!;
        SourceFile = failure.SourceFile;
        SourceLine = failure.SourceLine;
        SourceColumn = failure.SourceColumn;
        Operation = failure.Operation;
    }

    public string BoundaryPath { get; }
    public string? SourceFile { get; }
    public int SourceLine { get; }
    public int SourceColumn { get; }
    public string Operation { get; }
}

internal sealed class NativeCommitException : InvalidOperationException
{
    private NativeCommitException(Exception inner, GuiVNode node, string operation)
        : base(Format(inner, node, operation), inner)
    {
        BoundaryPath = DesktopBridge.GetBoundaryPath(node);
        SourceFile = node.SourceFile;
        SourceLine = node.SourceLine;
        SourceColumn = node.SourceColumn;
        Operation = operation;
    }

    internal string? BoundaryPath { get; }
    internal string? SourceFile { get; }
    internal int SourceLine { get; }
    internal int SourceColumn { get; }
    internal string Operation { get; }

    internal static NativeCommitException Wrap(Exception error, GuiVNode node, string operation) =>
        error as NativeCommitException ?? new NativeCommitException(error, node, operation);

    private static string Format(Exception error, GuiVNode node, string operation)
    {
        string source = string.IsNullOrWhiteSpace(node.SourceFile)
            ? string.Empty
            : $" at {node.SourceFile}:{node.SourceLine}:{node.SourceColumn}";
        return $"Native GUI {operation} failed for {node.Kind}{source}: {error.Message}";
    }
}

internal sealed class NativeSetterRecoveryException(Exception commitError, Exception recoveryError)
    : Exception("Native GUI setter and its immediate recovery both failed.", commitError)
{
    internal Exception CommitError { get; } = commitError;
    internal Exception RecoveryError { get; } = recoveryError;
}
