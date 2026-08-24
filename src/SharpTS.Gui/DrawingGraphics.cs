using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace SharpTS.Gui;

internal static partial class DrawingGraphics
{
    private const int MaximumDimension = 8192;
    private const long MaximumPixelCount = (long)MaximumDimension * MaximumDimension;

    internal static void ValidateSurfaceDimensions(double width, double height)
    {
        int pixelWidth = ToPixels(width, nameof(width));
        int pixelHeight = ToPixels(height, nameof(height));
        ValidatePixelCount(pixelWidth, pixelHeight);
    }

    internal static Bitmap RenderBitmap(double width, double height, DrawingSurface.DrawingModel[] commands)
    {
        int pixelWidth = ToPixels(width, nameof(width));
        int pixelHeight = ToPixels(height, nameof(height));
        ValidatePixelCount(pixelWidth, pixelHeight);
        using SKSurface surface = CreateSurface(pixelWidth, pixelHeight);
        surface.Canvas.Clear(SKColors.Transparent);
        DrawCommands(DesktopBridge.RequireContext(), surface.Canvas, commands);

        var bitmap = new WriteableBitmap(
            new PixelSize(pixelWidth, pixelHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        try
        {
            using ILockedFramebuffer framebuffer = bitmap.Lock();
            var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            if (!surface.ReadPixels(info, framebuffer.Address, framebuffer.RowBytes, 0, 0))
                throw new InvalidOperationException("Could not copy the rendered drawing surface.");
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal static void DrawVector(DrawingContext context, DrawingSurface.DrawingModel command)
    {
        using IDisposable? opacity = command.Opacity is double value && value < 1
            ? context.PushOpacity(value)
            : null;
        IBrush? fill = command.Fill is null ? null : Brush.Parse(command.Fill);
        Pen? pen = command.Stroke is null ? null : new Pen(
            Brush.Parse(command.Stroke), command.StrokeThickness ?? 1, lineCap: LineCap(command.LineCap), lineJoin: LineJoin(command.LineJoin));
        switch (command.Kind)
        {
            case "line":
                context.DrawLine(pen!, new Point(command.X1, command.Y1), new Point(command.X2, command.Y2));
                break;
            case "rectangle":
                context.DrawRectangle(fill, pen, new Rect(command.X, command.Y, command.Width, command.Height));
                break;
            case "ellipse":
                context.DrawEllipse(fill, pen, new Point(command.CenterX, command.CenterY), command.RadiusX, command.RadiusY);
                break;
            case "polyline":
            {
                DrawingSurface.DrawingPointModel[] points = command.Points!;
                if (points.Length == 1)
                {
                    double radius = (command.StrokeThickness ?? 1) / 2;
                    context.DrawEllipse(Brush.Parse(command.Stroke!), null, new Point(points[0].X, points[0].Y), radius, radius);
                    break;
                }
                var geometry = new StreamGeometry();
                using (StreamGeometryContext geometryContext = geometry.Open())
                {
                    geometryContext.BeginFigure(new Point(points[0].X, points[0].Y), false);
                    for (int index = 1; index < points.Length; index++)
                        geometryContext.LineTo(new Point(points[index].X, points[index].Y));
                    geometryContext.EndFigure(false);
                }
                context.DrawGeometry(null, pen, geometry);
                break;
            }
            case "image":
            {
                using Stream source = DesktopBridge.RequireContext().OpenImageStream(command.Source!);
                using var bitmap = new Bitmap(source);
                context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(command.X, command.Y, command.Width, command.Height));
                break;
            }
        }
    }

    internal static string GetImageDimensionsJson(DesktopRuntimeContext context, string source)
    {
        SKImageInfo info = ReadImageInfo(context, source, "The selected file is not a supported image.");
        return JsonSerializer.Serialize(new ImageDimensions(info.Width, info.Height), GraphicsJsonContext.Default.ImageDimensions);
    }

    internal static void ValidateImageSource(DesktopRuntimeContext context, string source)
    {
        _ = ReadImageInfo(
            context, source,
            "The drawing image source is not a supported image.");
    }

    internal static void RenderDocumentToPng(DesktopRuntimeContext context, string documentJson, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        DrawingDocumentModel document = JsonSerializer.Deserialize(
            documentJson, GraphicsJsonContext.Default.DrawingDocumentModel)
            ?? throw new ArgumentException("Drawing document JSON is empty.", nameof(documentJson));
        ValidateDocument(context, document);
        int width = ToPixels(document.Width, "width");
        int height = ToPixels(document.Height, "height");
        ValidatePixelCount(width, height);
        byte[] png;
        using (SKSurface surface = CreateSurface(width, height))
        {
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(document.Background is null ? SKColors.Transparent : Color(document.Background, (byte)255));
            foreach (DrawingLayerModel layer in document.Layers)
            {
                if (!layer.IsVisible || layer.Opacity <= 0) continue;
                using SKSurface layerSurface = CreateSurface(width, height);
                layerSurface.Canvas.Clear(SKColors.Transparent);
                DrawCommands(context, layerSurface.Canvas, layer.Commands);
                using SKImage image = layerSurface.Snapshot();
                using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255)) };
                canvas.DrawImage(image, 0, 0, paint);
            }
            png = Encode(surface);
        }

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".sharpts-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, png);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static SKSurface CreateSurface(int width, int height) =>
        SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul))
        ?? throw new InvalidOperationException("Could not allocate the drawing surface.");

    private static byte[] Encode(SKSurface surface)
    {
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode the drawing as PNG.");
        return data.ToArray();
    }

    private static void DrawCommands(
        DesktopRuntimeContext context,
        SKCanvas canvas,
        DrawingSurface.DrawingModel[] commands)
    {
        foreach (DrawingSurface.DrawingModel command in commands)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                BlendMode = command.Composite == "destinationOut" ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
                StrokeWidth = (float)(command.StrokeThickness ?? 1),
                StrokeCap = command.LineCap switch { "butt" => SKStrokeCap.Butt, "square" => SKStrokeCap.Square, _ => SKStrokeCap.Round },
                StrokeJoin = command.LineJoin switch { "miter" => SKStrokeJoin.Miter, "bevel" => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Round },
            };
            byte alpha = (byte)Math.Round((command.Opacity ?? 1) * 255);
            switch (command.Kind)
            {
                case "line":
                    paint.Style = SKPaintStyle.Stroke;
                    paint.Color = Color(command.Stroke!, alpha);
                    canvas.DrawLine((float)command.X1, (float)command.Y1, (float)command.X2, (float)command.Y2, paint);
                    break;
                case "rectangle":
                    DrawShape(canvas, paint, command, new SKRect((float)command.X, (float)command.Y,
                        (float)(command.X + command.Width), (float)(command.Y + command.Height)), ellipse: false, alpha);
                    break;
                case "ellipse":
                    DrawShape(canvas, paint, command, new SKRect((float)(command.CenterX - command.RadiusX), (float)(command.CenterY - command.RadiusY),
                        (float)(command.CenterX + command.RadiusX), (float)(command.CenterY + command.RadiusY)), ellipse: true, alpha);
                    break;
                case "polyline":
                    DrawPolyline(canvas, paint, command, alpha);
                    break;
                case "image":
                    DrawImage(context, canvas, paint, command, alpha);
                    break;
            }
        }
    }

    private static void DrawShape(SKCanvas canvas, SKPaint paint, DrawingSurface.DrawingModel command, SKRect bounds, bool ellipse, byte alpha)
    {
        if (command.Fill is not null)
        {
            paint.Style = SKPaintStyle.Fill;
            paint.Color = Color(command.Fill, alpha);
            if (ellipse) canvas.DrawOval(bounds, paint); else canvas.DrawRect(bounds, paint);
        }
        if (command.Stroke is not null)
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.Color = Color(command.Stroke, alpha);
            if (ellipse) canvas.DrawOval(bounds, paint); else canvas.DrawRect(bounds, paint);
        }
    }

    private static void DrawPolyline(SKCanvas canvas, SKPaint paint, DrawingSurface.DrawingModel command, byte alpha)
    {
        DrawingSurface.DrawingPointModel[] points = command.Points!;
        paint.Color = Color(command.Stroke!, alpha);
        if (points.Length == 1)
        {
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawCircle((float)points[0].X, (float)points[0].Y, paint.StrokeWidth / 2, paint);
            return;
        }
        paint.Style = SKPaintStyle.Stroke;
        using var path = new SKPath();
        path.MoveTo((float)points[0].X, (float)points[0].Y);
        for (int index = 1; index < points.Length; index++)
            path.LineTo((float)points[index].X, (float)points[index].Y);
        canvas.DrawPath(path, paint);
    }

    private static void DrawImage(
        DesktopRuntimeContext context,
        SKCanvas canvas,
        SKPaint paint,
        DrawingSurface.DrawingModel command,
        byte alpha)
    {
        using Stream stream = context.OpenImageStream(command.Source!);
        using SKBitmap bitmap = SKBitmap.Decode(stream)
            ?? throw new InvalidDataException($"Drawing image source '{command.Source}' is invalid.");
        paint.Color = SKColors.White.WithAlpha(alpha);
        canvas.DrawBitmap(bitmap, new SKRect((float)command.X, (float)command.Y,
            (float)(command.X + command.Width), (float)(command.Y + command.Height)), paint);
    }

    private static SKColor Color(string value, byte alpha)
    {
        Avalonia.Media.Color color = Avalonia.Media.Color.Parse(value);
        return new SKColor(color.R, color.G, color.B, (byte)(color.A * alpha / 255));
    }

    private static PenLineCap LineCap(string? value) => value switch
    {
        "butt" => PenLineCap.Flat,
        "square" => PenLineCap.Square,
        _ => PenLineCap.Round,
    };
    private static PenLineJoin LineJoin(string? value) => value switch
    {
        "miter" => PenLineJoin.Miter,
        "bevel" => PenLineJoin.Bevel,
        _ => PenLineJoin.Round,
    };

    private static int ToPixels(double value, string name)
    {
        if (!double.IsFinite(value) || value < 1 || value > MaximumDimension || value != Math.Truncate(value))
            throw new ArgumentOutOfRangeException(name, $"Drawing dimensions must be whole numbers between 1 and {MaximumDimension} pixels.");
        return checked((int)value);
    }

    private static void ValidatePixelCount(int width, int height)
    {
        if ((long)width * height > MaximumPixelCount)
            throw new ArgumentOutOfRangeException(
                "width/height",
                $"Drawing surfaces are limited to {MaximumPixelCount:N0} pixels.");
    }

    private static SKImageInfo ReadImageInfo(
        DesktopRuntimeContext context,
        string source,
        string invalidMessage)
    {
        using Stream stream = context.OpenImageStream(source);
        using SKCodec codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException(invalidMessage);
        SKImageInfo info = codec.Info;
        if (info.Width < 1 || info.Height < 1 || info.Width > MaximumDimension || info.Height > MaximumDimension)
            throw new InvalidDataException(
                $"Image dimensions must be between 1 and {MaximumDimension} pixels per axis.");
        ValidatePixelCount(info.Width, info.Height);
        if (source.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase) &&
            codec.EncodedFormat != SKEncodedImageFormat.Png)
            throw new InvalidDataException("The embedded image payload is not a PNG image.");
        return info;
    }

    private static void ValidateDocument(DesktopRuntimeContext context, DrawingDocumentModel document)
    {
        _ = ToPixels(document.Width, "width");
        _ = ToPixels(document.Height, "height");
        if (document.Layers is null || document.Layers.Length > 64)
            throw new ArgumentException("A drawing document supports at most 64 layers.");
        if (document.Background is not null) _ = Avalonia.Media.Color.Parse(document.Background);
        int commandCount = 0;
        foreach (DrawingLayerModel layer in document.Layers)
        {
            if (!double.IsFinite(layer.Opacity) || layer.Opacity < 0 || layer.Opacity > 1)
                throw new ArgumentException("Layer opacity must be between zero and one.");
            if (layer.Commands is null)
                throw new ArgumentException("Every drawing layer requires a command list.");
            DrawingSurface.Parse(
                JsonSerializer.Serialize(layer.Commands, GraphicsJsonContext.Default.DrawingModelArray),
                context);
            commandCount += layer.Commands.Length;
            if (commandCount > 100_000)
                throw new ArgumentException("A drawing document supports at most 100,000 commands.");
        }
    }

    internal sealed record ImageDimensions(double Width, double Height);
    internal sealed record DrawingLayerModel(bool IsVisible, double Opacity, DrawingSurface.DrawingModel[] Commands);
    internal sealed record DrawingDocumentModel(double Width, double Height, string? Background, DrawingLayerModel[] Layers);

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ImageDimensions))]
    [JsonSerializable(typeof(DrawingDocumentModel))]
    [JsonSerializable(typeof(DrawingSurface.DrawingModel[]))]
    private sealed partial class GraphicsJsonContext : JsonSerializerContext;
}
