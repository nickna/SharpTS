using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SharpTS.Gui;

/// <summary>Keep native editing behavior while removing Fluent input chrome for inline editors.</summary>
internal static class DesktopTextInputAppearance
{
    private static readonly string[] BrushResources =
    [
        "TextControlBackgroundFocused", "TextControlBackgroundPointerOver",
        "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver",
    ];

    internal static bool Apply(TextBox control, GuiVNode previous, GuiVNode next)
    {
        bool plain = next.TextBoxAppearance == "plain";
        if (!plain)
        {
            if (previous.TextBoxAppearance != "plain") return false;
            foreach (string key in BrushResources) control.Resources.Remove(key);
            control.Resources.Remove("TextControlBorderThemeThicknessFocused");
            control.ClearValue(TextBox.BorderThicknessProperty);
            control.ClearValue(TextBox.CornerRadiusProperty);
            control.ClearValue(TextBox.PaddingProperty);
            control.ClearValue(TextBox.MinHeightProperty);
            control.ClearValue(TextBox.CaretBrushProperty);
            // Reapply current props after removing the plain appearance's local defaults.
            CommonProperties.Apply(control, next);
            CommonProperties.ApplyTemplated(control, next);
            return true;
        }

        IBrush background = CommonProperties.ParseBrush(next.Background) ?? Brushes.Transparent;
        control.Background = background;
        if (previous.TextBoxAppearance != next.TextBoxAppearance || previous.Background != next.Background)
        {
            control.Resources[BrushResources[0]] = background;
            control.Resources[BrushResources[1]] = background;
            control.Resources[BrushResources[2]] = Brushes.Transparent;
            control.Resources[BrushResources[3]] = Brushes.Transparent;
            control.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        }
        control.BorderThickness = new Thickness(0);
        control.CornerRadius = new CornerRadius(0);
        control.Padding = new Thickness(0);
        control.MinHeight = 0;
        control.CaretBrush = CommonProperties.ParseBrush(next.Foreground) ?? control.Foreground;
        return previous.TextBoxAppearance != next.TextBoxAppearance ||
            previous.Background != next.Background || previous.Foreground != next.Foreground;
    }
}
