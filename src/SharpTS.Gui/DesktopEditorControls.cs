using Avalonia.Controls;
using Avalonia.Media;

namespace SharpTS.Gui;

internal sealed class PathIconDescriptor() : NodeDescriptor("PathIcon", 0, 0)
{
    public override void Validate(GuiVNode node) => _ = Geometry.Parse(node.Text ?? "");
    public override Control Create(GuiVNode node)
    {
        var icon = new PathIcon();
        Update(icon, new GuiVNode(Kind), node);
        return icon;
    }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var icon = (PathIcon)control;
        bool changed = CommonProperties.Apply(icon, next) | CommonProperties.ApplyTemplated(icon, next);
        if (previous.Text != next.Text) { icon.Data = Geometry.Parse(next.Text ?? ""); changed = true; }
        return changed;
    }
}

internal sealed class ColorViewDescriptor(string kind) : NodeDescriptor(kind, 0, 0)
{
    public override void Validate(GuiVNode node) => _ = Color.Parse(node.Text ?? "#000000");
    public override Control Create(GuiVNode node)
    {
        ColorView view = Kind == "ColorPicker" ? new ColorPicker() : new ColorView();
        view.HexInputAlphaPosition = AlphaComponentPosition.Leading;
        Update(view, new GuiVNode(Kind), node);
        return view;
    }
    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var view = (ColorView)control;
        bool changed = CommonProperties.Apply(view, next) | CommonProperties.ApplyTemplated(view, next);
        Color color = Color.Parse(next.Text ?? "#000000");
        if (view.Color != color) { view.Color = color; changed = true; }
        return changed;
    }
}
