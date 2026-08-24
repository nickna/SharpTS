using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

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

internal sealed partial class RichTextBlockDescriptor() : NodeDescriptor("RichTextBlock", 0, 0)
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
        JsonSerializer.Deserialize(json ?? "[]", RichTextJsonContext.Default.RichTextRunModelArray) ?? [];
    private sealed record RichTextRunModel(string? Text, string? Foreground, double? FontSize, string? FontWeight, string? FontStyle);
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(RichTextRunModel[]))]
    private sealed partial class RichTextJsonContext : JsonSerializerContext;
}

internal sealed class DrawingCanvasDescriptor() : NodeDescriptor("DrawingCanvas", 0, 0)
{
    public override void Validate(GuiVNode node)
    {
        DrawingSurface.DrawingModel[] commands = DrawingSurface.Parse(node.DrawingJson);
        bool hasWidth = !double.IsNaN(node.CoordinateWidth);
        bool hasHeight = !double.IsNaN(node.CoordinateHeight);
        if (hasWidth != hasHeight)
            throw new ArgumentException("coordinateWidth and coordinateHeight must be supplied together.");
        if (hasWidth)
        {
            DrawingGraphics.ValidateSurfaceDimensions(node.CoordinateWidth, node.CoordinateHeight);
        }
        else if (commands.Any(command => command.Composite == "destinationOut"))
            throw new ArgumentException("destinationOut drawing commands require logical coordinate dimensions.");
    }

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
        if (!surface.CoordinateWidth.Equals(next.CoordinateWidth) || !surface.CoordinateHeight.Equals(next.CoordinateHeight))
        {
            surface.CoordinateWidth = next.CoordinateWidth;
            surface.CoordinateHeight = next.CoordinateHeight;
            changed = true;
        }
        return changed;
    }
}

internal sealed partial class DrawingSurface : Control, Avalonia.Rendering.ICustomHitTest
{
    private DrawingModel[] _commands = [];
    private Bitmap? _bitmap;
    private double _coordinateWidth = double.NaN;
    private double _coordinateHeight = double.NaN;
    public DrawingModel[] Commands
    {
        get => _commands;
        set { _commands = value; ResetBitmap(); }
    }
    public double CoordinateWidth { get => _coordinateWidth; set { _coordinateWidth = value; ResetBitmap(); } }
    public double CoordinateHeight { get => _coordinateHeight; set { _coordinateHeight = value; ResetBitmap(); } }

    public DrawingSurface() => DetachedFromVisualTree += OnDetachedFromVisualTree;

    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (double.IsFinite(CoordinateWidth) && double.IsFinite(CoordinateHeight))
        {
            _bitmap ??= DrawingGraphics.RenderBitmap(CoordinateWidth, CoordinateHeight, _commands);
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), new Rect(Bounds.Size));
            return;
        }
        foreach (DrawingModel command in _commands)
            DrawingGraphics.DrawVector(context, command);
    }

    private void ResetBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        InvalidateVisual();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public static DrawingModel[] Parse(string? json, DesktopRuntimeContext? context = null)
    {
        DrawingModel[] commands = JsonSerializer.Deserialize(
            json ?? "[]", DrawingJsonContext.Default.DrawingModelArray) ?? [];
        foreach (DrawingModel command in commands)
        {
            if (command.Kind is not ("line" or "rectangle" or "ellipse" or "polyline" or "image"))
                throw new ArgumentException($"Unsupported drawing command '{command.Kind}'.");
            if (command.Kind is "line" or "polyline" && command.Stroke is null)
                throw new ArgumentException($"A {command.Kind} command requires stroke.");
            if (command.Kind == "polyline" && (command.Points is null || command.Points.Length == 0))
                throw new ArgumentException("A polyline command requires at least one point.");
            if (command.Kind == "image" && string.IsNullOrWhiteSpace(command.Source))
                throw new ArgumentException("An image command requires a source.");
            if (command.Kind == "image") DrawingGraphics.ValidateImageSource(
                context ?? DesktopBridge.RequireContext(), command.Source!);
            foreach (double value in command.Values())
                if (!double.IsFinite(value)) throw new ArgumentException("Drawing coordinates must be finite.");
            if (command.Kind is "rectangle" && (command.Width < 0 || command.Height < 0) ||
                command.Kind is "image" && (command.Width <= 0 || command.Height <= 0) ||
                command.Kind is "ellipse" && (command.RadiusX < 0 || command.RadiusY < 0))
                throw new ArgumentException("Drawing dimensions are invalid.");
            if (command.Kind == "polyline" && command.Points!.Length > 10_000)
                throw new ArgumentException("A polyline command supports at most 10,000 points.");
            if (command.StrokeThickness is <= 0)
                throw new ArgumentException("Drawing stroke thickness must be positive.");
            if (command.Opacity is double opacity && (!double.IsFinite(opacity) || opacity < 0 || opacity > 1))
                throw new ArgumentException("Drawing opacity must be between zero and one.");
            if (command.Composite is not (null or "sourceOver" or "destinationOut"))
                throw new ArgumentException($"Unsupported drawing composite mode '{command.Composite}'.");
            if (command.Kind == "image" && command.Composite == "destinationOut")
                throw new ArgumentException("Image commands do not support destinationOut compositing.");
            if (command.LineCap is not (null or "butt" or "round" or "square"))
                throw new ArgumentException($"Unsupported line cap '{command.LineCap}'.");
            if (command.LineJoin is not (null or "miter" or "round" or "bevel"))
                throw new ArgumentException($"Unsupported line join '{command.LineJoin}'.");
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
        DrawingPointModel[]? Points,
        string? Source,
        string? Fill,
        string? Stroke,
        double? StrokeThickness,
        double? Opacity,
        string? Composite,
        string? LineCap,
        string? LineJoin)
    {
        public IEnumerable<double> Values() => Kind switch
        {
            "line" => [X1, Y1, X2, Y2],
            "rectangle" => [X, Y, Width, Height],
            "ellipse" => [CenterX, CenterY, RadiusX, RadiusY],
            "polyline" => (Points ?? []).SelectMany(point => new[] { point.X, point.Y }),
            "image" => [X, Y, Width, Height],
            _ => [],
        };
    }
    internal sealed record DrawingPointModel(double X, double Y);

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(DrawingModel[]))]
    private sealed partial class DrawingJsonContext : JsonSerializerContext;
}
