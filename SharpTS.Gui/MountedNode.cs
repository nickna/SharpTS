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


internal sealed class MountedNode(GuiVNode vnode, NodeDescriptor descriptor, Control control)
{
    public GuiVNode VNode { get; set; } = vnode;
    public NodeDescriptor Descriptor { get; } = descriptor;
    public Control Control { get; } = control;
    public ControlRef Handle { get; } = new(control);
    public List<MountedNode> Children { get; } = [];
    public Action? LatestClick { get; set; }
    public Action<string>? LatestTextChanged { get; set; }
    public Action<bool>? LatestCheckedChanged { get; set; }
    public Action<bool>? LatestExpandedChanged { get; set; }
    public Action<double>? LatestSelectionChanged { get; set; }
    public Action<double>? LatestValueChanged { get; set; }
    public Action<int[]>? LatestIndicesChanged { get; set; }
    public Action<double?>? LatestNullableValueChanged { get; set; }
    public Action<string?>? LatestNullableStringChanged { get; set; }
    public Func<string, bool, bool, bool, bool, bool, bool>? LatestKeyDown { get; set; }
    public Func<string, bool, bool, bool, bool, bool, bool>? LatestKeyUp { get; set; }
    public Func<string[], string?, string, bool, bool, bool, bool, string>? LatestDragOver { get; set; }
    public Action<string[], string?, string, bool, bool, bool, bool>? LatestDrop { get; set; }
    public EventHandler<Avalonia.Input.KeyEventArgs>? KeyDownHandler { get; set; }
    public EventHandler<Avalonia.Input.KeyEventArgs>? KeyUpHandler { get; set; }
    public EventHandler<Avalonia.Input.DragEventArgs>? DragOverHandler { get; set; }
    public EventHandler<Avalonia.Input.DragEventArgs>? DropHandler { get; set; }
    public EventHandler<RoutedEventArgs>? RoutedHandler { get; set; }
    public EventHandler<TextChangedEventArgs>? TextHandler { get; set; }
    public EventHandler<SelectionChangedEventArgs>? SelectionHandler { get; set; }
    public EventHandler<RangeBaseValueChangedEventArgs>? ValueHandler { get; set; }
    public bool EventAttached { get; set; }
    public bool PrimaryEventAttached { get; set; }
    public bool WindowKeyResetAttached { get; set; }
    public bool SuppressEvents { get; set; }
    public string? LastTextValue { get; set; }
    public bool LastCheckedValue { get; set; }
    public int LastSelectionValue { get; set; } = -1;
    public double LastNumberValue { get; set; } = double.NaN;
    public double? LastNullableNumberValue { get; set; }
    public string? LastNullableStringValue { get; set; }
    public int[] LastIndices { get; set; } = [];
    public List<Action> ExtraUnsubscribe { get; } = [];
    public Action<object?>? RefCallback { get; set; }
    public object? RefIdentity { get; set; }
    public bool RefAttached { get; set; }
    public bool Released { get; set; }
}
