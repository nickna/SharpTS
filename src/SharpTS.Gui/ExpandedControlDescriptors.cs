using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SharpTS.Gui;

internal sealed class WrapPanelDescriptor() : NodeDescriptor("WrapPanel", 0, int.MaxValue)
{
    public override void Validate(GuiVNode node)
    {
        CommonProperties.ValidateFiniteNonNegative(node.Spacing, "spacing");
        _ = Orientation(node.Orientation);
    }

    public override Control Create(GuiVNode node)
    {
        var panel = new WrapPanel();
        Update(panel, new GuiVNode(Kind), node);
        return panel;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var panel = (WrapPanel)control;
        bool changed = CommonProperties.Apply(panel, next);
        Orientation orientation = Orientation(next.Orientation);
        if (panel.Orientation != orientation) { panel.Orientation = orientation; changed = true; }
        if (panel.ItemSpacing != next.Spacing) { panel.ItemSpacing = next.Spacing; changed = true; }
        if (panel.LineSpacing != next.Spacing) { panel.LineSpacing = next.Spacing; changed = true; }
        return changed;
    }

    private static Orientation Orientation(string value) => value switch
    {
        "horizontal" => Avalonia.Layout.Orientation.Horizontal,
        "vertical" => Avalonia.Layout.Orientation.Vertical,
        _ => throw new ArgumentException($"Unsupported orientation '{value}'."),
    };
}

internal sealed class DockPanelDescriptor() : NodeDescriptor("DockPanel", 0, int.MaxValue)
{
    public override Control Create(GuiVNode node)
    {
        var panel = new DockPanel();
        Update(panel, new GuiVNode(Kind), node);
        return panel;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var panel = (DockPanel)control;
        bool changed = CommonProperties.Apply(panel, next);
        if (panel.LastChildFill != next.LastChildFill) { panel.LastChildFill = next.LastChildFill; changed = true; }
        return changed;
    }
}

internal sealed class SeparatorDescriptor() : NodeDescriptor("Separator", 0, 0)
{
    public override Control Create(GuiVNode node)
    {
        var separator = new Separator();
        Update(separator, new GuiVNode(Kind), node);
        return separator;
    }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next) => CommonProperties.Apply(control, next);
}

internal sealed class ListBoxDescriptor() : NodeDescriptor("ListBox", 0, 0)
{
    public override void Validate(GuiVNode node)
    {
        int count = node.Items?.Length ?? 0;
        foreach (int index in node.SelectedIndices ?? [])
            if (index < 0 || index >= count) throw new ArgumentOutOfRangeException("selectedIndices");
        if (node.SelectionMode is not ("single" or "multiple")) throw new ArgumentException("selectionMode must be 'single' or 'multiple'.");
    }
    public override Control Create(GuiVNode node)
    {
        var list = new ListBox(); Update(list, new GuiVNode(Kind), node); return list;
    }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var list = (ListBox)control;
        bool changed = CommonProperties.Apply(list, next);
        changed |= CommonProperties.ApplyTemplated(list, next);
        string[] items = next.Items ?? [];
        if (!(previous.Items ?? []).SequenceEqual(items, StringComparer.Ordinal)) { list.ItemsSource = items; changed = true; }
        var mode = next.SelectionMode == "multiple" ? Avalonia.Controls.SelectionMode.Multiple : Avalonia.Controls.SelectionMode.Single;
        if (list.SelectionMode != mode) { list.SelectionMode = mode; changed = true; }
        int[] desired = next.SelectedIndices ?? [];
        int[] current = list.SelectedItems is { } selectedItems
            ? selectedItems.Cast<object>().Select(item => Array.IndexOf(items, item as string)).Where(index => index >= 0).Order().ToArray()
            : [];
        if (!current.SequenceEqual(desired.Order()))
        {
            list.SelectedItems?.Clear();
            foreach (int index in desired) list.SelectedItems?.Add(items[index]);
            changed = true;
        }
        return changed;
    }
}

internal sealed class NumericUpDownDescriptor() : NodeDescriptor("NumericUpDown", 0, 0)
{
    public override void Validate(GuiVNode node)
    {
        if (!double.IsFinite(node.Minimum) || !double.IsFinite(node.Maximum) || node.Maximum < node.Minimum)
            throw new ArgumentOutOfRangeException("minimum/maximum");
        if (!double.IsFinite(node.Increment) || node.Increment <= 0) throw new ArgumentOutOfRangeException("increment");
        if (node.NullableValue is double value && (!double.IsFinite(value) || value < node.Minimum || value > node.Maximum))
            throw new ArgumentOutOfRangeException("value");
    }
    public override Control Create(GuiVNode node) { var value = new NumericUpDown(); Update(value, new GuiVNode(Kind), node); return value; }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var value = (NumericUpDown)control; bool changed = CommonProperties.Apply(value, next);
        changed |= CommonProperties.ApplyTemplated(value, next);
        decimal minimum = (decimal)next.Minimum, maximum = (decimal)next.Maximum, increment = (decimal)next.Increment;
        decimal? current = next.NullableValue is double number ? (decimal)number : null;
        if (value.Minimum != minimum) { value.Minimum = minimum; changed = true; }
        if (value.Maximum != maximum) { value.Maximum = maximum; changed = true; }
        if (value.Increment != increment) { value.Increment = increment; changed = true; }
        if (value.Value != current) { value.Value = current; changed = true; }
        return changed;
    }
}

internal sealed class DatePickerDescriptor() : NodeDescriptor("DatePicker", 0, 0)
{
    public override void Validate(GuiVNode node) => _ = Parse(node.StringValue);
    public override Control Create(GuiVNode node) { var picker = new DatePicker(); Update(picker, new GuiVNode(Kind), node); return picker; }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var picker = (DatePicker)control; bool changed = CommonProperties.Apply(picker, next);
        DateTimeOffset? value = Parse(next.StringValue);
        if (picker.SelectedDate != value) { picker.SelectedDate = value; changed = true; }
        return changed;
    }
    internal static DateTimeOffset? Parse(string? value)
    {
        if (value is null) return null;
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            throw new ArgumentException("DatePicker values must use YYYY-MM-DD.");
        return new DateTimeOffset(date, TimeSpan.Zero);
    }
}

internal sealed class TimePickerDescriptor() : NodeDescriptor("TimePicker", 0, 0)
{
    public override void Validate(GuiVNode node) => _ = Parse(node.StringValue);
    public override Control Create(GuiVNode node) { var picker = new TimePicker(); Update(picker, new GuiVNode(Kind), node); return picker; }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var picker = (TimePicker)control; bool changed = CommonProperties.Apply(picker, next);
        TimeSpan? value = Parse(next.StringValue);
        if (picker.SelectedTime != value) { picker.SelectedTime = value; changed = true; }
        picker.UseSeconds = next.StringValue?.Count(character => character == ':') == 2;
        return changed;
    }
    internal static TimeSpan? Parse(string? value)
    {
        if (value is null) return null;
        string[] formats = ["hh\\:mm", "hh\\:mm\\:ss"];
        if (!TimeSpan.TryParseExact(value, formats, CultureInfo.InvariantCulture, out TimeSpan time))
            throw new ArgumentException("TimePicker values must use HH:mm or HH:mm:ss.");
        return time;
    }
}

internal sealed class ImageDescriptor() : NodeDescriptor("Image", 0, 0)
{
    public override void Validate(GuiVNode node)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node.Source);
        _ = Stretch(node.Stretch);
    }
    public override Control Create(GuiVNode node) { var image = new Image(); Update(image, new GuiVNode(Kind), node); return image; }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var image = (Image)control; bool changed = CommonProperties.Apply(image, next);
        Stretch stretch = Stretch(next.Stretch);
        if (image.Stretch != stretch) { image.Stretch = stretch; changed = true; }
        if (!string.Equals(previous.Source, next.Source, StringComparison.Ordinal))
        {
            DesktopRuntimeContext context = DesktopBridge.RequireContext();
            try
            {
                image.Source = context.LoadImage(next.Source!);
                if (next.Loaded is not null)
                    context.ScheduleGuestMicrotask(() => context.DispatchGuestCallback(next.Loaded));
            }
            catch (Exception exception)
            {
                image.Source = null;
                if (next.LoadError is null)
                    throw;
                string message = exception.Message;
                context.ScheduleGuestMicrotask(() => context.DispatchGuestCallback(() => next.LoadError(message)));
            }
            changed = true;
        }
        return changed;
    }
    private static Stretch Stretch(string value) => value switch
    {
        "none" => Avalonia.Media.Stretch.None,
        "fill" => Avalonia.Media.Stretch.Fill,
        "uniform" => Avalonia.Media.Stretch.Uniform,
        "uniformToFill" => Avalonia.Media.Stretch.UniformToFill,
        _ => throw new ArgumentException($"Unsupported stretch '{value}'."),
    };
}

internal sealed class TabControlDescriptor() : NodeDescriptor("TabControl", 0, int.MaxValue)
{
    public override Control Create(GuiVNode node) { var tabs = new TabControl(); Update(tabs, new GuiVNode(Kind), node); return tabs; }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var tabs = (TabControl)control; bool changed = CommonProperties.Apply(tabs, next);
        if (tabs.SelectedIndex != next.SelectedIndex) { tabs.SelectedIndex = next.SelectedIndex; changed = true; }
        return changed;
    }
}

internal sealed class TabItemDescriptor() : NodeDescriptor("TabItem", 0, 1)
{
    public override Control Create(GuiVNode node) { var item = new TabItem(); Update(item, new GuiVNode(Kind), node); return item; }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var item = (TabItem)control; bool changed = CommonProperties.Apply(item, next);
        if (!Equals(item.Header, next.Header)) { item.Header = next.Header; changed = true; }
        return changed;
    }
}

internal sealed class MenuDescriptor() : NodeDescriptor("Menu", 0, int.MaxValue)
{
    public override Control Create(GuiVNode node) { var menu = new Menu(); Update(menu, new GuiVNode(Kind), node); return menu; }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next) => CommonProperties.Apply(control, next);
}

internal sealed class MenuItemDescriptor() : NodeDescriptor("MenuItem", 0, int.MaxValue)
{
    public override Control Create(GuiVNode node) { var item = new MenuItem(); Update(item, new GuiVNode(Kind), node); return item; }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var item = (MenuItem)control; bool changed = CommonProperties.Apply(item, next);
        if (!Equals(item.Header, next.Text)) { item.Header = next.Text; changed = true; }
        if (item.IsChecked != next.IsChecked) { item.IsChecked = next.IsChecked; changed = true; }
        return changed;
    }
}
