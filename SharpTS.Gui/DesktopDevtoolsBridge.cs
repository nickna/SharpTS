using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.Gui;

public static class DesktopDevtoolsBridge
{
    public static string InspectDesktopTreeJson()
    {
        Context.EnsureOwnerThread();
        var document = new InspectorDocument(
            Context.Roots
                .Where(root => !root.IsDisposed)
                .Select(root => root.GetInspectorSnapshot())
                .Where(snapshot => snapshot is not null)
                .Select(snapshot => snapshot!)
                .ToArray());
        return JsonSerializer.Serialize(document, GuiInspectorJsonContext.Default.InspectorDocument);
    }

    public static string CaptureHeadlessSnapshot(string path)
    {
        byte[] png = RenderHeadlessPng();
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, png);
        return SnapshotHash(png);
    }

    public static string AssertHeadlessSnapshot(string baselinePath, bool update)
    {
        byte[] actual = RenderHeadlessPng();
        string fullPath = Path.GetFullPath(baselinePath);
        string actualPath = Path.ChangeExtension(fullPath, ".actual.png");
        if (update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, actual);
            if (File.Exists(actualPath)) File.Delete(actualPath);
            return SnapshotHash(actual);
        }
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                $"Headless visual baseline '{fullPath}' does not exist. Re-run with update enabled to create it.",
                fullPath);
        byte[] expected = File.ReadAllBytes(fullPath);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            File.WriteAllBytes(actualPath, actual);
            throw new InvalidOperationException(
                $"Headless visual snapshot differed from '{fullPath}'. Actual output: '{actualPath}'. " +
                $"Expected SHA-256 {SnapshotHash(expected)}; actual {SnapshotHash(actual)}.");
        }
        if (File.Exists(actualPath)) File.Delete(actualPath);
        return SnapshotHash(actual);
    }

    private static byte[] RenderHeadlessPng()
    {
        Context.EnsureOwnerThread();
        if (!Context.IsHeadless)
            throw new InvalidOperationException("Visual snapshot capture is available only in Headless mode.");
        Window window = Context.CurrentRoot?.Window
            ?? throw new InvalidOperationException("No desktop Window is mounted.");
        using Bitmap bitmap = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The Headless Window did not produce a rendered frame.");
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static string SnapshotHash(byte[] png) =>
        Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();

    private static DesktopRuntimeContext Context => DesktopBridge.RequireContext();
}

internal sealed record InspectorDocument(InspectorNode[] Windows);
internal sealed record InspectorNode(
    string Kind,
    string? Key,
    string? NativeType,
    InspectorSource? Source,
    InspectorBounds Bounds,
    bool IsVisible,
    bool IsEnabled,
    string[] Classes,
    InspectorProps Props,
    InspectorNode[] Children);
internal sealed record InspectorSource(string File, int Line, int Column);
internal sealed record InspectorBounds(double X, double Y, double Width, double Height);
internal sealed record InspectorProps(string? Text, string? Title, double? Width, double? Height);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InspectorDocument))]
internal sealed partial class GuiInspectorJsonContext : JsonSerializerContext;
