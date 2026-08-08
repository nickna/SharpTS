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
    private static readonly IReadOnlyDictionary<string, NodeDescriptor> Descriptors =
        new NodeDescriptor[]
        {
            new WindowDescriptor(),
            new StackPanelDescriptor("StackPanel"),
            new StackPanelDescriptor("ToolBar"),
            new BorderDescriptor("StatusBar"),
            new WrapPanelDescriptor(),
            new DockPanelDescriptor(),
            new GridDescriptor(),
            new BorderDescriptor(),
            new ScrollViewerDescriptor(),
            new TextBlockDescriptor(),
            new ButtonDescriptor(),
            new TextBoxDescriptor(),
            new TextBoxDescriptor("PasswordBox"),
            new CheckBoxDescriptor(),
            new CheckBoxDescriptor("RadioButton"),
            new CheckBoxDescriptor("ToggleSwitch"),
            new ComboBoxDescriptor(),
            new SliderDescriptor(),
            new ProgressBarDescriptor(),
            new SeparatorDescriptor(),
            new ListBoxDescriptor(),
            new NumericUpDownDescriptor(),
            new DatePickerDescriptor(),
            new TimePickerDescriptor(),
            new ImageDescriptor(),
            new TabControlDescriptor(),
            new TabItemDescriptor(),
            new MenuDescriptor(),
            new MenuItemDescriptor(),
            new StackPanelDescriptor("Fragment", updateLayout: false),
        }.ToDictionary(descriptor => descriptor.Kind, StringComparer.Ordinal);

    public static NodeDescriptor? Get(string kind) =>
        kind is not null && Descriptors.TryGetValue(kind, out NodeDescriptor? descriptor)
            ? descriptor
            : null;
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
