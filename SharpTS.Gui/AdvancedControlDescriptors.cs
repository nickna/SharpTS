using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace SharpTS.Gui;

internal sealed class ItemsHostDescriptor(string kind) : NodeDescriptor(kind, 0, int.MaxValue)
{
    public override Control Create(GuiVNode node)
    {
        Control control = Kind switch
        {
            "ItemsControl" => new ItemsControl(),
            "TreeView" => new TreeView(),
            _ => throw new InvalidOperationException($"Unsupported items host '{Kind}'."),
        };
        Update(control, new GuiVNode(Kind), node);
        return control;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next) =>
        CommonProperties.Apply(control, next);
}

internal sealed class VirtualizingListDescriptor() : NodeDescriptor("VirtualizingList", 0, int.MaxValue)
{
    public override void Validate(GuiVNode node)
    {
        _ = Selection(node.SelectionMode);
        if ((node.SelectedIndices ?? []).Any(index => index < 0))
            throw new ArgumentOutOfRangeException("selectedIndices", "Selected indices must be non-negative.");
    }

    public override Control Create(GuiVNode node)
    {
        var list = new ListBox();
        Update(list, new GuiVNode(Kind), node);
        return list;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var list = (ListBox)control;
        bool changed = CommonProperties.Apply(list, next);
        SelectionMode mode = Selection(next.SelectionMode);
        if (list.SelectionMode != mode) { list.SelectionMode = mode; changed = true; }
        changed |= SynchronizeSelection(list, next);
        return changed;
    }

    internal static bool SynchronizeSelection(ListBox list, GuiVNode next)
    {
        int[] selected = next.SelectedIndices ?? [];
        var selectedItems = list.SelectedItems
            ?? throw new InvalidOperationException("The native virtualizing list has no selection collection.");
        int[] current = selectedItems.Cast<object>()
            .Select(item => list.Items.IndexOf(item)).Where(index => index >= 0).Order().ToArray();
        if (!current.SequenceEqual(selected.Order()))
        {
            selectedItems.Clear();
            foreach (int index in selected)
                if (index < list.Items.Count) selectedItems.Add(list.Items[index]!);
            return true;
        }
        return false;
    }

    private static SelectionMode Selection(string value) => value switch
    {
        "single" => SelectionMode.Single,
        "multiple" => SelectionMode.Multiple,
        _ => throw new ArgumentException($"Unsupported selectionMode '{value}'."),
    };
}

internal sealed class TreeViewItemDescriptor() : NodeDescriptor("TreeViewItem", 0, int.MaxValue)
{
    public override Control Create(GuiVNode node)
    {
        var item = new TreeViewItem();
        Update(item, new GuiVNode(Kind), node);
        return item;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var item = (TreeViewItem)control;
        bool changed = CommonProperties.Apply(item, next);
        if (!Equals(item.Header, next.Header)) { item.Header = next.Header; changed = true; }
        if (item.IsExpanded != next.IsExpanded) { item.IsExpanded = next.IsExpanded; changed = true; }
        return changed;
    }
}

internal sealed class CanvasDescriptor() : NodeDescriptor("Canvas", 0, int.MaxValue)
{
    public override Control Create(GuiVNode node)
    {
        var canvas = new Canvas();
        Update(canvas, new GuiVNode(Kind), node);
        return canvas;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next) =>
        CommonProperties.Apply(control, next);
}

internal sealed class RichTextBlockDescriptor() : NodeDescriptor("RichTextBlock", 0, 0)
{
    public override void Validate(GuiVNode node) => _ = Parse(node.RichTextJson);

    public override Control Create(GuiVNode node)
    {
        var text = new TextBlock();
        Update(text, new GuiVNode(Kind), node);
        return text;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var text = (TextBlock)control;
        bool changed = CommonProperties.Apply(text, next);
        if (string.Equals(previous.RichTextJson, next.RichTextJson, StringComparison.Ordinal))
            return changed;
        text.Inlines!.Clear();
        foreach (RichTextRunModel model in Parse(next.RichTextJson))
        {
            if (model.FontSize is double candidate && (!double.IsFinite(candidate) || candidate <= 0))
                throw new ArgumentOutOfRangeException("runs.fontSize", "Rich text font size must be positive and finite.");
            var run = new Run(model.Text ?? string.Empty);
            if (model.Foreground is not null) run.Foreground = Brush.Parse(model.Foreground);
            if (model.FontSize is double size) run.FontSize = size;
            if (model.FontWeight is not null) run.FontWeight = CommonProperties.ParseFontWeight(model.FontWeight);
            if (model.FontStyle is not null) run.FontStyle = CommonProperties.ParseFontStyle(model.FontStyle);
            text.Inlines.Add(run);
        }
        return true;
    }

    private static RichTextRunModel[] Parse(string? json) =>
        JsonSerializer.Deserialize<RichTextRunModel[]>(json ?? "[]", JsonOptions) ?? [];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private sealed record RichTextRunModel(string? Text, string? Foreground, double? FontSize, string? FontWeight, string? FontStyle);
}

internal sealed class DrawingCanvasDescriptor() : NodeDescriptor("DrawingCanvas", 0, 0)
{
    public override void Validate(GuiVNode node) => DrawingSurface.Parse(node.DrawingJson);

    public override Control Create(GuiVNode node)
    {
        var surface = new DrawingSurface();
        Update(surface, new GuiVNode(Kind), node);
        return surface;
    }

    public override bool Update(Control control, GuiVNode previous, GuiVNode next)
    {
        var surface = (DrawingSurface)control;
        bool changed = CommonProperties.Apply(surface, next);
        if (!string.Equals(previous.DrawingJson, next.DrawingJson, StringComparison.Ordinal))
        {
            surface.Commands = DrawingSurface.Parse(next.DrawingJson);
            changed = true;
        }
        return changed;
    }
}

internal sealed class DrawingSurface : Control
{
    private DrawingModel[] _commands = [];
    public DrawingModel[] Commands
    {
        get => _commands;
        set { _commands = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        foreach (DrawingModel command in _commands)
        {
            IBrush? fill = command.Fill is null ? null : Brush.Parse(command.Fill);
            Pen? pen = command.Stroke is null ? null : new Pen(Brush.Parse(command.Stroke), command.StrokeThickness ?? 1);
            switch (command.Kind)
            {
                case "line":
                    context.DrawLine(
                        pen ?? throw new ArgumentException("A line command requires stroke."),
                        new Point(command.X1, command.Y1), new Point(command.X2, command.Y2));
                    break;
                case "rectangle":
                    context.DrawRectangle(fill, pen, new Rect(command.X, command.Y, command.Width, command.Height));
                    break;
                case "ellipse":
                    context.DrawEllipse(fill, pen, new Point(command.CenterX, command.CenterY), command.RadiusX, command.RadiusY);
                    break;
            }
        }
    }

    public static DrawingModel[] Parse(string? json)
    {
        DrawingModel[] commands = JsonSerializer.Deserialize<DrawingModel[]>(
            json ?? "[]", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        foreach (DrawingModel command in commands)
        {
            if (command.Kind is not ("line" or "rectangle" or "ellipse"))
                throw new ArgumentException($"Unsupported drawing command '{command.Kind}'.");
            if (command.Kind == "line" && command.Stroke is null)
                throw new ArgumentException("A line command requires stroke.");
            foreach (double value in command.Values())
                if (!double.IsFinite(value)) throw new ArgumentException("Drawing coordinates must be finite.");
            if (command.Kind is "rectangle" && (command.Width < 0 || command.Height < 0) ||
                command.Kind is "ellipse" && (command.RadiusX < 0 || command.RadiusY < 0))
                throw new ArgumentException("Drawing dimensions must be non-negative.");
            if (command.StrokeThickness is <= 0)
                throw new ArgumentException("Drawing stroke thickness must be positive.");
            if (command.Fill is not null) _ = Brush.Parse(command.Fill);
            if (command.Stroke is not null) _ = Brush.Parse(command.Stroke);
        }
        return commands;
    }

    internal sealed record DrawingModel(
        string Kind,
        double X,
        double Y,
        double Width,
        double Height,
        double X1,
        double Y1,
        double X2,
        double Y2,
        double CenterX,
        double CenterY,
        double RadiusX,
        double RadiusY,
        string? Fill,
        string? Stroke,
        double? StrokeThickness)
    {
        public IEnumerable<double> Values() => Kind switch
        {
            "line" => [X1, Y1, X2, Y2],
            "rectangle" => [X, Y, Width, Height],
            "ellipse" => [CenterX, CenterY, RadiusX, RadiusY],
            _ => [],
        };
    }
}
