using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SharpTS.Gui;

internal static class DesktopGeometry
{
    internal static Rect WindowBounds(Control control)
    {
        if (control is Window window) return new Rect(window.ClientSize);
        if (TopLevel.GetTopLevel(control) is not Visual root) return default;
        var transform = control.TransformToVisual(root);
        return transform is { } matrix ? new Rect(control.Bounds.Size).TransformToAABB(matrix) : default;
    }

    internal static bool IsVisible(Control control) => control.IsVisible &&
        control.GetVisualAncestors().All(ancestor => ancestor.IsVisible);

    internal static Rect VisibleBounds(Control control)
    {
        if (!IsVisible(control) || TopLevel.GetTopLevel(control) is not TopLevel root) return default;
        Rect bounds = WindowBounds(control).Intersect(new Rect(root.ClientSize));
        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>())
            if (ancestor.ClipToBounds) bounds = bounds.Intersect(WindowBounds(ancestor));
        return bounds;
    }

    internal static bool IsInViewport(Control control)
    {
        Rect bounds = WindowBounds(control), visible = VisibleBounds(control);
        return bounds.Width > 0 && bounds.Height > 0 &&
            Math.Abs(bounds.Width - visible.Width) < 0.5 && Math.Abs(bounds.Height - visible.Height) < 0.5;
    }
}
