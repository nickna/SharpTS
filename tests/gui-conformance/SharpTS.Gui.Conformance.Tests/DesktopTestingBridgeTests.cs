using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using SharpTS.Gui;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

[Collection(DesktopRendererCollection.Name)]
public sealed class DesktopTestingBridgeTests
{
    static DesktopTestingBridgeTests()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<TestApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
        }
    }

    [Fact]
    public void TestingDriverOperationsAreScopedToTheSuppliedWindow()
    {
        using DesktopRuntimeRegistration runtime = Configure(headless: true);
        using DesktopApplicationSession application = DesktopBridge.CreateDesktopApplication("explicit");
        using DesktopRoot first = application.CreateWindowRoot(() => { }, null, false, true);
        using DesktopRoot second = application.CreateWindowRoot(() => { }, null, false, false);
        first.Render(Window("first"));
        second.Render(Window("second"));

        Assert.Equal("first", DesktopTestingBridge.GetText(first, "value"));
        Assert.Equal("second", DesktopTestingBridge.GetText(second, "value"));
        bool afterRenderCalled = false;
        DesktopTestingBridge.AfterRender(first, () => afterRenderCalled = true);
        Assert.True(afterRenderCalled);
        Assert.Throws<InvalidOperationException>(() => DesktopTestingBridge.Click(first, "value"));
        Assert.Throws<InvalidOperationException>(() => DesktopTestingBridge.GetText(first, "missing"));

        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => DesktopTestingBridge.GetText(first, "value"));
    }

    [Fact]
    public void TestingDriverRejectsNonHeadlessRuntime()
    {
        using DesktopRuntimeRegistration runtime = Configure(headless: false);
        using DesktopApplicationSession application = DesktopBridge.CreateDesktopApplication("explicit");
        using DesktopRoot root = application.CreateWindowRoot(() => { }, null, false, true);
        root.Render(Window("value"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            DesktopTestingBridge.GetText(root, "value"));
        Assert.Contains("Headless", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestingDriverResizesTheWindowInLogicalCoordinates()
    {
        using DesktopRuntimeRegistration runtime = Configure(headless: true);
        using DesktopApplicationSession application = DesktopBridge.CreateDesktopApplication("explicit");
        using DesktopRoot root = application.CreateWindowRoot(() => { }, null, false, true);
        root.Render(Window("value"));
        root.Window!.Show();
        Dispatcher.UIThread.RunJobs();

        DesktopTestingBridge.SetWindowClientSize(root, 720, 480);

        Assert.InRange(root.Window.ClientSize.Width, 719, 721);
        Assert.InRange(root.Window.ClientSize.Height, 479, 481);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DesktopTestingBridge.SetWindowClientSize(root, double.NaN, 480));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DesktopTestingBridge.SetWindowClientSize(root, 720, 0));
    }

    [Fact]
    public void TestingDriverSupportsDeterministicPointerPhasesAndCancellation()
    {
        var events = new List<string>();
        using DesktopRuntimeRegistration runtime = Configure(headless: true);
        using DesktopApplicationSession application = DesktopBridge.CreateDesktopApplication("explicit");
        using DesktopRoot root = application.CreateWindowRoot(() => { }, null, false, true);
        var surface = new GuiVNode(
            "Border",
            Key: "surface",
            Width: 100,
            Height: 80,
            Background: "#ffffff",
            CapturePointerOnPress: true,
            PointerDown: (_, _, x, y, _, buttons, _, _, _, _, _) =>
            {
                events.Add($"down:{buttons}:{Math.Round(x)},{Math.Round(y)}");
                return true;
            },
            PointerMove: (_, _, x, y, _, buttons, _, _, _, _, _) =>
            {
                events.Add($"move:{buttons}:{Math.Round(x)},{Math.Round(y)}");
                return true;
            },
            PointerUp: (_, _, x, y, _, buttons, _, _, _, _, _) =>
            {
                events.Add($"up:{buttons}:{Math.Round(x)},{Math.Round(y)}");
                return true;
            },
            PointerCancel: (_, _, x, y, _, buttons, _, _, _, _, _) =>
            {
                events.Add($"cancel:{buttons}:{Math.Round(x)},{Math.Round(y)}");
                return true;
            });
        root.Render(new GuiVNode("Window", Width: 240, Height: 180, Children: new[] { surface }));
        root.Window!.Show();
        Dispatcher.UIThread.RunJobs();

        DesktopTestingBridge.PressPointer(root, "surface", 10, 12);
        DesktopTestingBridge.MovePointer(root, "surface", 30, 32);
        DesktopTestingBridge.CancelPointer(root, "surface");

        Assert.Equal(["down:1:10,12", "move:1:30,32", "cancel:1:30,32"], events);

        events.Clear();
        DesktopTestingBridge.PressPointer(root, "surface", 14, 16);
        DesktopTestingBridge.ReleasePointer(root, "surface", 44, 46);
        Assert.Equal(["down:1:14,16", "up:0:44,46"], events);
    }

    [Fact]
    public void NativeNotificationsArePostedButPredicatesRemainSynchronous()
    {
        var notifications = new Queue<Action>();
        var events = new List<string>();
        using DesktopRuntimeRegistration runtime = DesktopBridge.Configure(
            new TraceRecorder(Environment.CurrentManagedThreadId),
            (_, _) => { },
            headless: true,
            dispatchGuestCallback: callback => notifications.Enqueue(callback),
            scheduleGuestMicrotask: callback => callback(),
            invokeGuestCallback: callback => callback());
        using DesktopApplicationSession application = DesktopBridge.CreateDesktopApplication("explicit");
        using DesktopRoot root = application.CreateWindowRoot(() => { }, null, false, true);
        root.Render(new GuiVNode(
            "Window",
            Children: new GuiVNode[]
            {
                new GuiVNode("Button", Key: "action", Text: "Action", Click: () => events.Add("click")),
            },
            KeyDown: (key, _, _, _, _, _) =>
            {
                events.Add("key:" + key);
                return true;
            }));
        root.Window!.Show();
        Dispatcher.UIThread.RunJobs();

        DesktopTestingBridge.Click(root, "action");
        Assert.Empty(events);
        Assert.Single(notifications);
        notifications.Dequeue()();
        Assert.Equal(["click"], events);

        DesktopTestingBridge.PressKey(root, "C");
        Assert.Equal(["click", "key:C"], events);
        Assert.Empty(notifications);
    }

    [Fact]
    public void MenuRoutingPostsOnlyTheOriginatingItemCallback()
    {
        var notifications = new Queue<Action>();
        var events = new List<string>();
        using DesktopRuntimeRegistration runtime = DesktopBridge.Configure(
            new TraceRecorder(Environment.CurrentManagedThreadId),
            (_, _) => { },
            headless: true,
            dispatchGuestCallback: callback => notifications.Enqueue(callback),
            scheduleGuestMicrotask: callback => callback(),
            invokeGuestCallback: callback => callback());
        using DesktopApplicationSession application = DesktopBridge.CreateDesktopApplication("explicit");
        using DesktopRoot root = application.CreateWindowRoot(() => { }, null, false, true);
        root.Render(new GuiVNode("Window", Children: new GuiVNode[]
        {
            new GuiVNode("Menu", Children: new GuiVNode[]
            {
                new GuiVNode("MenuItem", Key: "parent", Text: "File", Click: () => events.Add("parent"), Children: new GuiVNode[]
                {
                    new GuiVNode("MenuItem", Key: "leaf", Text: "Open", Click: () => events.Add("leaf")),
                }),
            }),
        }));

        DesktopTestingBridge.ClickMenuItem(root, "leaf");

        Assert.Single(notifications);
        notifications.Dequeue()();
        Assert.Equal(["leaf"], events);
    }

    [Fact]
    public async Task DesktopInteractions_StartAfterTheNativeEventAndPreserveTypedResults()
    {
        var services = new RecordingInteractionServices();
        using DesktopRuntimeRegistration runtime = DesktopBridge.Configure(
            new TraceRecorder(Environment.CurrentManagedThreadId),
            (_, _) => { },
            headless: true,
            dispatchGuestCallback: callback => callback(),
            scheduleGuestMicrotask: callback => callback(),
            invokeGuestCallback: callback => callback(),
            interactionServices: services);
        using DesktopApplicationSession application = DesktopBridge.CreateDesktopApplication("explicit");
        using DesktopRoot root = application.CreateWindowRoot(() => { }, null, false, true);
        root.Render(Window("services"));

        Task<string> message = DesktopBridge.ShowMessageDialogAsync("Title", "Message", "yesNo");
        Task<string[]> open = DesktopBridge.ShowOpenFileDialogAsync("Open", false, "[]");
        Task<string?> save = DesktopBridge.ShowSaveFileDialogAsync("Save", "file.txt", "txt", "[]");

        Assert.Empty(services.Calls);
        Assert.False(message.IsCompleted);
        Assert.False(open.IsCompleted);
        Assert.False(save.IsCompleted);

        Dispatcher.UIThread.RunJobs();

        Assert.Equal("yes", await message);
        Assert.Equal(["C:\\paint\\one.sharpaint", "C:\\paint\\two.png"], await open);
        Assert.Null(await save);
        Assert.Equal(["message", "open", "save"], services.Calls);
    }

    private static DesktopRuntimeRegistration Configure(bool headless) =>
        DesktopBridge.Configure(
            new TraceRecorder(Environment.CurrentManagedThreadId),
            (_, _) => { },
            headless,
            callback => callback(),
            callback => callback());

    private static GuiVNode Window(string text) =>
        new("Window", Children: new[] { new GuiVNode("TextBlock", Key: "value", Text: text) });

    private sealed class RecordingInteractionServices : IDesktopInteractionServices
    {
        public List<string> Calls { get; } = [];
        public bool SupportsHeadless => true;

        public Task<string> ShowMessageAsync(
            Avalonia.Controls.Window owner, string title, string message, string buttons)
        {
            Calls.Add("message");
            return Task.FromResult("yes");
        }

        public Task<string[]> OpenFilesAsync(
            Avalonia.Controls.Window owner, string title, bool allowMultiple, string filtersJson)
        {
            Calls.Add("open");
            return Task.FromResult(new[] { "C:\\paint\\one.sharpaint", "C:\\paint\\two.png" });
        }

        public Task<string?> SaveFileAsync(
            Avalonia.Controls.Window owner, string title, string suggestedFileName,
            string defaultExtension, string filtersJson)
        {
            Calls.Add("save");
            return Task.FromResult<string?>(null);
        }

        public Task<string?> OpenFolderAsync(Avalonia.Controls.Window owner, string title) =>
            Task.FromResult<string?>(null);

        public Task<string> ReadClipboardAsync(Avalonia.Controls.Window owner) =>
            Task.FromResult(string.Empty);

        public Task WriteClipboardAsync(Avalonia.Controls.Window owner, string value) =>
            Task.CompletedTask;
    }

    private sealed class TestApplication : Application;
}
