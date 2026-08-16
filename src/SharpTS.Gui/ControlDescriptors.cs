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


internal sealed class WindowDescriptor() : NodeDescriptor("Window", 0, 1)
{
    public override void Validate(GuiVNode node) => _ = ParseTheme(node.Theme);

    public override Control Create(GuiVNode node)
    {
        var window = new Window();
        Update(window, new GuiVNode("Window"), node);
        return window;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var window = (Window)control;
        bool changed = CommonProperties.Apply(window, next);
        string title = next.Title ?? "SharpTS GUI";
        if (window.Title != title)
        {
            window.Title = title;
            changed = true;
        }
        if (window.CanResize != next.CanResize)
        {
            window.CanResize = next.CanResize;
            changed = true;
        }
        ThemeVariant theme = ParseTheme(next.Theme);
        if (!Equals(window.RequestedThemeVariant, theme))
        {
            window.RequestedThemeVariant = theme;
            changed = true;
        }
        return changed;
    }

    private static ThemeVariant ParseTheme(string theme) => theme switch
    {
        "system" => ThemeVariant.Default,
        "light" => ThemeVariant.Light,
        "dark" => ThemeVariant.Dark,
        _ => throw new ArgumentException($"Unsupported theme '{theme}'."),
    };
}

internal sealed class StackPanelDescriptor(string kind, bool updateLayout = true)
    : NodeDescriptor(kind, 0, int.MaxValue)
{
    public override void Validate(GuiVNode node)
    {
        if (!updateLayout)
            return;
        CommonProperties.ValidateFiniteNonNegative(node.Spacing, "spacing");
        _ = ParseOrientation(node.Orientation);
    }

    public override Control Create(GuiVNode node)
    {
        var panel = new StackPanel();
        Update(panel, new GuiVNode(Kind), node);
        return panel;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var panel = (StackPanel)control;
        bool changed = CommonProperties.Apply(panel, next);
        if (!updateLayout)
            return changed;
        if (panel.Spacing != next.Spacing)
        {
            panel.Spacing = next.Spacing;
            changed = true;
        }
        Orientation orientation = ParseOrientation(next.Orientation);
        if (panel.Orientation != orientation)
        {
            panel.Orientation = orientation;
            changed = true;
        }
        return changed;
    }

    private static Orientation ParseOrientation(string value) => value switch
    {
        "horizontal" => Orientation.Horizontal,
        "vertical" => Orientation.Vertical,
        _ => throw new ArgumentException($"Unsupported orientation '{value}'."),
    };
}

internal sealed class GridDescriptor() : NodeDescriptor("Grid", 0, int.MaxValue)
{
    public override void Validate(GuiVNode node)
    {
        _ = new RowDefinitions(node.Rows);
        _ = new ColumnDefinitions(node.Columns);
    }

    public override Control Create(GuiVNode node)
    {
        var grid = new Grid();
        Update(grid, new GuiVNode("Grid"), node);
        return grid;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var grid = (Grid)control;
        bool changed = CommonProperties.Apply(grid, next);
        if (!string.Equals(previous.Rows, next.Rows, StringComparison.Ordinal))
        {
            grid.RowDefinitions = new RowDefinitions(next.Rows);
            changed = true;
        }
        if (!string.Equals(previous.Columns, next.Columns, StringComparison.Ordinal))
        {
            grid.ColumnDefinitions = new ColumnDefinitions(next.Columns);
            changed = true;
        }
        return changed;
    }
}

internal sealed class BorderDescriptor(string kind = "Border") : NodeDescriptor(kind, 0, 1)
{
    public override void Validate(GuiVNode node)
    {
        foreach (double value in new[] { CommonProperties.Padding(node).Left, CommonProperties.Padding(node).Top, CommonProperties.Padding(node).Right, CommonProperties.Padding(node).Bottom })
            CommonProperties.ValidateFiniteNonNegative(value, "padding");
        foreach (double value in new[] { CommonProperties.BorderThickness(node).Left, CommonProperties.BorderThickness(node).Top, CommonProperties.BorderThickness(node).Right, CommonProperties.BorderThickness(node).Bottom })
            CommonProperties.ValidateFiniteNonNegative(value, "borderThickness");
        CommonProperties.ValidateFiniteNonNegative(node.CornerRadius, "cornerRadius");
        _ = CommonProperties.ParseBrush(node.Background);
        _ = CommonProperties.ParseBrush(node.BorderBrush);
    }

    public override Control Create(GuiVNode node)
    {
        var border = new Border();
        Update(border, new GuiVNode("Border"), node);
        return border;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var border = (Border)control;
        bool changed = CommonProperties.Apply(border, next);
        var padding = CommonProperties.Padding(next);
        var thickness = CommonProperties.BorderThickness(next);
        var radius = new CornerRadius(next.CornerRadius);
        IBrush? background = CommonProperties.ParseBrush(next.Background);
        IBrush? brush = CommonProperties.ParseBrush(next.BorderBrush);
        changed |= Set(border.Padding, padding, value => border.Padding = value);
        changed |= Set(border.BorderThickness, thickness, value => border.BorderThickness = value);
        changed |= Set(border.CornerRadius, radius, value => border.CornerRadius = value);
        changed |= Set(border.Background, background, value => border.Background = value);
        changed |= Set(border.BorderBrush, brush, value => border.BorderBrush = value);
        return changed;
    }

    private static bool Set<T>(T current, T value, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return false;
        assign(value);
        return true;
    }
}

internal sealed class ScrollViewerDescriptor() : NodeDescriptor("ScrollViewer", 0, 1)
{
    public override void Validate(GuiVNode node)
    {
        _ = ParseVisibility(node.HorizontalScrollBarVisibility);
        _ = ParseVisibility(node.VerticalScrollBarVisibility);
    }

    public override Control Create(GuiVNode node)
    {
        var viewer = new ScrollViewer();
        Update(viewer, new GuiVNode("ScrollViewer"), node);
        return viewer;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var viewer = (ScrollViewer)control;
        bool changed = CommonProperties.Apply(viewer, next);
        ScrollBarVisibility horizontal = ParseVisibility(next.HorizontalScrollBarVisibility);
        ScrollBarVisibility vertical = ParseVisibility(next.VerticalScrollBarVisibility);
        if (viewer.HorizontalScrollBarVisibility != horizontal)
        {
            viewer.HorizontalScrollBarVisibility = horizontal;
            changed = true;
        }
        if (viewer.VerticalScrollBarVisibility != vertical)
        {
            viewer.VerticalScrollBarVisibility = vertical;
            changed = true;
        }
        return changed;
    }

    private static ScrollBarVisibility ParseVisibility(string value) => value switch
    {
        "auto" => ScrollBarVisibility.Auto,
        "visible" => ScrollBarVisibility.Visible,
        "hidden" => ScrollBarVisibility.Hidden,
        "disabled" => ScrollBarVisibility.Disabled,
        _ => throw new ArgumentException($"Unsupported scroll bar visibility '{value}'."),
    };
}

internal sealed class TextBlockDescriptor() : NodeDescriptor("TextBlock", 0, 0)
{
    public override void Validate(GuiVNode node)
    {
        if (!double.IsNaN(node.FontSize))
            CommonProperties.ValidateDimension(node.FontSize, "fontSize", allowNaN: false);
        _ = CommonProperties.ParseFontWeight(node.FontWeight);
        _ = CommonProperties.ParseFontStyle(node.FontStyle);
        _ = CommonProperties.ParseTextAlignment(node.TextAlignment);
        _ = ParseTextWrapping(node.TextWrapping);
        _ = CommonProperties.ParseBrush(node.Foreground);
    }

    public override Control Create(GuiVNode node)
    {
        var text = new TextBlock();
        Update(text, new GuiVNode("TextBlock"), node);
        return text;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var text = (TextBlock)control;
        bool changed = CommonProperties.Apply(text, next);
        string value = next.Text ?? string.Empty;
        if (text.Text != value)
        {
            text.Text = value;
            changed = true;
        }
        double fontSize = double.IsNaN(next.FontSize) ? TextBlock.FontSizeProperty.GetDefaultValue(text) : next.FontSize;
        if (text.FontSize != fontSize)
        {
            text.FontSize = fontSize;
            changed = true;
        }
        FontWeight weight = CommonProperties.ParseFontWeight(next.FontWeight);
        if (text.FontWeight != weight)
        {
            text.FontWeight = weight;
            changed = true;
        }
        FontStyle style = CommonProperties.ParseFontStyle(next.FontStyle);
        if (text.FontStyle != style)
        {
            text.FontStyle = style;
            changed = true;
        }
        TextAlignment alignment = CommonProperties.ParseTextAlignment(next.TextAlignment);
        if (text.TextAlignment != alignment)
        {
            text.TextAlignment = alignment;
            changed = true;
        }
        TextWrapping wrapping = ParseTextWrapping(next.TextWrapping);
        if (text.TextWrapping != wrapping)
        {
            text.TextWrapping = wrapping;
            changed = true;
        }
        IBrush? foreground = CommonProperties.ParseBrush(next.Foreground);
        if (!Equals(text.Foreground, foreground))
        {
            text.Foreground = foreground;
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(next.FontFamily))
        {
            var family = new FontFamily(next.FontFamily);
            if (!Equals(text.FontFamily, family))
            {
                text.FontFamily = family;
                changed = true;
            }
        }
        return changed;
    }

    private static TextWrapping ParseTextWrapping(string value) => value switch
    {
        "noWrap" => TextWrapping.NoWrap,
        "wrap" => TextWrapping.Wrap,
        _ => throw new ArgumentException($"Unsupported textWrapping '{value}'."),
    };
}

internal sealed class ButtonDescriptor() : NodeDescriptor("Button", 0, 0)
{
    public override void Validate(GuiVNode node)
    {
        foreach (double value in new[] { CommonProperties.Padding(node).Left, CommonProperties.Padding(node).Top, CommonProperties.Padding(node).Right, CommonProperties.Padding(node).Bottom })
            CommonProperties.ValidateFiniteNonNegative(value, "padding");
    }

    public override Control Create(GuiVNode node)
    {
        var button = new Button();
        Update(button, new GuiVNode("Button"), node);
        return button;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var button = (Button)control;
        bool changed = CommonProperties.Apply(button, next);
        changed |= CommonProperties.ApplyContent(button, next);
        if (button.CornerRadius != new CornerRadius(next.CornerRadius))
        {
            button.CornerRadius = new CornerRadius(next.CornerRadius);
            changed = true;
        }
        string content = next.Text ?? string.Empty;
        if (!Equals(button.Content, content))
        {
            button.Content = content;
            changed = true;
        }
        return changed;
    }
}

internal sealed class TextBoxDescriptor(string kind = "TextBox") : NodeDescriptor(kind, 0, 0)
{
    public override Control Create(GuiVNode node)
    {
        var textBox = new TextBox();
        Update(textBox, new GuiVNode("TextBox"), node);
        return textBox;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var textBox = (TextBox)control;
        bool changed = CommonProperties.Apply(textBox, next);
        changed |= CommonProperties.ApplyTemplated(textBox, next);
        TextAlignment alignment = CommonProperties.ParseTextAlignment(next.TextAlignment);
        if (textBox.TextAlignment != alignment)
        {
            textBox.TextAlignment = alignment;
            changed = true;
        }
        string text = next.Text ?? string.Empty;
        if (textBox.Text != text)
        {
            textBox.Text = text;
            changed = true;
        }
        if (textBox.PlaceholderText != next.Placeholder)
        {
            textBox.PlaceholderText = next.Placeholder;
            changed = true;
        }
        if (textBox.IsReadOnly != next.IsReadOnly)
        {
            textBox.IsReadOnly = next.IsReadOnly;
            changed = true;
        }
        if (textBox.AcceptsReturn != next.AcceptsReturn)
        {
            textBox.AcceptsReturn = next.AcceptsReturn;
            changed = true;
        }
        if (textBox.MaxLength != next.MaxLength)
        {
            textBox.MaxLength = next.MaxLength;
            changed = true;
        }
        char password = next.IsPassword ? '●' : '\0';
        if (textBox.PasswordChar != password)
        {
            textBox.PasswordChar = password;
            changed = true;
        }
        return changed;
    }
}

internal sealed class CheckBoxDescriptor(string kind = "CheckBox") : NodeDescriptor(kind, 0, 0)
{
    public override Control Create(GuiVNode node)
    {
        ToggleButton checkBox = Kind switch
        {
            "RadioButton" => new RadioButton { GroupName = node.GroupName },
            "ToggleSwitch" => new ToggleSwitch(),
            _ => new CheckBox(),
        };
        Update(checkBox, new GuiVNode(Kind), node);
        return checkBox;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var checkBox = (ToggleButton)control;
        bool changed = CommonProperties.Apply(checkBox, next);
        changed |= CommonProperties.ApplyContent(checkBox, next);
        string content = next.Text ?? string.Empty;
        if (!Equals(checkBox.Content, content))
        {
            checkBox.Content = content;
            changed = true;
        }
        if (checkBox.IsChecked != next.IsChecked)
        {
            checkBox.IsChecked = next.IsChecked;
            changed = true;
        }
        if (checkBox is RadioButton radio && radio.GroupName != next.GroupName)
        {
            radio.GroupName = next.GroupName;
            changed = true;
        }
        return changed;
    }
}

internal sealed class ComboBoxDescriptor() : NodeDescriptor("ComboBox", 0, 0)
{
    public override void Validate(GuiVNode node)
    {
        int count = node.Items?.Length ?? 0;
        if (node.SelectedIndex < -1 || node.SelectedIndex >= count)
            throw new ArgumentOutOfRangeException("selectedIndex", "selectedIndex must be -1 or identify an item.");
    }

    public override Control Create(GuiVNode node)
    {
        var comboBox = new ComboBox();
        Update(comboBox, new GuiVNode("ComboBox"), node);
        return comboBox;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var comboBox = (ComboBox)control;
        bool changed = CommonProperties.Apply(comboBox, next);
        changed |= CommonProperties.ApplyTemplated(comboBox, next);
        string[] items = next.Items ?? [];
        if (!(previous.Items ?? []).SequenceEqual(items, StringComparer.Ordinal))
        {
            comboBox.ItemsSource = items;
            changed = true;
        }
        if (comboBox.SelectedIndex != next.SelectedIndex)
        {
            comboBox.SelectedIndex = next.SelectedIndex;
            changed = true;
        }
        return changed;
    }
}

internal abstract class RangeDescriptor(string kind) : NodeDescriptor(kind, 0, 0)
{
    public override void Validate(GuiVNode node)
    {
        if (!double.IsFinite(node.Minimum) || !double.IsFinite(node.Maximum) || node.Maximum <= node.Minimum)
            throw new ArgumentOutOfRangeException("minimum/maximum", "maximum must be greater than minimum and both must be finite.");
        if (!double.IsFinite(node.Value) || node.Value < node.Minimum || node.Value > node.Maximum)
            throw new ArgumentOutOfRangeException("value", "value must be finite and within minimum and maximum.");
    }

    protected static bool UpdateRange(RangeBase range, GuiVNode next)
    {
        bool changed = false;
        if (range.Minimum != next.Minimum)
        {
            range.Minimum = next.Minimum;
            changed = true;
        }
        if (range.Maximum != next.Maximum)
        {
            range.Maximum = next.Maximum;
            changed = true;
        }
        if (range.Value != next.Value)
        {
            range.Value = next.Value;
            changed = true;
        }
        return changed;
    }
}

internal sealed class SliderDescriptor() : RangeDescriptor("Slider")
{
    public override Control Create(GuiVNode node)
    {
        var slider = new Slider();
        Update(slider, new GuiVNode("Slider"), node);
        return slider;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next) =>
        CommonProperties.Apply(control, next) | UpdateRange((Slider)control, next);
}

internal sealed class ProgressBarDescriptor() : RangeDescriptor("ProgressBar")
{
    public override Control Create(GuiVNode node)
    {
        var progress = new ProgressBar();
        Update(progress, new GuiVNode("ProgressBar"), node);
        return progress;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next) =>
        CommonProperties.Apply(control, next) | UpdateRange((ProgressBar)control, next);
}
