using System.Collections;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Automation;

namespace SharpTS.Gui;


internal static class CommonProperties
{
    public static void Validate(GuiVNode node)
    {
        ValidateDimension(node.Width, "width", allowNaN: true);
        ValidateDimension(node.Height, "height", allowNaN: true);
        ValidateFiniteNonNegative(node.MinWidth, "minWidth");
        ValidateFiniteNonNegative(node.MinHeight, "minHeight");
        ValidateMaximum(node.MaxWidth, node.MinWidth, "maxWidth", "minWidth");
        ValidateMaximum(node.MaxHeight, node.MinHeight, "maxHeight", "minHeight");
        foreach (double value in Margins(node))
            ValidateFiniteNonNegative(value, "margin");
        if (!double.IsFinite(node.Opacity) || node.Opacity < 0 || node.Opacity > 1)
            throw new ArgumentOutOfRangeException("opacity", "opacity must be between zero and one.");
        _ = ParseHorizontalAlignment(node.HorizontalAlignment);
        _ = ParseVerticalAlignment(node.VerticalAlignment);
        if (node.GridRow < 0 || node.GridColumn < 0)
            throw new ArgumentOutOfRangeException("gridRow/gridColumn", "Grid row and column must be non-negative.");
        if (node.GridRowSpan < 1 || node.GridColumnSpan < 1)
            throw new ArgumentOutOfRangeException("gridRowSpan/gridColumnSpan", "Grid spans must be at least one.");
        _ = ParseDock(node.Dock);
    }

    public static bool Apply(Control control, GuiVNode node)
    {
        bool changed = false;
        changed |= SetDouble(control.Width, EffectiveWidth(node), value => control.Width = value);
        changed |= SetDouble(control.Height, EffectiveHeight(node), value => control.Height = value);
        changed |= SetDouble(control.MinWidth, node.MinWidth, value => control.MinWidth = value);
        changed |= SetDouble(control.MinHeight, node.MinHeight, value => control.MinHeight = value);
        changed |= SetDouble(control.MaxWidth, node.MaxWidth, value => control.MaxWidth = value);
        changed |= SetDouble(control.MaxHeight, node.MaxHeight, value => control.MaxHeight = value);
        double[] margins = Margins(node);
        var margin = new Thickness(margins[0], margins[1], margins[2], margins[3]);
        if (control.Margin != margin)
        {
            control.Margin = margin;
            changed = true;
        }
        HorizontalAlignment horizontal = ParseHorizontalAlignment(node.HorizontalAlignment);
        if (control.HorizontalAlignment != horizontal)
        {
            control.HorizontalAlignment = horizontal;
            changed = true;
        }
        VerticalAlignment vertical = ParseVerticalAlignment(node.VerticalAlignment);
        if (control.VerticalAlignment != vertical)
        {
            control.VerticalAlignment = vertical;
            changed = true;
        }
        if (control.IsVisible != node.IsVisible)
        {
            control.IsVisible = node.IsVisible;
            changed = true;
        }
        if (control.IsEnabled != node.IsEnabled)
        {
            control.IsEnabled = node.IsEnabled;
            changed = true;
        }
        if (control.Opacity != node.Opacity)
        {
            control.Opacity = node.Opacity;
            changed = true;
        }
        object? currentTip = ToolTip.GetTip(control);
        if (!Equals(currentTip, node.ToolTip))
        {
            ToolTip.SetTip(control, node.ToolTip);
            changed = true;
        }
        if (!string.Equals(AutomationProperties.GetName(control), node.AutomationName, StringComparison.Ordinal))
        {
            AutomationProperties.SetName(control, node.AutomationName);
            changed = true;
        }
        if (Grid.GetRow(control) != node.GridRow)
        {
            Grid.SetRow(control, node.GridRow);
            changed = true;
        }
        if (Grid.GetColumn(control) != node.GridColumn)
        {
            Grid.SetColumn(control, node.GridColumn);
            changed = true;
        }
        if (Grid.GetRowSpan(control) != node.GridRowSpan)
        {
            Grid.SetRowSpan(control, node.GridRowSpan);
            changed = true;
        }
        if (Grid.GetColumnSpan(control) != node.GridColumnSpan)
        {
            Grid.SetColumnSpan(control, node.GridColumnSpan);
            changed = true;
        }
        Dock dock = ParseDock(node.Dock);
        if (DockPanel.GetDock(control) != dock)
        {
            DockPanel.SetDock(control, dock);
            changed = true;
        }
        return changed;
    }

    public static void ValidateFiniteNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, $"{name} must be finite and non-negative.");
    }

    public static void ValidateDimension(double value, string name, bool allowNaN)
    {
        if (allowNaN && double.IsNaN(value))
            return;
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, $"{name} must be a positive finite number.");
    }

    public static IBrush? ParseBrush(string? value) =>
        value is null ? null : Brush.Parse(value);

    public static Thickness Padding(GuiVNode node)
    {
        double fallback = node.Padding;
        return new Thickness(
            double.IsNaN(node.PaddingLeft) ? fallback : node.PaddingLeft,
            double.IsNaN(node.PaddingTop) ? fallback : node.PaddingTop,
            double.IsNaN(node.PaddingRight) ? fallback : node.PaddingRight,
            double.IsNaN(node.PaddingBottom) ? fallback : node.PaddingBottom);
    }

    public static Thickness BorderThickness(GuiVNode node)
    {
        double fallback = node.BorderThickness;
        return new Thickness(
            double.IsNaN(node.BorderLeft) ? fallback : node.BorderLeft,
            double.IsNaN(node.BorderTop) ? fallback : node.BorderTop,
            double.IsNaN(node.BorderRight) ? fallback : node.BorderRight,
            double.IsNaN(node.BorderBottom) ? fallback : node.BorderBottom);
    }

    public static FontWeight ParseFontWeight(string value) => value switch
    {
        "normal" => FontWeight.Normal,
        "medium" => FontWeight.Medium,
        "semibold" => FontWeight.SemiBold,
        "bold" => FontWeight.Bold,
        _ => throw new ArgumentException($"Unsupported fontWeight '{value}'."),
    };

    public static FontStyle ParseFontStyle(string value) => value switch
    {
        "normal" => FontStyle.Normal,
        "italic" => FontStyle.Italic,
        _ => throw new ArgumentException($"Unsupported fontStyle '{value}'."),
    };

    public static bool ApplyTemplated(TemplatedControl control, GuiVNode node)
    {
        bool changed = false;
        IBrush? background = ParseBrush(node.Background);
        IBrush? foreground = ParseBrush(node.Foreground);
        if (!Equals(control.Background, background)) { control.Background = background; changed = true; }
        if (!Equals(control.Foreground, foreground)) { control.Foreground = foreground; changed = true; }
        if (!double.IsNaN(node.FontSize) && control.FontSize != node.FontSize) { control.FontSize = node.FontSize; changed = true; }
        FontWeight weight = ParseFontWeight(node.FontWeight);
        if (control.FontWeight != weight) { control.FontWeight = weight; changed = true; }
        FontStyle style = ParseFontStyle(node.FontStyle);
        if (control.FontStyle != style) { control.FontStyle = style; changed = true; }
        if (!string.IsNullOrWhiteSpace(node.FontFamily))
        {
            var family = new FontFamily(node.FontFamily);
            if (!Equals(control.FontFamily, family)) { control.FontFamily = family; changed = true; }
        }
        return changed;
    }

    public static bool ApplyContent(ContentControl control, GuiVNode node)
    {
        bool changed = ApplyTemplated(control, node);
        HorizontalAlignment horizontal = ParseHorizontalAlignment(node.HorizontalContentAlignment);
        VerticalAlignment vertical = ParseVerticalAlignment(node.VerticalContentAlignment);
        if (control.HorizontalContentAlignment != horizontal) { control.HorizontalContentAlignment = horizontal; changed = true; }
        if (control.VerticalContentAlignment != vertical) { control.VerticalContentAlignment = vertical; changed = true; }
        Thickness padding = Padding(node);
        if (control.Padding != padding) { control.Padding = padding; changed = true; }
        return changed;
    }

    public static TextAlignment ParseTextAlignment(string value) => value switch
    {
        "left" => TextAlignment.Left,
        "center" => TextAlignment.Center,
        "right" => TextAlignment.Right,
        "justify" => TextAlignment.Justify,
        _ => throw new ArgumentException($"Unsupported textAlignment '{value}'."),
    };

    private static double[] Margins(GuiVNode node) =>
    [
        double.IsNaN(node.MarginLeft) ? node.Margin : node.MarginLeft,
        double.IsNaN(node.MarginTop) ? node.Margin : node.MarginTop,
        double.IsNaN(node.MarginRight) ? node.Margin : node.MarginRight,
        double.IsNaN(node.MarginBottom) ? node.Margin : node.MarginBottom,
    ];

    private static double EffectiveWidth(GuiVNode node) =>
        node.Kind == "Window" && double.IsNaN(node.Width) ? 480 : node.Width;

    private static double EffectiveHeight(GuiVNode node) =>
        node.Kind == "Window" && double.IsNaN(node.Height) ? 260 : node.Height;

    private static void ValidateMaximum(double maximum, double minimum, string maximumName, string minimumName)
    {
        if ((double.IsInfinity(maximum) && maximum > 0) || (double.IsFinite(maximum) && maximum >= minimum))
            return;
        throw new ArgumentOutOfRangeException(maximumName, $"{maximumName} must be at least {minimumName}.");
    }

    private static HorizontalAlignment ParseHorizontalAlignment(string value) => value switch
    {
        "left" => HorizontalAlignment.Left,
        "center" => HorizontalAlignment.Center,
        "right" => HorizontalAlignment.Right,
        "stretch" => HorizontalAlignment.Stretch,
        _ => throw new ArgumentException($"Unsupported horizontalAlignment '{value}'."),
    };

    private static VerticalAlignment ParseVerticalAlignment(string value) => value switch
    {
        "top" => VerticalAlignment.Top,
        "center" => VerticalAlignment.Center,
        "bottom" => VerticalAlignment.Bottom,
        "stretch" => VerticalAlignment.Stretch,
        _ => throw new ArgumentException($"Unsupported verticalAlignment '{value}'."),
    };

    private static Dock ParseDock(string value) => value switch
    {
        "left" => Dock.Left,
        "top" => Dock.Top,
        "right" => Dock.Right,
        "bottom" => Dock.Bottom,
        _ => throw new ArgumentException($"Unsupported dock '{value}'."),
    };

    private static bool SetDouble(double current, double value, Action<double> assign)
    {
        if (current.Equals(value))
            return false;
        assign(value);
        return true;
    }
}
