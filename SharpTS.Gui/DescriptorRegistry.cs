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
    private static readonly object Sync = new();
    private static readonly Dictionary<string, NodeDescriptor> Descriptors =
        GeneratedControlContract.CreateDescriptors()
            .ToDictionary(descriptor => descriptor.Kind, StringComparer.Ordinal);
    private static readonly HashSet<string> ProviderIds = new(StringComparer.Ordinal);

    public static NodeDescriptor? Get(string kind)
    {
        lock (Sync)
            return kind is not null && Descriptors.TryGetValue(kind, out NodeDescriptor? descriptor)
                ? descriptor
                : null;
    }

    internal static IDisposable RegisterProvider(IGuiControlProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        string providerId = provider.ProviderId;
        ValidateProviderId(providerId);
        if (provider.ContractVersion != DesktopBridge.CustomControlProviderApiVersion)
            throw new InvalidOperationException(
                $"GUI control provider '{providerId}' uses contract version {provider.ContractVersion}; " +
                $"the host requires {DesktopBridge.CustomControlProviderApiVersion}.");
        IReadOnlyList<NodeDescriptor> descriptors = provider.Descriptors
            ?? throw new InvalidOperationException($"GUI control provider '{providerId}' returned a null descriptor list.");
        if (descriptors.Count == 0)
            throw new InvalidOperationException($"GUI control provider '{providerId}' must declare at least one descriptor.");

        string prefix = providerId + ".";
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (NodeDescriptor? descriptor in descriptors)
        {
            if (descriptor is null)
                throw new InvalidOperationException($"GUI control provider '{providerId}' contains a null descriptor.");
            if (string.IsNullOrWhiteSpace(descriptor.Kind))
                throw new InvalidOperationException($"GUI control provider '{providerId}' contains an unnamed descriptor.");
            if (!descriptor.Kind.StartsWith(prefix, StringComparison.Ordinal) || descriptor.Kind.Length == prefix.Length)
                throw new InvalidOperationException(
                    $"Custom control kind '{descriptor.Kind}' must use the provider namespace '{prefix}'.");
            if (!kinds.Add(descriptor.Kind))
                throw new InvalidOperationException(
                    $"GUI control provider '{providerId}' declares duplicate kind '{descriptor.Kind}'.");
            if (descriptor.MinimumChildren < 0 || descriptor.MaximumChildren < descriptor.MinimumChildren)
                throw new InvalidOperationException(
                    $"Custom control kind '{descriptor.Kind}' has invalid child cardinality.");
        }

        lock (Sync)
        {
            if (!ProviderIds.Add(providerId))
                throw new InvalidOperationException($"GUI control provider '{providerId}' is already registered.");
            string? collision = kinds.FirstOrDefault(Descriptors.ContainsKey);
            if (collision is not null)
            {
                ProviderIds.Remove(providerId);
                throw new InvalidOperationException($"A descriptor named '{collision}' is already registered.");
            }
            foreach (NodeDescriptor descriptor in descriptors)
                Descriptors.Add(descriptor.Kind, descriptor);
        }
        return new ProviderRegistration(providerId, kinds.ToArray());
    }

    internal static IDisposable RegisterForTesting(NodeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (Sync)
            if (!Descriptors.TryAdd(descriptor.Kind, descriptor))
                throw new InvalidOperationException($"A descriptor named '{descriptor.Kind}' is already registered.");
        return new TestRegistration(descriptor.Kind);
    }

    private static void ValidateProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 80 ||
            providerId[0] is < 'a' or > 'z' || providerId[^1] is '.' or '-' ||
            providerId.Any(character => character is not (>= 'a' and <= 'z') and
                not (>= '0' and <= '9') and not '.' and not '-'))
        {
            throw new InvalidOperationException(
                "GUI control provider IDs must be lowercase dot/dash identifiers beginning with a letter.");
        }
    }

    private sealed class ProviderRegistration(string providerId, string[] kinds) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            lock (Sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                foreach (string kind in kinds)
                    Descriptors.Remove(kind);
                ProviderIds.Remove(providerId);
            }
        }
    }

    private sealed class TestRegistration(string kind) : IDisposable
    {
        public void Dispose()
        {
            lock (Sync)
                Descriptors.Remove(kind);
        }
    }
}

/// <summary>
/// Explicit, reflection-free managed adapter for one custom or built-in GUI control kind.
/// Custom kinds must be prefixed with their provider ID followed by a dot.
/// </summary>
public abstract class NodeDescriptor(string kind, int minimumChildren, int maximumChildren)
{
    public string Kind { get; } = kind;
    public int MinimumChildren { get; } = minimumChildren;
    public int MaximumChildren { get; } = maximumChildren;
    public virtual void Validate(GuiVNode node) { }
    public abstract Control Create(GuiVNode node);
    public abstract bool Update(Control control, GuiVNode previous, GuiVNode next);
}

/// <summary>A statically registered set of managed GUI control descriptors.</summary>
public interface IGuiControlProvider
{
    int ContractVersion { get; }
    string ProviderId { get; }
    IReadOnlyList<NodeDescriptor> Descriptors { get; }
}
