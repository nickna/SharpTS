using System.Collections;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace SharpTS.Gui;


internal static class DescriptorRegistry
{
    private static readonly Dictionary<string, NodeDescriptor> Descriptors =
        GeneratedControlContract.CreateDescriptors()
            .ToDictionary(descriptor => descriptor.Kind, StringComparer.Ordinal);

    public static NodeDescriptor? Get(string kind) =>
        kind is not null && Descriptors.TryGetValue(kind, out NodeDescriptor? descriptor)
            ? descriptor
            : null;

    internal static IDisposable RegisterForTesting(NodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!Descriptors.TryAdd(descriptor.Kind, descriptor))
            throw new InvalidOperationException($"A descriptor named '{descriptor.Kind}' is already registered.");
        return new TestRegistration(descriptor.Kind);
    }

    private sealed class TestRegistration(string kind) : IDisposable
    {
        public void Dispose() => Descriptors.Remove(kind);
    }
}

internal abstract class NodeDescriptor(string kind, int minimumChildren, int maximumChildren)
{
    public string Kind { get; } = kind;
    public int MinimumChildren { get; } = minimumChildren;
    public int MaximumChildren { get; } = maximumChildren;
    public virtual void Validate(GuiVNode node) { }
    public abstract Control Create(GuiVNode node);
    public abstract bool Update(Control control, GuiVNode previous, GuiVNode next);
}
