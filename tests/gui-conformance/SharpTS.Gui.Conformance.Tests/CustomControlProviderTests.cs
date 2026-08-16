using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using SharpTS.Gui;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

[Collection(DesktopRendererCollection.Name)]
public sealed class CustomControlProviderTests
{
    static CustomControlProviderTests()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<TestApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
        }
    }

    [Fact]
    public void StaticallyRegisteredProvider_CreatesUpdatesAndUnregistersNamespacedControl()
    {
        using IDisposable providers = DesktopBridge.RegisterControlProviders(new BadgeProvider());
        var trace = new TraceRecorder(Environment.CurrentManagedThreadId);
        using DesktopRuntimeRegistration runtime = DesktopBridge.Configure(
            trace,
            (_, _) => { },
            headless: true,
            dispatchGuestCallback: callback => callback(),
            scheduleGuestMicrotask: callback => callback());
        using DesktopApplicationSession application = DesktopBridge.CreateDesktopApplication("explicit");
        using DesktopRoot root = application.CreateWindowRoot(
            () => { }, owner: null, modal: false, mainWindow: true);

        root.Render(Window(Custom("one")));
        var badge = Assert.IsType<TextBlock>(root.FindControl("badge"));
        Assert.Equal("one", badge.Text);

        root.Render(Window(Custom("two")));
        Assert.Same(badge, root.FindControl("badge"));
        Assert.Equal("two", badge.Text);
    }

    [Fact]
    public void ProviderRegistration_RejectsUnqualifiedKindsAndDuplicateProviders()
    {
        InvalidOperationException version = Assert.Throws<InvalidOperationException>(() =>
            DesktopBridge.RegisterControlProviders(new WrongVersionProvider()));
        Assert.Contains("contract version", version.Message, StringComparison.Ordinal);

        InvalidOperationException unqualified = Assert.Throws<InvalidOperationException>(() =>
            DesktopBridge.RegisterControlProviders(new InvalidProvider()));
        Assert.Contains("example.widgets.", unqualified.Message, StringComparison.Ordinal);

        using IDisposable first = DesktopBridge.RegisterControlProviders(new BadgeProvider());
        InvalidOperationException duplicate = Assert.Throws<InvalidOperationException>(() =>
            DesktopBridge.RegisterControlProviders(new BadgeProvider()));
        Assert.Contains("already registered", duplicate.Message, StringComparison.Ordinal);
    }

    private static GuiVNode Window(GuiVNode child) =>
        new("Window", Children: new[] { child });

    private static GuiVNode Custom(string label) =>
        DesktopBridge.CreateCustomControl(
            "example.widgets.Badge",
            JsonSerializer.Serialize(new { label }),
            [],
            "badge",
            null);

    private sealed class BadgeProvider : IGuiControlProvider
    {
        public int ContractVersion => DesktopBridge.CustomControlProviderApiVersion;
        public string ProviderId => "example.widgets";
        public IReadOnlyList<NodeDescriptor> Descriptors { get; } = [new BadgeDescriptor()];
    }

    private sealed class InvalidProvider : IGuiControlProvider
    {
        public int ContractVersion => DesktopBridge.CustomControlProviderApiVersion;
        public string ProviderId => "example.widgets";
        public IReadOnlyList<NodeDescriptor> Descriptors { get; } = [new InvalidDescriptor()];
    }

    private sealed class WrongVersionProvider : IGuiControlProvider
    {
        public int ContractVersion => 2;
        public string ProviderId => "example.widgets";
        public IReadOnlyList<NodeDescriptor> Descriptors { get; } = [new BadgeDescriptor()];
    }

    private sealed class BadgeDescriptor() : NodeDescriptor("example.widgets.Badge", 0, 0)
    {
        public override Control Create(GuiVNode node)
        {
            var control = new TextBlock();
            Update(control, new GuiVNode(Kind), node);
            return control;
        }

        public override bool Update(Control control, GuiVNode previous, GuiVNode next)
        {
            string label = JsonDocument.Parse(next.CustomPropertiesJson ?? "{}").RootElement
                .GetProperty("label").GetString() ?? string.Empty;
            var text = (TextBlock)control;
            if (text.Text == label)
                return false;
            text.Text = label;
            return true;
        }
    }

    private sealed class InvalidDescriptor() : NodeDescriptor("Badge", 0, 0)
    {
        public override Control Create(GuiVNode node) => new TextBlock();
        public override bool Update(Control control, GuiVNode previous, GuiVNode next) => false;
    }

    private sealed class TestApplication : Application;
}
