using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required receiver-aware Reflect read declarations and independently selected capabilities for one compilation.</summary>
public sealed class EmittedReflectRuntime
{
    internal EmittedReflectRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _get;
    public MethodBuilder Get
    {
        get => Require(_get);
        internal set => SetHandle(ref _get, value);
    }

    public EmittedReflectAssignment? Assignment { get; private set; }

    public EmittedReflectAssignment RequireAssignment() => Assignment
        ?? throw new InvalidOperationException("Reflect assignment was not enabled for this compilation.");

    internal void BeginAssignmentEmission()
    {
        EnsureMutable();
        if (Assignment is not null)
            throw new InvalidOperationException("Reflect assignment emission has already started.");
        Assignment = new EmittedReflectAssignment();
    }

    public EmittedReflectNamespace? Namespace { get; private set; }

    public EmittedReflectNamespace RequireNamespace() => Namespace
        ?? throw new InvalidOperationException("Reflect namespace was not enabled for this compilation.");

    internal void BeginNamespaceEmission()
    {
        EnsureMutable();
        if (Namespace is not null)
            throw new InvalidOperationException("Reflect namespace emission has already started.");
        Namespace = new EmittedReflectNamespace();
    }

    public EmittedReflectMetadata? Metadata { get; private set; }

    public EmittedReflectMetadata RequireMetadata() => Metadata
        ?? throw new InvalidOperationException("Reflect metadata was not enabled for this compilation.");

    internal void BeginMetadataEmission()
    {
        EnsureMutable();
        if (Metadata is not null)
            throw new InvalidOperationException("Reflect metadata emission has already started.");
        Metadata = new EmittedReflectMetadata();
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Reflect required metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Reflect required metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Get;
        if (Namespace is not null)
            _ = RequireAssignment();

        // Validate every selected capability before freezing any of them, so a
        // missing late decorator or namespace wrapper can still be repaired.
        Assignment?.ValidateEmission();
        Namespace?.ValidateEmission();
        Metadata?.ValidateEmission();
        Assignment?.CompleteEmission();
        Namespace?.CompleteEmission();
        Metadata?.CompleteEmission();
        IsComplete = true;
    }
}
