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
using Avalonia.Input;

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
        if (!double.IsNaN(node.CanvasLeft) && !double.IsFinite(node.CanvasLeft))
            throw new ArgumentOutOfRangeException("canvasLeft", "canvasLeft must be finite.");
        if (!double.IsNaN(node.CanvasTop) && !double.IsFinite(node.CanvasTop))
            throw new ArgumentOutOfRangeException("canvasTop", "canvasTop must be finite.");
        if (node.Classes is not null)
        {
            var seenClasses = new HashSet<string>(StringComparer.Ordinal);
            foreach (string styleClass in node.Classes)
            {
                if (string.IsNullOrWhiteSpace(styleClass) || styleClass[0] == ':')
                    throw new ArgumentException("Style classes must be non-empty user class names.", "classes");
                if (!seenClasses.Add(styleClass))
                    throw new ArgumentException($"Duplicate style class '{styleClass}'.", "classes");
            }
        }
    }

    public static bool Apply(Control control, GuiVNode node)
    {
        bool changed = false;
        changed |= ApplyStyled(control, Layoutable.WidthProperty, EffectiveWidth(node),
            IsSpecified(node, "width") || !double.IsNaN(node.Width) || node.Kind == "Window");
        changed |= ApplyStyled(control, Layoutable.HeightProperty, EffectiveHeight(node),
            IsSpecified(node, "height") || !double.IsNaN(node.Height) || node.Kind == "Window");
        changed |= ApplyStyled(control, Layoutable.MinWidthProperty, node.MinWidth,
            IsSpecified(node, "minWidth") || node.MinWidth != 0);
        changed |= ApplyStyled(control, Layoutable.MinHeightProperty, node.MinHeight,
            IsSpecified(node, "minHeight") || node.MinHeight != 0);
        changed |= ApplyStyled(control, Layoutable.MaxWidthProperty, node.MaxWidth,
            IsSpecified(node, "maxWidth") || !double.IsPositiveInfinity(node.MaxWidth));
        changed |= ApplyStyled(control, Layoutable.MaxHeightProperty, node.MaxHeight,
            IsSpecified(node, "maxHeight") || !double.IsPositiveInfinity(node.MaxHeight));
        double[] margins = Margins(node);
        var margin = new Thickness(margins[0], margins[1], margins[2], margins[3]);
        changed |= ApplyStyled(control, Layoutable.MarginProperty, margin,
            IsSpecified(node, "margin") || margin != default);
        HorizontalAlignment horizontal = ParseHorizontalAlignment(node.HorizontalAlignment);
        changed |= ApplyStyled(control, Layoutable.HorizontalAlignmentProperty, horizontal,
            IsSpecified(node, "horizontalAlignment") || horizontal != HorizontalAlignment.Stretch);
        VerticalAlignment vertical = ParseVerticalAlignment(node.VerticalAlignment);
        changed |= ApplyStyled(control, Layoutable.VerticalAlignmentProperty, vertical,
            IsSpecified(node, "verticalAlignment") || vertical != VerticalAlignment.Stretch);
        changed |= ApplyStyled(control, Visual.IsVisibleProperty, node.IsVisible,
            IsSpecified(node, "isVisible") || !node.IsVisible);
        changed |= ApplyStyled(control, InputElement.IsEnabledProperty, node.IsEnabled,
            IsSpecified(node, "isEnabled") || !node.IsEnabled);
        changed |= ApplyStyled(control, Visual.OpacityProperty, node.Opacity,
            IsSpecified(node, "opacity") || node.Opacity != 1);
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
        string[] classes = node.Classes ?? [];
        if (!control.Classes.SequenceEqual(classes, StringComparer.Ordinal))
        {
            control.Classes.Clear();
            foreach (string styleClass in classes)
                control.Classes.Add(styleClass);
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
        if (!Canvas.GetLeft(control).Equals(node.CanvasLeft))
        {
            Canvas.SetLeft(control, node.CanvasLeft);
            changed = true;
        }
        if (!Canvas.GetTop(control).Equals(node.CanvasTop))
        {
            Canvas.SetTop(control, node.CanvasTop);
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
        changed |= ApplyStyled(control, TemplatedControl.BackgroundProperty, background,
            IsSpecified(node, "background") || background is not null);
        changed |= ApplyStyled(control, TemplatedControl.ForegroundProperty, foreground,
            IsSpecified(node, "foreground") || foreground is not null);
        changed |= ApplyStyled(control, TemplatedControl.FontSizeProperty, node.FontSize,
            IsSpecified(node, "fontSize") || !double.IsNaN(node.FontSize));
        FontWeight weight = ParseFontWeight(node.FontWeight);
        changed |= ApplyStyled(control, TemplatedControl.FontWeightProperty, weight,
            IsSpecified(node, "fontWeight") || weight != FontWeight.Normal);
        FontStyle style = ParseFontStyle(node.FontStyle);
        changed |= ApplyStyled(control, TemplatedControl.FontStyleProperty, style,
            IsSpecified(node, "fontStyle") || style != FontStyle.Normal);
        if (!string.IsNullOrWhiteSpace(node.FontFamily))
        {
            var family = new FontFamily(node.FontFamily);
            changed |= ApplyStyled(control, TemplatedControl.FontFamilyProperty, family, specified: true);
        }
        else changed |= ApplyStyled(control, TemplatedControl.FontFamilyProperty, control.FontFamily, specified: false);
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
        changed |= ApplyStyled(control, TemplatedControl.PaddingProperty, padding,
            IsSpecified(node, "padding") || padding != default);
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

    private static bool IsSpecified(GuiVNode node, string property) =>
        node.SpecifiedProperties.Contains(property, StringComparer.Ordinal);

    private static bool ApplyStyled<T>(
        AvaloniaObject target,
        StyledProperty<T> property,
        T value,
        bool specified)
    {
        if (!specified)
        {
            target.ClearValue(property);
            return false;
        }
        if (Equals(target.GetValue(property), value))
            return false;
        target.SetValue(property, value);
        return true;
    }
}
