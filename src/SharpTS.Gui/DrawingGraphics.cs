using System.Collections;
using System.Text;
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
    private const int MaximumImagePayloadBytes = 25 * 1024 * 1024;

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
        _ = ReadImageInfo(context, source, "The drawing image source is not a supported image.");
    }

    internal static void RenderDocumentToPng(DesktopRuntimeContext context, string documentJson, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        DrawingDocumentModel document = ParseDocument(context, documentJson);
        byte[] png;
        using (SKSurface surface = RenderDocument(context, document))
            png = Encode(surface);

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

    internal static string RenderDocumentToImageJson(
        DesktopRuntimeContext context,
        string documentJson,
        string optionsJson)
    {
        DrawingDocumentModel document = ParseDocument(context, documentJson);
        DrawingRenderOptionsModel options = JsonSerializer.Deserialize(
            optionsJson, GraphicsJsonContext.Default.DrawingRenderOptionsModel)
            ?? new DrawingRenderOptionsModel(null);
        DrawingEffectModel[] effects = options.Effects ?? [];
        ValidateEffects(effects);

        SKSurface surface = RenderDocument(context, document);
        try
        {
            surface = ApplyEffects(surface, effects);
            DrawingImageModel result = CreateDrawingImage(Encode(surface), (int)document.Width, (int)document.Height);
            return JsonSerializer.Serialize(result, GraphicsJsonContext.Default.DrawingImageModel);
        }
        finally
        {
            surface.Dispose();
        }
    }

    internal static string SampleDrawingPixelJson(
        DesktopRuntimeContext context,
        string documentJson,
        double x,
        double y)
    {
        DrawingDocumentModel document = ParseDocument(context, documentJson);
        ValidatePoint(document, x, y);
        using SKSurface surface = RenderDocument(context, document);
        using SKImage image = surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image)
            ?? throw new InvalidOperationException("Could not read the rendered drawing document.");
        SKColor pixel = bitmap.GetPixel((int)Math.Floor(x), (int)Math.Floor(y));
        byte red = pixel.Alpha == 0 ? (byte)0 : pixel.Red;
        byte green = pixel.Alpha == 0 ? (byte)0 : pixel.Green;
        byte blue = pixel.Alpha == 0 ? (byte)0 : pixel.Blue;
        string color = pixel.Alpha == 255
            ? $"#{red:x2}{green:x2}{blue:x2}"
            : $"#{pixel.Alpha:x2}{red:x2}{green:x2}{blue:x2}";
        var result = new DrawingPixelModel(red, green, blue, pixel.Alpha, color);
        return JsonSerializer.Serialize(result, GraphicsJsonContext.Default.DrawingPixelModel);
    }

    internal static string FloodFillDrawingJson(
        DesktopRuntimeContext context,
        string documentJson,
        string optionsJson)
    {
        DrawingDocumentModel document = ParseDocument(context, documentJson);
        DrawingFloodFillOptionsModel options = JsonSerializer.Deserialize(
            optionsJson, GraphicsJsonContext.Default.DrawingFloodFillOptionsModel)
            ?? throw new ArgumentException("Flood-fill options are required.", nameof(optionsJson));
        ValidatePoint(document, options.X, options.Y);
        if (string.IsNullOrWhiteSpace(options.Color))
            throw new ArgumentException("Flood fill requires a replacement color.", nameof(optionsJson));
        if (!double.IsFinite(options.Tolerance) || options.Tolerance < 0 || options.Tolerance > 1)
            throw new ArgumentOutOfRangeException(nameof(optionsJson), "Flood-fill tolerance must be between zero and one.");

        SKColor replacement = Color(options.Color, 255);
        using SKSurface surface = RenderDocument(context, document);
        using SKImage image = surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image)
            ?? throw new InvalidOperationException("Could not read the rendered drawing document.");
        int width = bitmap.Width;
        int height = bitmap.Height;
        int seedX = (int)Math.Floor(options.X);
        int seedY = (int)Math.Floor(options.Y);
        SKColor target = bitmap.GetPixel(seedX, seedY);
        if (target == replacement)
        {
            return JsonSerializer.Serialize(
                new DrawingFloodFillResultModel(false, null),
                GraphicsJsonContext.Default.DrawingFloodFillResultModel);
        }

        int tolerance = (int)Math.Round(options.Tolerance * 255);
        var visited = new BitArray(checked(width * height));
        var seeds = new Stack<int>();
        seeds.Push(seedY * width + seedX);

        bool Eligible(int pixelX, int pixelY)
        {
            int index = pixelY * width + pixelX;
            return !visited[index] && WithinTolerance(bitmap.GetPixel(pixelX, pixelY), target, tolerance);
        }

        while (seeds.Count > 0)
        {
            int seed = seeds.Pop();
            int y = seed / width;
            int x = seed - y * width;
            if (!Eligible(x, y)) continue;

            int left = x;
            while (left > 0 && Eligible(left - 1, y)) left--;
            bool spanAbove = false;
            bool spanBelow = false;
            for (int current = left; current < width && Eligible(current, y); current++)
            {
                int index = y * width + current;
                visited[index] = true;
                bitmap.SetPixel(current, y, replacement);

                if (y > 0)
                {
                    bool eligibleAbove = Eligible(current, y - 1);
                    if (eligibleAbove && !spanAbove) seeds.Push((y - 1) * width + current);
                    spanAbove = eligibleAbove;
                }
                if (y + 1 < height)
                {
                    bool eligibleBelow = Eligible(current, y + 1);
                    if (eligibleBelow && !spanBelow) seeds.Push((y + 1) * width + current);
                    spanBelow = eligibleBelow;
                }
            }
        }

        DrawingImageModel drawingImage = CreateDrawingImage(Encode(bitmap), width, height);
        return JsonSerializer.Serialize(
            new DrawingFloodFillResultModel(true, drawingImage),
            GraphicsJsonContext.Default.DrawingFloodFillResultModel);
    }

    private static DrawingDocumentModel ParseDocument(DesktopRuntimeContext context, string documentJson)
    {
        DrawingDocumentModel document = JsonSerializer.Deserialize(
            documentJson, GraphicsJsonContext.Default.DrawingDocumentModel)
            ?? throw new ArgumentException("Drawing document JSON is empty.", nameof(documentJson));
        ValidateDocument(context, document);
        return document;
    }

    private static SKSurface RenderDocument(DesktopRuntimeContext context, DrawingDocumentModel document)
    {
        int width = ToPixels(document.Width, "width");
        int height = ToPixels(document.Height, "height");
        ValidatePixelCount(width, height);
        SKSurface surface = CreateSurface(width, height);
        try
        {
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(document.Background is null ? SKColors.Transparent : Color(document.Background, 255));
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
            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    private static SKSurface CreateSurface(int width, int height) =>
        SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul))
        ?? throw new InvalidOperationException("Could not allocate the drawing surface.");

    private static byte[] Encode(SKSurface surface)
    {
        using SKImage image = surface.Snapshot();
        return Encode(image);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        return Encode(image);
    }

    private static byte[] Encode(SKImage image)
    {
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode the drawing as PNG.");
        return data.ToArray();
    }

    private static DrawingImageModel CreateDrawingImage(byte[] png, int width, int height)
    {
        if (png.Length > MaximumImagePayloadBytes)
            throw new InvalidDataException("Rendered PNG payloads are limited to 25 MiB.");
        return new DrawingImageModel(
            "data:image/png;base64," + Convert.ToBase64String(png),
            width,
            height);
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
                case "text":
                    DrawText(canvas, paint, command, alpha);
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

    private static void DrawText(SKCanvas canvas, SKPaint paint, DrawingSurface.DrawingModel command, byte alpha)
    {
        SKFontStyle typefaceStyle = command.FontWeight is "bold" or "semibold"
            ? command.FontStyle == "italic" ? SKFontStyle.BoldItalic : SKFontStyle.Bold
            : command.FontStyle == "italic" ? SKFontStyle.Italic : SKFontStyle.Normal;
        string? family = command.FontFamily is null or "" or "sans-serif" ? null : command.FontFamily;
        SKTypeface? ownedTypeface = SKTypeface.FromFamilyName(family, typefaceStyle);
        SKTypeface typeface = ownedTypeface ?? SKTypeface.Default;
        try
        {
            paint.Style = SKPaintStyle.Fill;
            paint.Color = Color(command.Fill!, alpha);
            using var font = new SKFont(typeface, (float)command.FontSize);
            var bounds = new SKRect(
                (float)command.X,
                (float)command.Y,
                (float)(command.X + command.Width),
                (float)(command.Y + command.Height));
            canvas.Save();
            try
            {
                canvas.ClipRect(bounds);
                SKFontMetrics metrics = font.Metrics;
                float lineHeight = Math.Max(1, metrics.Descent - metrics.Ascent + metrics.Leading);
                float baseline = bounds.Top - metrics.Ascent;
                foreach (string line in LayoutTextLines(font, command.Text!, bounds.Width, command.TextWrapping == "wrap"))
                {
                    if (baseline + metrics.Descent > bounds.Bottom) break;
                    float lineWidth = font.MeasureText(line);
                    float x = command.TextAlignment switch
                    {
                        "center" => bounds.Left + (bounds.Width - lineWidth) / 2,
                        "right" => bounds.Right - lineWidth,
                        _ => bounds.Left,
                    };
                    canvas.DrawText(line, x, baseline, SKTextAlign.Left, font, paint);
                    baseline += lineHeight;
                }
            }
            finally
            {
                canvas.Restore();
            }
        }
        finally
        {
            ownedTypeface?.Dispose();
        }
    }

    private static IEnumerable<string> LayoutTextLines(SKFont font, string text, float width, bool wrap)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (string paragraph in normalized.Split('\n'))
        {
            if (!wrap || paragraph.Length == 0)
            {
                yield return paragraph;
                continue;
            }

            string[] words = paragraph.Split(' ', StringSplitOptions.None);
            var line = new StringBuilder();
            foreach (string word in words)
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length == 0 || font.MeasureText(candidate) <= width)
                {
                    line.Clear();
                    line.Append(candidate);
                    continue;
                }

                yield return line.ToString();
                line.Clear();
                if (font.MeasureText(word) <= width)
                {
                    line.Append(word);
                    continue;
                }

                var segment = new StringBuilder();
                foreach (char character in word)
                {
                    string expanded = segment.ToString() + character;
                    if (segment.Length > 0 && font.MeasureText(expanded) > width)
                    {
                        yield return segment.ToString();
                        segment.Clear();
                    }
                    segment.Append(character);
                }
                line.Append(segment);
            }
            yield return line.ToString();
        }
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

    private static SKSurface ApplyEffects(SKSurface surface, DrawingEffectModel[] effects)
    {
        SKSurface current = surface;
        try
        {
            foreach (DrawingEffectModel effect in effects)
            {
                if (effect.Kind == "gaussianBlur" && effect.Radius == 0) continue;
                using SKImage image = current.Snapshot();
                SKSurface next = CreateSurface(image.Width, image.Height);
                try
                {
                    next.Canvas.Clear(SKColors.Transparent);
                    using var paint = new SKPaint { IsAntialias = true };
                    using SKImageFilter? imageFilter = effect.Kind == "gaussianBlur"
                        ? SKImageFilter.CreateBlur((float)effect.Radius, (float)effect.Radius)
                        : null;
                    using SKColorFilter? colorFilter = CreateEffectColorFilter(effect);
                    paint.ImageFilter = imageFilter;
                    paint.ColorFilter = colorFilter;
                    next.Canvas.DrawImage(image, 0, 0, paint);
                }
                catch
                {
                    next.Dispose();
                    throw;
                }
                current.Dispose();
                current = next;
            }
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static SKColorFilter? CreateEffectColorFilter(DrawingEffectModel effect)
    {
        float[]? matrix = effect.Kind switch
        {
            "grayscale" => SaturationMatrix(0),
            "invert" =>
            [
                -1, 0, 0, 0, 1,
                0, -1, 0, 0, 1,
                0, 0, -1, 0, 1,
                0, 0, 0, 1, 0,
            ],
            "brightnessContrast" => BrightnessContrastMatrix(effect.Brightness, effect.Contrast),
            "hueSaturation" => MultiplyColorMatrices(
                SaturationMatrix(1 + effect.Saturation / 100),
                HueMatrix(effect.Hue)),
            _ => null,
        };
        return matrix is null ? null : SKColorFilter.CreateColorMatrix(matrix);
    }

    private static float[] BrightnessContrastMatrix(double brightness, double contrast)
    {
        float factor = (float)(1 + contrast / 100);
        float offset = (float)(brightness / 100 + 0.5 * (1 - factor));
        return
        [
            factor, 0, 0, 0, offset,
            0, factor, 0, 0, offset,
            0, 0, factor, 0, offset,
            0, 0, 0, 1, 0,
        ];
    }

    private static float[] SaturationMatrix(double saturation)
    {
        const float red = 0.213f;
        const float green = 0.715f;
        const float blue = 0.072f;
        float inverse = (float)(1 - saturation);
        float value = (float)saturation;
        return
        [
            red * inverse + value, green * inverse, blue * inverse, 0, 0,
            red * inverse, green * inverse + value, blue * inverse, 0, 0,
            red * inverse, green * inverse, blue * inverse + value, 0, 0,
            0, 0, 0, 1, 0,
        ];
    }

    private static float[] HueMatrix(double hue)
    {
        double radians = hue * Math.PI / 180;
        float cosine = (float)Math.Cos(radians);
        float sine = (float)Math.Sin(radians);
        return
        [
            0.213f + cosine * 0.787f - sine * 0.213f,
            0.715f - cosine * 0.715f - sine * 0.715f,
            0.072f - cosine * 0.072f + sine * 0.928f, 0, 0,
            0.213f - cosine * 0.213f + sine * 0.143f,
            0.715f + cosine * 0.285f + sine * 0.140f,
            0.072f - cosine * 0.072f - sine * 0.283f, 0, 0,
            0.213f - cosine * 0.213f - sine * 0.787f,
            0.715f - cosine * 0.715f + sine * 0.715f,
            0.072f + cosine * 0.928f + sine * 0.072f, 0, 0,
            0, 0, 0, 1, 0,
        ];
    }

    private static float[] MultiplyColorMatrices(float[] first, float[] second)
    {
        var result = new float[20];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float value = 0;
                for (int index = 0; index < 4; index++)
                    value += first[row * 5 + index] * second[index * 5 + column];
                result[row * 5 + column] = value;
            }
            float offset = first[row * 5 + 4];
            for (int index = 0; index < 4; index++)
                offset += first[row * 5 + index] * second[index * 5 + 4];
            result[row * 5 + 4] = offset;
        }
        return result;
    }

    private static void ValidateEffects(DrawingEffectModel[] effects)
    {
        if (effects.Length > 16)
            throw new ArgumentException("A drawing render supports at most 16 effects.");
        foreach (DrawingEffectModel effect in effects)
        {
            switch (effect.Kind)
            {
                case "gaussianBlur" when double.IsFinite(effect.Radius) && effect.Radius is >= 0 and <= 64:
                case "grayscale":
                case "invert":
                    break;
                case "brightnessContrast" when
                    double.IsFinite(effect.Brightness) && effect.Brightness is >= -100 and <= 100 &&
                    double.IsFinite(effect.Contrast) && effect.Contrast is >= -100 and <= 100:
                    break;
                case "hueSaturation" when
                    double.IsFinite(effect.Hue) && effect.Hue is >= -180 and <= 180 &&
                    double.IsFinite(effect.Saturation) && effect.Saturation is >= -100 and <= 100:
                    break;
                default:
                    throw new ArgumentException($"Drawing effect '{effect.Kind}' has invalid parameters.");
            }
        }
    }

    private static bool WithinTolerance(SKColor value, SKColor target, int tolerance) =>
        Math.Abs(value.Red - target.Red) <= tolerance &&
        Math.Abs(value.Green - target.Green) <= tolerance &&
        Math.Abs(value.Blue - target.Blue) <= tolerance &&
        Math.Abs(value.Alpha - target.Alpha) <= tolerance;

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

    private static void ValidatePoint(DrawingDocumentModel document, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= document.Width || y >= document.Height)
            throw new ArgumentOutOfRangeException("x/y", "Drawing points must be inside the document bounds.");
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
    internal sealed record DrawingImageModel(string Source, int Width, int Height);
    internal sealed record DrawingPixelModel(byte Red, byte Green, byte Blue, byte Alpha, string Color);
    internal sealed record DrawingRenderOptionsModel(DrawingEffectModel[]? Effects);
    internal sealed record DrawingEffectModel(
        string Kind,
        double Radius,
        double Brightness,
        double Contrast,
        double Hue,
        double Saturation);
    internal sealed record DrawingFloodFillOptionsModel(double X, double Y, string? Color, double Tolerance);
    internal sealed record DrawingFloodFillResultModel(bool Changed, DrawingImageModel? Image);

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ImageDimensions))]
    [JsonSerializable(typeof(DrawingDocumentModel))]
    [JsonSerializable(typeof(DrawingSurface.DrawingModel[]))]
    [JsonSerializable(typeof(DrawingImageModel))]
    [JsonSerializable(typeof(DrawingPixelModel))]
    [JsonSerializable(typeof(DrawingRenderOptionsModel))]
    [JsonSerializable(typeof(DrawingFloodFillOptionsModel))]
    [JsonSerializable(typeof(DrawingFloodFillResultModel))]
    private sealed partial class GraphicsJsonContext : JsonSerializerContext;
}
