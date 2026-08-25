using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;

namespace SharpTS.Gui;

internal interface IDesktopInteractionServices
{
    bool SupportsHeadless { get; }
    Task<string> ShowMessageAsync(Window owner, string title, string message, string buttons);
    Task<string[]> OpenFilesAsync(Window owner, string title, bool allowMultiple, string filtersJson);
    Task<string?> SaveFileAsync(Window owner, string title, string suggestedFileName, string defaultExtension, string filtersJson);
    Task<string?> OpenFolderAsync(Window owner, string title);
    Task<string> ReadClipboardAsync(Window owner);
    Task WriteClipboardAsync(Window owner, string value);
}

internal sealed class NativeDesktopInteractionServices : IDesktopInteractionServices
{
    public static NativeDesktopInteractionServices Instance { get; } = new();

    private NativeDesktopInteractionServices() { }

    public bool SupportsHeadless => false;

    public Task<string> ShowMessageAsync(
        Window owner, string title, string message, string buttons) =>
        DesktopServices.ShowMessageAsync(owner, title, message, buttons);

    public Task<string[]> OpenFilesAsync(
        Window owner, string title, bool allowMultiple, string filtersJson) =>
        DesktopServices.OpenFilesAsync(owner, title, allowMultiple, filtersJson);

    public Task<string?> SaveFileAsync(
        Window owner, string title, string suggestedFileName,
        string defaultExtension, string filtersJson) =>
        DesktopServices.SaveFileAsync(
            owner, title, suggestedFileName, defaultExtension, filtersJson);

    public Task<string?> OpenFolderAsync(Window owner, string title) =>
        DesktopServices.OpenFolderAsync(owner, title);

    public Task<string> ReadClipboardAsync(Window owner) =>
        DesktopServices.ReadClipboardAsync(owner);

    public Task WriteClipboardAsync(Window owner, string value) =>
        DesktopServices.WriteClipboardAsync(owner, value);
}

internal sealed class ScriptedDesktopInteractionServices : IDesktopInteractionServices
{
    private readonly Queue<string> _messageResults = [];
    private readonly Queue<string[]> _openResults = [];
    private readonly Queue<string?> _saveResults = [];
    private readonly Queue<string?> _folderResults = [];
    private string _clipboard = string.Empty;

    public bool SupportsHeadless => true;

    public void EnqueueMessageResult(string result) => _messageResults.Enqueue(result);
    public void EnqueueOpenResult(string[] paths) => _openResults.Enqueue(paths.ToArray());
    public void EnqueueSaveResult(string? path) => _saveResults.Enqueue(path);
    public void EnqueueFolderResult(string? path) => _folderResults.Enqueue(path);

    public Task<string> ShowMessageAsync(
        Window owner, string title, string message, string buttons) =>
        Task.FromResult(Dequeue(_messageResults, "message dialog"));

    public Task<string[]> OpenFilesAsync(
        Window owner, string title, bool allowMultiple, string filtersJson) =>
        Task.FromResult(Dequeue(_openResults, "open-file dialog").ToArray());

    public Task<string?> SaveFileAsync(
        Window owner, string title, string suggestedFileName,
        string defaultExtension, string filtersJson) =>
        Task.FromResult(Dequeue(_saveResults, "save-file dialog"));

    public Task<string?> OpenFolderAsync(Window owner, string title) =>
        Task.FromResult(Dequeue(_folderResults, "folder dialog"));

    public Task<string> ReadClipboardAsync(Window owner) => Task.FromResult(_clipboard);

    public Task WriteClipboardAsync(Window owner, string value)
    {
        _clipboard = value;
        return Task.CompletedTask;
    }

    private static T Dequeue<T>(Queue<T> queue, string service)
    {
        if (queue.Count == 0)
        {
            throw new InvalidOperationException(
                $"No Headless result was queued for the next {service}.");
        }
        return queue.Dequeue();
    }
}

internal static partial class DesktopServices
{
    public static async Task<string> ShowMessageAsync(Window owner, string title, string message, string buttons)
    {
        string result = buttons == "yesNo" ? "no" : buttons == "okCancel" ? "cancel" : "ok";
        var dialog = new Window
        {
            Title = string.IsNullOrWhiteSpace(title) ? owner.Title : title,
            Width = 420,
            MinHeight = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 18 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach ((string text, string value) in Buttons(buttons))
        {
            var button = new Button { Content = text, MinWidth = 80, IsDefault = value is "ok" or "yes", IsCancel = value is "cancel" or "no" };
            button.Click += (_, _) => { result = value; dialog.Close(); };
            actions.Children.Add(button);
        }
        panel.Children.Add(actions);
        dialog.Content = panel;
        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<string[]> OpenFilesAsync(Window owner, string title, bool allowMultiple, string filtersJson)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Empty(title), AllowMultiple = allowMultiple, FileTypeFilter = Filters(filtersJson),
        });
        return files.Select(file => file.TryGetLocalPath()).Where(path => path is not null).Cast<string>().ToArray();
    }

    public static async Task<string?> SaveFileAsync(Window owner, string title, string suggestedFileName, string defaultExtension, string filtersJson)
    {
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Empty(title), SuggestedFileName = Empty(suggestedFileName), DefaultExtension = Empty(defaultExtension),
            FileTypeChoices = Filters(filtersJson),
        });
        return file?.TryGetLocalPath();
    }

    public static async Task<string?> OpenFolderAsync(Window owner, string title)
    {
        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Empty(title), AllowMultiple = false,
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string> ReadClipboardAsync(Window owner)
    {
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard
            ?? throw new InvalidOperationException("The system clipboard is unavailable.");
        return await clipboard.TryGetTextAsync() ?? string.Empty;
    }

    public static async Task WriteClipboardAsync(Window owner, string value)
    {
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard
            ?? throw new InvalidOperationException("The system clipboard is unavailable.");
        await clipboard.SetTextAsync(value);
    }

    private static IReadOnlyList<FilePickerFileType>? Filters(string json)
    {
        FilterModel[] values = JsonSerializer.Deserialize(
            json, DesktopServicesJsonContext.Default.FilterModelArray) ?? [];
        return values.Length == 0 ? null : values.Select(value => new FilePickerFileType(value.Name)
        {
            Patterns = value.Patterns,
        }).ToArray();
    }

    private static IEnumerable<(string Text, string Value)> Buttons(string buttons) => buttons switch
    {
        "ok" => [("OK", "ok")],
        "okCancel" => [("OK", "ok"), ("Cancel", "cancel")],
        "yesNo" => [("Yes", "yes"), ("No", "no")],
        _ => throw new ArgumentException($"Unsupported message-dialog buttons '{buttons}'."),
    };

    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private sealed record FilterModel(string Name, string[] Patterns);
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(FilterModel[]))]
    private sealed partial class DesktopServicesJsonContext : JsonSerializerContext;
}
