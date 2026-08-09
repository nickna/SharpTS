using Avalonia;
using Avalonia.Headless;
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

    private static DesktopRuntimeRegistration Configure(bool headless) =>
        DesktopBridge.Configure(
            new TraceRecorder(Environment.CurrentManagedThreadId),
            (_, _) => { },
            headless,
            callback => callback(),
            callback => callback());

    private static GuiVNode Window(string text) =>
        new("Window", Children: new[] { new GuiVNode("TextBlock", Key: "value", Text: text) });

    private sealed class TestApplication : Application;
}
