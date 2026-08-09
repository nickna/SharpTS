using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SharpTS.Gui;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopRendererCollection
{
    public const string Name = "DesktopRenderer";
}

[Collection(DesktopRendererCollection.Name)]
public sealed class DesktopRendererTests : IDisposable
{
    static DesktopRendererTests()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<TestApplication>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false,
                    ShouldRenderOnUIThread = true,
                })
                .SetupWithoutStarting();
        }
    }

    private readonly TraceRecorder _trace = new(Environment.CurrentManagedThreadId);
    private readonly DesktopRuntimeRegistration _runtimeRegistration;
    private readonly List<int> _shutdownRequests = [];

    public DesktopRendererTests()
    {
        _runtimeRegistration = DesktopBridge.Configure(
            _trace,
            (_, _) => { },
            headless: true,
            dispatchGuestCallback: callback => callback(),
            scheduleGuestMicrotask: callback => callback(),
            requestShutdown: exitCode => _shutdownRequests.Add(exitCode));
    }

    public void Dispose() => _runtimeRegistration.Dispose();

    [Fact]
    public void DesktopApplication_TracksHeadlessTrayIconCallbacksUpdatesAndDisposal()
    {
        using DesktopApplicationSession application =
            DesktopBridge.CreateDesktopApplication("explicit");
        var events = new List<string>();
        using DesktopTrayIcon tray = DesktopBridge.CreateDesktopTrayIcon(
            application,
            "asset:///icon.ico",
            "SharpTS",
            "[{\"id\":\"open\",\"label\":\"Open\"}]",
            () => events.Add("old-click"),
            id => events.Add("old-" + id));

        tray.RaiseClickForTesting();
        tray.RaiseMenuClickForTesting("open");
        tray.Update(
            "asset:///icon.ico",
            "Updated",
            "[{\"id\":\"quit\",\"label\":\"Quit\"}]",
            () => events.Add("new-click"),
            id => events.Add("new-" + id));
        tray.RaiseClickForTesting();
        tray.RaiseMenuClickForTesting("quit");

        Assert.Equal(["old-click", "old-open", "new-click", "new-quit"], events);
        Assert.False(tray.IsDisposed);
        application.Dispose();
        Assert.True(tray.IsDisposed);
    }

    [Fact]
    public void DesktopPlatformServices_ReportEnvironmentAndRejectMissingShellTargets()
    {
        using JsonDocument info = JsonDocument.Parse(DesktopBridge.GetDesktopPlatformInfoJson());
        Assert.Equal("windows", info.RootElement.GetProperty("operatingSystem").GetString());
        Assert.NotEmpty(info.RootElement.GetProperty("architecture").GetString()!);
        Assert.Empty(DesktopBridge.GetDesktopLaunchArguments());
        Assert.Throws<FileNotFoundException>(() =>
            DesktopPlatformServices.ResolveExistingPath($"missing-{Guid.NewGuid():N}"));
    }

    [Fact]
    public async Task DesktopNotifications_ValidateEscapeAndNoOpInHeadlessMode()
    {
        string xml = DesktopNotifications.CreateToastXml("Ready <now>", "Use A&B", silent: true);
        var toast = System.Xml.Linq.XDocument.Parse(xml);
        Assert.Equal("Ready <now>", toast.Descendants("text").First().Value);
        Assert.Equal("Use A&B", toast.Descendants("text").Last().Value);
        Assert.Equal("true", toast.Descendants("audio").Single().Attribute("silent")?.Value);

        await DesktopBridge.ShowDesktopNotificationAsync("Headless", "Validated", silent: false);
        Assert.Throws<ArgumentException>(() => DesktopNotifications.CreateToastXml(" ", "body", false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DesktopNotifications.CreateToastXml(new string('x', 257), "body", false));
        if (OperatingSystem.IsWindows() && !DesktopNotifications.HasPackageIdentity())
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DesktopNotifications.ShowAsync(headless: false, "Unpackaged", "Rejected", silent: false));
            Assert.Contains("MSIX package identity", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DragDrop_NormalizesPayloadUsesLatestCallbacksAndReleasesSubscriptions()
    {
        var events = new List<string>();
        using DesktopRoot root = DesktopBridge.CreateDesktopRoot(() => { });
        GuiVNode Target(string version) => new(
            "Border",
            Key: "drop-target",
            AllowDrop: true,
            DragOver: (files, text, effect, ctrl, alt, shift, meta) =>
            {
                events.Add($"{version}-over:{text}:{effect}:{ctrl}");
                return "copy";
            },
            Drop: (files, text, effect, ctrl, alt, shift, meta) =>
                events.Add($"{version}-drop:{text}:{effect}:{ctrl}"));

        root.Render(Window(Target("old")));
        var target = _runtimeRegistration.Context.RequireControl<Border>("drop-target");
        Assert.True(DragDrop.GetAllowDrop(target));
        Assert.Equal(1, root.ActiveSubscriptions);
        root.Render(Window(Target("new")));

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText("payload"));
        var over = new DragEventArgs(
            DragDrop.DragOverEvent, transfer, target, new Point(4, 8), KeyModifiers.Control)
        {
            DragEffects = DragDropEffects.Move,
        };
        target.RaiseEvent(over);
        Assert.Equal(DragDropEffects.Copy, over.DragEffects);
        Assert.True(over.Handled);

        var drop = new DragEventArgs(
            DragDrop.DropEvent, transfer, target, new Point(4, 8), KeyModifiers.Control)
        {
            DragEffects = DragDropEffects.Copy,
        };
        target.RaiseEvent(drop);
        Assert.True(drop.Handled);
        Assert.Equal(["new-over:payload:move:True", "new-drop:payload:copy:True"], events);

        root.Dispose();
        Assert.Equal(0, root.ActiveSubscriptions);
    }

    [Fact]
    public void DesktopApplication_TracksOwnedModalWindowsAndLastWindowShutdown()
    {
        using DesktopApplicationSession application =
            DesktopBridge.CreateDesktopApplication("onLastWindowClose");
        DesktopRoot main = application.CreateWindowRoot(
            () => { }, owner: null, modal: false, mainWindow: true);
        main.Render(Window(title: "Main"));
        DesktopRoot dialog = application.CreateWindowRoot(
            () => { }, owner: main, modal: true, mainWindow: false);
        dialog.Render(Window(title: "Dialog"));

        Assert.Equal(2, application.WindowCount);
        Assert.True(main.IsMainWindow);
        Assert.Same(main, dialog.Owner);
        Assert.True(dialog.IsModal);
        Assert.False(_runtimeRegistration.Context.ShouldRequestShutdown(dialog));

        dialog.Dispose();
        Assert.True(dialog.IsDisposed);
        Assert.True(dialog.Completion.IsCompletedSuccessfully);
        Assert.Equal(1, application.WindowCount);
        Assert.True(_runtimeRegistration.Context.ShouldRequestShutdown(main));

        application.Shutdown(7);
        Assert.Equal([7], _shutdownRequests);
    }

    [Theory]
    [InlineData("onMainWindowClose", true, false)]
    [InlineData("explicit", false, false)]
    public void DesktopApplication_HonorsConfiguredShutdownMode(
        string shutdownMode,
        bool mainRequestsShutdown,
        bool secondaryRequestsShutdown)
    {
        using DesktopApplicationSession application =
            DesktopBridge.CreateDesktopApplication(shutdownMode);
        DesktopRoot main = application.CreateWindowRoot(
            () => { }, owner: null, modal: false, mainWindow: false);
        main.Render(Window(title: "Main"));
        DesktopRoot secondary = application.CreateWindowRoot(
            () => { }, owner: main, modal: false, mainWindow: false);
        secondary.Render(Window(title: "Secondary"));

        Assert.True(main.IsMainWindow);
        Assert.Equal(mainRequestsShutdown,
            _runtimeRegistration.Context.ShouldRequestShutdown(main));
        Assert.Equal(secondaryRequestsShutdown,
            _runtimeRegistration.Context.ShouldRequestShutdown(secondary));
    }

    [Fact]
    public void DesktopApplication_RejectsForeignOwnerAndDuplicateMainWindow()
    {
        using DesktopApplicationSession application =
            DesktopBridge.CreateDesktopApplication("explicit");
        DesktopRoot main = application.CreateWindowRoot(
            () => { }, owner: null, modal: false, mainWindow: true);

        Assert.Throws<InvalidOperationException>(() => application.CreateWindowRoot(
            () => { }, owner: null, modal: false, mainWindow: true));
        Assert.Throws<ArgumentException>(() => application.CreateWindowRoot(
            () => { }, owner: null, modal: true, mainWindow: false));

        main.Dispose();
        Assert.Equal(0, application.WindowCount);
    }

    [Fact]
    public void DesktopApplication_AppliesValidatedResourcesStylesAndControlClasses()
    {
        using DesktopApplicationSession application =
            DesktopBridge.CreateDesktopApplication("explicit");
        application.ConfigureStyleResources(
            """
            {
              "resources": { "accent": "#336699", "spacing": 8 },
              "styles": [{
                "selector": { "control": "Button", "classes": ["accent"] },
                "setters": {
                  "background": { "resource": "accent" },
                  "padding": { "resource": "spacing" }
                }
              }]
            }
            """);
        DesktopRoot root = application.CreateWindowRoot(
            () => { }, owner: null, modal: false, mainWindow: true);
        root.Render(Window(Panel(0, new GuiVNode(
            "Button", Key: "styled", Text: "Styled", Classes: ["accent"]))));

        Assert.Equal("#336699", application.FindResource(root, "accent"));
        Assert.Null(application.FindResource(root, "missing"));
        Assert.Single(root.Window!.Styles);
        Assert.Equal(["accent"], root.FindControl("styled")!.Classes);
    }

    [Theory]
    [InlineData("{\"resources\":{},\"styles\":[{\"selector\":{\"control\":\"Unknown\"},\"setters\":{\"width\":1}}]}")]
    [InlineData("{\"resources\":{},\"styles\":[{\"selector\":{\"control\":\"Button\"},\"setters\":{\"notAProperty\":1}}]}")]
    [InlineData("{\"resources\":{},\"styles\":[{\"selector\":{\"control\":\"Button\"},\"setters\":{\"background\":{\"resource\":\"missing\"}}}]}")]
    public void DesktopApplication_RejectsInvalidStyleContracts(string json)
    {
        using DesktopApplicationSession application =
            DesktopBridge.CreateDesktopApplication("explicit");
        Assert.ThrowsAny<ArgumentException>(() => application.ConfigureStyleResources(json));
    }

    [Fact]
    public void AdvancedItemsTreeCanvasRichTextAndDrawing_ReconcileAsNativeControls()
    {
        int[] selectedEvent = [];
        bool? expandedEvent = null;
        DesktopRoot root = CreateRoot();
        root.Render(Window(Panel(0,
            new GuiVNode("VirtualizingList", Key: "list", SelectedIndices: [1],
                SelectionMode: "single", IndicesChanged: value => selectedEvent = value,
                Children: new[] { Text("A", "a"), Text("B", "b") }),
            new GuiVNode("TreeView", Key: "tree", Children: new[]
            {
                new GuiVNode("TreeViewItem", Key: "node", Header: "Root", IsExpanded: true,
                    ExpandedChanged: value => expandedEvent = value,
                    Children: new[] { Text("Leaf", "leaf") })
            }),
            new GuiVNode("Canvas", Key: "canvas", Height: 80, Children: new[]
            {
                new GuiVNode("Button", Key: "positioned", Text: "At", CanvasLeft: 12, CanvasTop: 18)
            }),
            new GuiVNode("RichTextBlock", Key: "rich", RichTextJson:
                "[{\"text\":\"Bold\",\"fontWeight\":\"bold\"},{\"text\":\" text\",\"foreground\":\"#336699\"}]"),
            new GuiVNode("DrawingCanvas", Key: "drawing", Width: 120, Height: 80, DrawingJson:
                "[{\"kind\":\"line\",\"x1\":0,\"y1\":0,\"x2\":20,\"y2\":20,\"stroke\":\"#336699\"}]"))));

        var list = Assert.IsType<ListBox>(root.FindControl("list"));
        Assert.Equal(2, list.Items.Count);
        Assert.Same(list.Items[1], Assert.Single(list.SelectedItems!));
        var treeItem = Assert.IsType<TreeViewItem>(root.FindControl("node"));
        Assert.True(treeItem.IsExpanded);
        Control positioned = root.FindControl("positioned")!;
        Assert.Equal(12, Canvas.GetLeft(positioned));
        Assert.Equal(18, Canvas.GetTop(positioned));
        Assert.Equal(2, Assert.IsType<TextBlock>(root.FindControl("rich")).Inlines!.Count);
        Assert.Single(Assert.IsType<DrawingSurface>(root.FindControl("drawing")).Commands);
        list.SelectedIndex = 0;
        treeItem.IsExpanded = false;
        Assert.Equal([0], selectedEvent);
        Assert.False(expandedEvent);

        int identity = RuntimeHelpers.GetHashCode(root.FindControl("b")!);
        root.Render(Window(Panel(0,
            new GuiVNode("VirtualizingList", Key: "list", SelectedIndices: [0],
                SelectionMode: "single", Children: new[] { Text("B2", "b"), Text("A2", "a") }),
            new GuiVNode("TreeView", Key: "tree", Children: Array.Empty<GuiVNode>()),
            new GuiVNode("Canvas", Key: "canvas", Height: 80, Children: Array.Empty<GuiVNode>()),
            new GuiVNode("RichTextBlock", Key: "rich", RichTextJson: "[]"),
            new GuiVNode("DrawingCanvas", Key: "drawing", Width: 120, Height: 80, DrawingJson: "[]"))));

        Assert.Equal(identity, RuntimeHelpers.GetHashCode(root.FindControl("b")!));
        Assert.Same(list.Items[0], Assert.Single(list.SelectedItems!));
        Assert.Empty(Assert.IsType<TextBlock>(root.FindControl("rich")).Inlines!);
    }

    [Theory]
    [InlineData("[{\"kind\":\"unknown\"}]")]
    [InlineData("[{\"kind\":\"line\",\"x1\":0,\"y1\":0,\"x2\":1,\"y2\":1}]")]
    [InlineData("[{\"kind\":\"rectangle\",\"width\":-1,\"height\":2}]")]
    public void DrawingCanvas_RejectsInvalidCommandsBeforeNativeMutation(string commands)
    {
        DesktopRoot root = CreateRoot();
        Assert.ThrowsAny<Exception>(() => root.Render(Window(
            new GuiVNode("DrawingCanvas", Width: 100, Height: 100, DrawingJson: commands))));
        Assert.Null(root.Window);
    }

    [Fact]
    public void Render_UpdatesPropertiesAndMovesKeyedControlsWithoutRecreation()
    {
        DesktopRoot root = CreateRoot();
        root.Render(Window(
            Panel(4,
                Text("A", "a"),
                Text("B", "b")),
            title: "Before",
            width: 400,
            height: 200));

        Control a = Assert.IsType<TextBlock>(root.FindControl("a"));
        Control b = Assert.IsType<TextBlock>(root.FindControl("b"));
        var panel = Assert.IsType<StackPanel>(root.FindControl("panel"));

        root.Render(Window(
            Panel(11,
                Text("B updated", "b"),
                Text("A updated", "a")),
            title: "After",
            width: 640,
            height: 360));

        Assert.Same(a, root.FindControl("a"));
        Assert.Same(b, root.FindControl("b"));
        Assert.Same(b, panel.Children[0]);
        Assert.Same(a, panel.Children[1]);
        Assert.Equal("A updated", ((TextBlock)a).Text);
        Assert.Equal("B updated", ((TextBlock)b).Text);
        Assert.Equal(11, panel.Spacing);
        Assert.Equal("After", root.Window!.Title);
        Assert.Equal(640, root.Window.Width);
        Assert.Equal(360, root.Window.Height);
        Assert.Contains(_trace.Snapshot(), item => item.Stage == "reconcile-move");
    }

    [Fact]
    public void Render_MatchesUnkeyedChildrenPositionallyAndReplacesChangedKinds()
    {
        DesktopRoot root = CreateRoot();
        root.Render(Window(Panel(0, Text("first"), Text("second"))));
        var panel = Assert.IsType<StackPanel>(root.FindControl("panel"));
        Control first = panel.Children[0];
        Control second = panel.Children[1];

        root.Render(Window(Panel(0, Text("first updated"), ButtonNode("replacement"))));

        Assert.Same(first, panel.Children[0]);
        Assert.NotSame(second, panel.Children[1]);
        Assert.IsType<Button>(panel.Children[1]);
        Assert.Equal("first updated", ((TextBlock)first).Text);
    }

    [Fact]
    public void Fragment_IsARetainedContainerAndMovesItsKeyedChildren()
    {
        DesktopRoot root = CreateRoot();
        root.Render(Window(new GuiVNode(
            "Fragment",
            Key: "fragment",
            Children: new[] { Text("A", "a"), Text("B", "b") })));
        var fragment = Assert.IsType<StackPanel>(root.FindControl("fragment"));
        Control a = root.FindControl("a")!;
        Control b = root.FindControl("b")!;

        root.Render(Window(new GuiVNode(
            "Fragment",
            Key: "fragment",
            Children: new[] { Text("B", "b"), Text("A", "a") })));

        Assert.Same(fragment, root.FindControl("fragment"));
        Assert.Same(b, fragment.Children[0]);
        Assert.Same(a, fragment.Children[1]);
    }

    [Fact]
    public void Render_PrevalidatesDuplicateKeysLeafChildrenAndWindowCardinality()
    {
        DesktopRoot root = CreateRoot();
        root.Render(Window(Panel(0, Text("A", "a")), title: "Stable"));
        Window window = root.Window!;
        Control a = root.FindControl("a")!;

        Assert.Throws<InvalidOperationException>(() => root.Render(
            Window(Panel(0, Text("one", "x"), Text("two", "x")), title: "Must not commit")));
        Assert.Throws<InvalidOperationException>(() => root.Render(
            new GuiVNode("Window", Children: new[]
            {
                new GuiVNode("TextBlock", Text: "one"),
                new GuiVNode("TextBlock", Text: "two"),
            })));
        Assert.Throws<InvalidOperationException>(() => root.Render(
            Window(new GuiVNode("TextBlock", Text: "leaf", Children: new[] { Text("invalid") }))));
        Assert.Throws<InvalidOperationException>(() => root.Render(
            Window(new GuiVNode("Unknown"))));
        Assert.Equal("42", DesktopBridge.CreateTextBlock("key", double.NaN, "normal", "noWrap", null, 42d, null).Key);

        Assert.Same(window, root.Window);
        Assert.Same(a, root.FindControl("a"));
        Assert.Equal("Stable", root.Window!.Title);
    }

    [Fact]
    public void Button_KeepsOneSubscriptionAndDispatchesLatestCallback()
    {
        int oldCalls = 0;
        int newCalls = 0;
        object refIdentity = new();
        var refEvents = new List<string>();
        Action<object?> firstRef = value => refEvents.Add(value is null ? "detach" : "attach");
        Action<object?> secondRef = value => refEvents.Add(value is null ? "detach-new" : "attach-new");
        DesktopRoot root = CreateRoot();

        root.Render(Window(Panel(0,
            new GuiVNode(
                "Button",
                Key: "action",
                Text: "Old",
                Click: () => oldCalls++,
                AttachRef: firstRef,
                RefIdentity: refIdentity))));
        var button = Assert.IsType<Button>(root.FindControl("action"));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        root.Render(Window(Panel(0,
            new GuiVNode(
                "Button",
                Key: "action",
                Text: "New",
                Click: () => newCalls++,
                AttachRef: secondRef,
                RefIdentity: refIdentity))));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, oldCalls);
        Assert.Equal(1, newCalls);
        Assert.Equal(new[] { "attach" }, refEvents);
        Assert.Equal(1, root.ActiveSubscriptions);
        Assert.Single(_trace.Snapshot(), item => item.Stage == "subscribe");

        root.Dispose();
        Assert.Equal(new[] { "attach", "detach-new" }, refEvents);
        Assert.Single(_trace.Snapshot(), item => item.Stage == "unsubscribe");
    }

    [Fact]
    public void ReplacementAndDisposal_ReleaseEventsAndRefsChildFirstExactlyOnce()
    {
        var order = new List<string>();
        object oldIdentity = new();
        object newIdentity = new();
        object windowIdentity = new();
        object panelIdentity = new();
        object siblingIdentity = new();
        Action<object?> windowRef = value => order.Add(value is null ? "window-null" : "window-set");
        Action<object?> panelRef = value => order.Add(value is null ? "panel-null" : "panel-set");
        Action<object?> siblingRef = value => order.Add(value is null ? "sibling-null" : "sibling-set");
        DesktopRoot root = CreateRoot(() => order.Add("reactive-cleanup"));
        root.Render(new GuiVNode(
            "Window",
            AttachRef: windowRef,
            RefIdentity: windowIdentity,
            Children: new[]
            {
                new GuiVNode(
                    "StackPanel",
                    Key: "panel",
                    AttachRef: panelRef,
                    RefIdentity: panelIdentity,
                    Children: new[]
                    {
                        new GuiVNode(
                            "Button",
                            Key: "replace",
                            Text: "button",
                            AttachRef: value => order.Add(value is null ? "old-null" : "old-set"),
                            RefIdentity: oldIdentity),
                        new GuiVNode(
                            "TextBlock",
                            Key: "sibling",
                            Text: "sibling",
                            AttachRef: siblingRef,
                            RefIdentity: siblingIdentity),
                    })
            }));

        order.Clear();
        root.Render(new GuiVNode(
            "Window",
            AttachRef: windowRef,
            RefIdentity: windowIdentity,
            Children: new[]
            {
                new GuiVNode(
                    "StackPanel",
                    Key: "panel",
                    AttachRef: panelRef,
                    RefIdentity: panelIdentity,
                    Children: new[]
                    {
                        new GuiVNode(
                            "TextBlock",
                            Key: "replace",
                            Text: "text",
                            AttachRef: value => order.Add(value is null ? "new-null" : "new-set"),
                            RefIdentity: newIdentity),
                        new GuiVNode(
                            "TextBlock",
                            Key: "sibling",
                            Text: "sibling",
                            AttachRef: siblingRef,
                            RefIdentity: siblingIdentity),
                    })
            }));

        Assert.Equal(new[] { "old-null", "new-set" }, order);
        Assert.Equal(0, root.ActiveSubscriptions);

        order.Clear();
        root.Dispose();
        root.Dispose();
        Assert.Equal(
            new[] { "reactive-cleanup", "new-null", "sibling-null", "panel-null", "window-null" },
            order);
        Assert.Equal(1, order.Count(item => item == "new-null"));
    }

    [Fact]
    public void Root_RejectsAdditionalRootsAndAllOffThreadAccess()
    {
        int cleanups = 0;
        DesktopRoot root = CreateRoot(() => cleanups++);
        root.Render(Window(Panel(0, Text("owner"))));

        Assert.Throws<InvalidOperationException>(() => DesktopBridge.CreateDesktopRoot(() => { }));
        Exception? offThreadError = null;
        var thread = new Thread(() =>
        {
            try
            {
                root.Render(Window(Panel(0, Text("wrong thread"))));
            }
            catch (Exception exception)
            {
                offThreadError = exception;
            }
        });
        thread.Start();
        thread.Join();
        InvalidOperationException error = Assert.IsType<InvalidOperationException>(offThreadError);
        Assert.Contains("owner", error.Message, StringComparison.OrdinalIgnoreCase);

        root.Dispose();
        root.Dispose();
        Assert.Equal(1, cleanups);
        using DesktopRoot next = CreateRoot();
    }

    [Fact]
    public void FormsLayout_UpdatesInPlaceAcrossAllPreviewDescriptors()
    {
        DesktopRoot root = CreateRoot();
        root.Render(Window(new GuiVNode(
            "Border",
            Key: "border",
            Padding: 8,
            Background: "#112233",
            BorderBrush: "orange",
            BorderThickness: 2,
            CornerRadius: 4,
            Children: new[]
            {
                new GuiVNode(
                    "ScrollViewer",
                    Key: "scroll",
                    VerticalScrollBarVisibility: "visible",
                    Children: new[]
                    {
                        new GuiVNode(
                            "Grid",
                            Key: "grid",
                            Rows: "auto,*",
                            Columns: "120,*",
                            Children: new[]
                            {
                                new GuiVNode(
                                    "TextBlock",
                                    Key: "label",
                                    Text: "Before",
                                    FontSize: 18,
                                    FontWeight: "bold",
                                    TextWrapping: "wrap",
                                    Foreground: "white",
                                    Margin: 3,
                                    HorizontalAlignment: "right",
                                    GridRow: 1,
                                    GridColumn: 1),
                                new GuiVNode("ProgressBar", Key: "progress", Minimum: 0, Maximum: 10, Value: 2),
                            })
                    })
            })));

        var border = Assert.IsType<Border>(root.FindControl("border"));
        var scroll = Assert.IsType<ScrollViewer>(root.FindControl("scroll"));
        var grid = Assert.IsType<Grid>(root.FindControl("grid"));
        var label = Assert.IsType<TextBlock>(root.FindControl("label"));
        var progress = Assert.IsType<ProgressBar>(root.FindControl("progress"));

        root.Render(Window(new GuiVNode(
            "Border",
            Key: "border",
            Padding: 12,
            Background: "#223344",
            BorderBrush: "yellow",
            BorderThickness: 3,
            CornerRadius: 6,
            Children: new[]
            {
                new GuiVNode(
                    "ScrollViewer",
                    Key: "scroll",
                    HorizontalScrollBarVisibility: "hidden",
                    VerticalScrollBarVisibility: "auto",
                    Children: new[]
                    {
                        new GuiVNode(
                            "Grid",
                            Key: "grid",
                            Rows: "*,auto",
                            Columns: "*,2*",
                            Children: new[]
                            {
                                new GuiVNode("ProgressBar", Key: "progress", Minimum: 0, Maximum: 20, Value: 7),
                                new GuiVNode(
                                    "TextBlock",
                                    Key: "label",
                                    Text: "After",
                                    FontSize: 20,
                                    FontWeight: "normal",
                                    TextWrapping: "noWrap",
                                    Foreground: "black",
                                    HorizontalAlignment: "left",
                                    GridRow: 0,
                                    GridColumn: 0),
                            })
                    })
            })));

        Assert.Same(border, root.FindControl("border"));
        Assert.Same(scroll, root.FindControl("scroll"));
        Assert.Same(grid, root.FindControl("grid"));
        Assert.Same(label, root.FindControl("label"));
        Assert.Same(progress, root.FindControl("progress"));
        Assert.Equal(new Thickness(12), border.Padding);
        Assert.Equal(new CornerRadius(6), border.CornerRadius);
        Assert.Equal(ScrollBarVisibility.Hidden, scroll.HorizontalScrollBarVisibility);
        Assert.Equal("After", label.Text);
        Assert.Equal(FontWeight.Normal, label.FontWeight);
        Assert.Equal(HorizontalAlignment.Left, label.HorizontalAlignment);
        Assert.Equal(0, Grid.GetRow(label));
        Assert.Equal(0, Grid.GetColumn(label));
        Assert.Equal(7, progress.Value);
        Assert.Same(progress, grid.Children[0]);
        Assert.Same(label, grid.Children[1]);
    }

    [Fact]
    public void ControlledInputs_SuppressRenderFeedbackAndDispatchLatestCallbacks()
    {
        var oldEvents = new List<string>();
        var newEvents = new List<string>();
        DesktopRoot root = CreateRoot();

        root.Render(Window(Panel(0,
            new GuiVNode("Button", Key: "button", Text: "Before", Click: () => oldEvents.Add("click")),
            new GuiVNode("TextBox", Key: "text", Text: "before", TextChanged: value => oldEvents.Add("text:" + value)),
            new GuiVNode("CheckBox", Key: "check", Text: "Before", IsChecked: false, CheckedChanged: value => oldEvents.Add("check:" + value)),
            new GuiVNode("ComboBox", Key: "combo", Items: ["a", "b"], SelectedIndex: 0, SelectionChanged: value => oldEvents.Add("combo:" + value)),
            new GuiVNode("Slider", Key: "slider", Minimum: 0, Maximum: 10, Value: 1, ValueChanged: value => oldEvents.Add("slider:" + value)))));

        root.Render(Window(Panel(4,
            new GuiVNode("Button", Key: "button", Text: "After", Click: () => newEvents.Add("click")),
            new GuiVNode("TextBox", Key: "text", Text: "rendered", TextChanged: value => newEvents.Add("text:" + value)),
            new GuiVNode("CheckBox", Key: "check", Text: "After", IsChecked: true, CheckedChanged: value => newEvents.Add("check:" + value)),
            new GuiVNode("ComboBox", Key: "combo", Items: ["a", "b", "c"], SelectedIndex: 1, SelectionChanged: value => newEvents.Add("combo:" + value)),
            new GuiVNode("Slider", Key: "slider", Minimum: 0, Maximum: 20, Value: 2, ValueChanged: value => newEvents.Add("slider:" + value)))));

        Assert.Empty(oldEvents);
        Assert.Empty(newEvents);
        Assert.Equal(5, root.ActiveSubscriptions);
        Assert.Equal(5, _trace.Snapshot().Count(item => item.Stage == "subscribe"));

        Assert.IsType<Button>(root.FindControl("button")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var textBox = Assert.IsType<TextBox>(root.FindControl("text"));
        textBox.Text = "user";
        textBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        Assert.IsType<CheckBox>(root.FindControl("check")).IsChecked = false;
        Assert.IsType<ComboBox>(root.FindControl("combo")).SelectedIndex = 2;
        Assert.IsType<Slider>(root.FindControl("slider")).Value = 9;

        Assert.Empty(oldEvents);
        Assert.Equal(new[] { "click", "text:user", "check:False", "combo:2", "slider:9" }, newEvents);
        root.Dispose();
        Assert.Equal(5, _trace.Snapshot().Count(item => item.Stage == "unsubscribe"));
    }

    [Fact]
    public void PreviewProperties_ArePrevalidatedWithoutMutatingMountedControls()
    {
        DesktopRoot root = CreateRoot();
        root.Render(Window(Panel(2, Text("Stable", "stable")), title: "Stable"));
        Window window = root.Window!;
        Control stable = root.FindControl("stable")!;

        InvalidOperationException colorError = Assert.Throws<InvalidOperationException>(() => root.Render(
            Window(new GuiVNode("Border", Background: "not a color", SourceFile: "app.tsx", SourceLine: 7, SourceColumn: 9))));
        Assert.Contains("app.tsx:7:9", colorError.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => root.Render(
            Window(new GuiVNode("Grid", Rows: "not-a-grid-length"))));
        Assert.Throws<InvalidOperationException>(() => root.Render(
            Window(new GuiVNode("Slider", Minimum: 5, Maximum: 1, Value: 3))));
        Assert.Throws<InvalidOperationException>(() => root.Render(
            Window(new GuiVNode("ComboBox", Items: ["only"], SelectedIndex: 2))));

        Assert.Same(window, root.Window);
        Assert.Same(stable, root.FindControl("stable"));
        Assert.Equal("Stable", root.Window!.Title);
    }

    [Fact]
    public void KeyboardHandlers_AreDiffedAndRepeatedKeyDownIsNormalized()
    {
        var repeats = new List<bool>();
        DesktopRoot root = CreateRoot();
        root.Render(Window(Panel(0, new GuiVNode("Separator", Key: "input"))));
        Control input = root.FindControl("input")!;
        Assert.Equal(0, root.ActiveSubscriptions);

        root.Render(Window(Panel(0, new GuiVNode(
            "Separator",
            Key: "input",
            KeyDown: (_, _, _, _, _, repeat) => { repeats.Add(repeat); return false; }))));
        Assert.Same(input, root.FindControl("input"));
        Assert.Equal(1, root.ActiveSubscriptions);

        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A });
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A });
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.A });
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A });
        Assert.Equal(new[] { false, true, false }, repeats);

        root.Render(Window(Panel(0, new GuiVNode("Separator", Key: "input"))));
        Assert.Equal(0, root.ActiveSubscriptions);
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A });
        Assert.Equal(3, repeats.Count);
        Assert.Single(_trace.Snapshot(), item => item.Stage == "subscribe" && item.Detail == "Separator#input");
        Assert.Single(_trace.Snapshot(), item => item.Stage == "unsubscribe" && item.Detail == "Separator#input");
    }

    [Fact]
    public void KeyRepeat_IsStableAcrossHandlersForOneRoutedNativeEvent()
    {
        var repeats = new List<string>();
        DesktopRoot root = CreateRoot();
        root.Render(Window(new GuiVNode(
            "StackPanel",
            Key: "keyboard-parent",
            KeyDown: (_, _, _, _, _, repeat) => { repeats.Add("parent:" + repeat); return false; },
            Children: new[]
            {
                new GuiVNode(
                    "Separator",
                    Key: "keyboard-child",
                    KeyDown: (_, _, _, _, _, repeat) => { repeats.Add("child:" + repeat); return false; })
            })));
        Control child = root.FindControl("keyboard-child")!;

        child.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.B });
        child.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.B });

        Assert.Equal(
            new[] { "child:False", "parent:False", "child:True", "parent:True" },
            repeats);
    }

    [Fact]
    public void FailedNativeSetter_RollsBackToLastCommittedTree()
    {
        using IDisposable registration = DescriptorRegistry.RegisterForTesting(new RollbackProbeDescriptor());
        DesktopRoot root = CreateRoot();
        root.Render(Window(new GuiVNode("$RollbackProbe", Key: "probe", Text: "stable", Width: 100)));
        var probe = Assert.IsType<TextBlock>(root.FindControl("probe"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            root.Render(Window(new GuiVNode("$RollbackProbe", Key: "probe", Text: "throw", Width: 200))));

        Assert.Contains("injected setter failure", error.Message, StringComparison.Ordinal);
        Assert.False(root.IsDisposed);
        Assert.Same(probe, root.FindControl("probe"));
        Assert.Equal(100, probe.Width);
        Assert.Equal("stable", probe.Text);
    }

    [Fact]
    public void AccessibilityFocusImeAndThemeUseNativeAvaloniaContracts()
    {
        var committedText = new List<string>();
        DesktopRef reference = DesktopBridge.CreateRef();
        using DesktopRoot root = CreateRoot();
        root.Render(new GuiVNode(
            "Window", Key: "window", Theme: "dark", Width: 320, Height: 180,
            Children: new[] { new GuiVNode(
                "TextBox", Key: "editor", Text: string.Empty, AutomationName: "Document editor",
                TextChanged: value => committedText.Add(value), AttachRef: reference.Attach, RefIdentity: reference) }));

        var editor = Assert.IsType<TextBox>(root.FindControl("editor"));
        Assert.Equal("Document editor", AutomationProperties.GetName(editor));
        Assert.Equal(ThemeVariant.Dark, root.Window!.RequestedThemeVariant);
        Assert.True(reference.IsAttached);
        root.Window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(reference.focus());
        Assert.True(editor.IsFocused);

        editor.Text = "日本語";
        editor.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        Assert.Equal("日本語", Assert.Single(committedText));
    }

    [Fact]
    public void SuccessfullyRolledBackNativeFailureCarriesOwningBoundaryAndSource()
    {
        using IDisposable registration = DescriptorRegistry.RegisterForTesting(new RollbackProbeDescriptor());
        DesktopRoot root = CreateRoot();
        GuiVNode stable = DesktopBridge.WithBoundary(
            new GuiVNode("$RollbackProbe", Key: "probe", Text: "stable", Width: 100,
                SourceFile: "view.tsx", SourceLine: 12, SourceColumn: 7),
            "root/0:boundary");
        root.Render(Window(stable));

        GuiVNode failing = DesktopBridge.WithBoundary(
            stable with { Text = "throw", Width = 200 },
            "root/0:boundary");
        RecoverableNativeCommitException error = Assert.Throws<RecoverableNativeCommitException>(
            () => root.Render(Window(failing)));

        Assert.Equal("root/0:boundary", error.BoundaryPath);
        Assert.Equal("view.tsx", error.SourceFile);
        Assert.Equal(12, error.SourceLine);
        Assert.Equal("setter", error.Operation);
        Assert.False(root.IsDisposed);
        Assert.Equal("stable", Assert.IsType<TextBlock>(root.FindControl("probe")).Text);
    }

    [Fact]
    public void ScalarUpdateTouchesOnlyItsAffectedNativePath()
    {
        DesktopRoot root = CreateRoot();
        root.Render(Window(Panel(0, Text("before", "affected"), Text("stable", "unrelated"))));
        Control unrelated = root.FindControl("unrelated")!;
        root.ResetOperationCounts();

        root.Render(Window(Panel(0, Text("after", "affected"), Text("stable", "unrelated"))));

        Assert.Same(unrelated, root.FindControl("unrelated"));
        Assert.Equal(new RendererOperationCounts(0, 1, 0, 0), root.OperationCounts);
    }

    [Fact]
    public void ThousandMountUpdateUnmountCyclesReleaseRootsControlsCallbacksRefsAndSubscriptions()
    {
        var retained = new List<WeakReference>();
        for (int index = 0; index < 1_000; index++)
            AddDisposedCycle(retained, index);

        Dispatcher.UIThread.RunJobs();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.All(retained, reference => Assert.False(reference.IsAlive));
        Assert.Equal(0, _runtimeRegistration.Context.CurrentRoot?.ActiveSubscriptions ?? 0);
    }

    [Fact]
    public void Devtools_InspectTreeReportsLogicalNativeAndSourceStructure()
    {
        using DesktopRoot root = CreateRoot();
        root.Render(new GuiVNode(
            "Window", Key: "window", Title: "Inspector", Width: 320, Height: 180,
            SourceFile: "inspector.tsx", SourceLine: 4, SourceColumn: 2,
            Children: new[] { new GuiVNode("Border", Key: "surface", Background: "#ff0000",
                Children: new[] { Panel(4, Text("Before", "label")) }) }));

        using JsonDocument inspector = JsonDocument.Parse(
            DesktopDevtoolsBridge.InspectDesktopTreeJson());
        JsonElement window = Assert.Single(inspector.RootElement.GetProperty("windows").EnumerateArray());
        Assert.Equal("Window", window.GetProperty("kind").GetString());
        Assert.Equal("window", window.GetProperty("key").GetString());
        Assert.Equal("inspector.tsx", window.GetProperty("source").GetProperty("file").GetString());
        Assert.Equal(320, window.GetProperty("props").GetProperty("width").GetDouble());
        Assert.Equal("Border", Assert.Single(window.GetProperty("children").EnumerateArray())
            .GetProperty("kind").GetString());
    }

    private static void AddDisposedCycle(List<WeakReference> retained, int value)
    {
        object callbackTarget = new();
        var reference = DesktopBridge.CreateRef();
        DesktopRoot root = CreateRoot();
        var controlNode = new GuiVNode(
            "Button", Key: "cycle", Text: value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Click: () => GC.KeepAlive(callbackTarget), AttachRef: reference.Attach, RefIdentity: reference);
        root.Render(Window(controlNode));
        Control control = root.FindControl("cycle")!;
        root.Render(Window(controlNode with { Text = "updated" }));
        root.Dispose();
        Assert.Equal(0, root.ActiveSubscriptions);
        retained.Add(new WeakReference(root));
        retained.Add(new WeakReference(control));
        retained.Add(new WeakReference(callbackTarget));
        retained.Add(new WeakReference(reference));
    }

    [Fact]
    public void FailedRollback_DisposesDamagedWindowRoot()
    {
        using IDisposable registration = DescriptorRegistry.RegisterForTesting(
            new RollbackProbeDescriptor(failEveryUpdateAfterCreate: true));
        DesktopRoot root = CreateRoot();
        root.Render(Window(new GuiVNode("$RollbackProbe", Key: "probe", Text: "stable", Width: 100)));

        AggregateException error = Assert.Throws<AggregateException>(() =>
            root.Render(Window(new GuiVNode("$RollbackProbe", Key: "probe", Text: "changed", Width: 200))));

        Assert.Contains("window root was disposed", error.Message, StringComparison.Ordinal);
        Assert.True(root.IsDisposed);
        Assert.Null(root.Window);
        Assert.Contains(_trace.Snapshot(), item => item.Stage == "fatal-rollback-dispose");
    }

    private static GuiVNode Window(
        GuiVNode? content = null,
        string title = "Test",
        double width = 480,
        double height = 260) =>
        new(
            "Window",
            Key: "window",
            Title: title,
            Width: width,
            Height: height,
            Children: content is null ? Array.Empty<GuiVNode>() : new[] { content });

    private static GuiVNode Panel(double spacing, params GuiVNode[] children) =>
        new("StackPanel", Key: "panel", Spacing: spacing, Children: children);

    private static GuiVNode Text(string text, string? key = null) =>
        new("TextBlock", Key: key, Text: text);

    private static GuiVNode ButtonNode(string text, string? key = null) =>
        new("Button", Key: key, Text: text);

    private static DesktopRoot CreateRoot(Action? cleanup = null) =>
        DesktopBridge.CreateDesktopRoot(cleanup ?? (() => { }));

    private sealed class TestApplication : Application;

    private sealed class RollbackProbeDescriptor(bool failEveryUpdateAfterCreate = false)
        : NodeDescriptor("$RollbackProbe", 0, 0)
    {
        private int _updates;

        public override Control Create(GuiVNode node)
        {
            var control = new TextBlock();
            Update(control, new GuiVNode(Kind), node);
            return control;
        }

        public override bool Update(Control control, GuiVNode previous, GuiVNode next)
        {
            var text = (TextBlock)control;
            text.Width = next.Width;
            _updates++;
            if (next.Text == "throw" || (failEveryUpdateAfterCreate && _updates > 1))
                throw new InvalidOperationException("injected setter failure");
            text.Text = next.Text;
            return true;
        }
    }
}
