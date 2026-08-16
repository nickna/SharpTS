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


internal sealed record PreparedNode(
    GuiVNode VNode,
    NodeDescriptor Descriptor,
    IReadOnlyList<PreparedNode> Children);

internal static class VNodeValidator
{
    public static PreparedNode Prepare(GuiVNode root, bool requireWindowRoot)
    {
        ArgumentNullException.ThrowIfNull(root);
        PreparedNode prepared = PrepareCore(root, isRoot: true);
        if (requireWindowRoot && prepared.Descriptor.Kind != "Window")
            throw Error(root, "The desktop root must be a Window VNode.");
        return prepared;
    }

    private static PreparedNode PrepareCore(GuiVNode node, bool isRoot)
    {
        NodeDescriptor descriptor = DescriptorRegistry.Get(node.Kind)
            ?? throw Error(node, $"Unsupported desktop VNode kind '{node.Kind}'.");
        if (descriptor.Kind == "Window" && !isRoot)
            throw Error(node, "Window is only supported as the desktop root.");

        try
        {
            CommonProperties.Validate(node);
            descriptor.Validate(node);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw Error(node, exception.Message);
        }

        GuiVNode[] children = NormalizeChildren(node).ToArray();
        if (children.Length < descriptor.MinimumChildren || children.Length > descriptor.MaximumChildren)
        {
            string expected = descriptor.MaximumChildren == 0
                ? "no children"
                : descriptor.MaximumChildren == 1
                    ? "at most one child"
                    : "children within its supported cardinality";
            throw Error(node, $"{descriptor.Kind} accepts {expected}; received {children.Length}.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (GuiVNode child in children)
            if (child.Key is not null && !keys.Add(child.Key))
                throw Error(child, $"Duplicate sibling key '{child.Key}'.");

        return new PreparedNode(
            node,
            descriptor,
            children.Select(child => PrepareCore(child, isRoot: false)).ToArray());
    }

    private static IEnumerable<GuiVNode> NormalizeChildren(GuiVNode node)
    {
        object? children = node.Children;
        if (children is null)
            yield break;
        if (children is GuiVNode childNode)
        {
            yield return childNode;
            yield break;
        }
        if (children is IEnumerable enumerable and not string)
        {
            int index = 0;
            foreach (object? child in enumerable)
            {
                if (child is not GuiVNode vnode)
                {
                    throw Error(
                        node,
                        $"Unsupported child at index {index}; primitive text and non-VNode children are not supported.");
                }
                yield return vnode;
                index++;
            }
            yield break;
        }
        throw Error(
            node,
            $"Unsupported child container '{children.GetType().FullName}'; primitive text children are not supported.");
    }

    private static InvalidOperationException Error(GuiVNode node, string message)
    {
        string location = string.IsNullOrWhiteSpace(node.SourceFile)
            ? string.Empty
            : $" ({node.SourceFile}:{node.SourceLine}:{node.SourceColumn})";
        return new InvalidOperationException(message + location);
    }
}
