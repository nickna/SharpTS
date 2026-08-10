using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using BenchmarkDotNet.Attributes;
using SharpTS.Gui;

namespace SharpTS.Gui.Benchmarks;

[MemoryDiagnoser]
public class GuiRendererBenchmarks
{
    private DesktopRuntimeRegistration _registration = null!;
    private DesktopApplicationSession? _application;
    private DesktopRoot? _root;
    private int _value;

    [GlobalSetup]
    public void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<BenchmarkApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        _registration = DesktopBridge.Configure(
            new TraceRecorder(Environment.CurrentManagedThreadId), (_, _) => { }, true,
            callback => callback(), callback => callback());
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        _root?.Dispose();
        _root = null;
        _application?.Dispose();
        _application = null;
    }

    [GlobalCleanup]
    public void Cleanup() => _registration.Dispose();

    [Benchmark(Baseline = true)]
    public Window DirectAvaloniaInitialMount() => DirectTree();

    [Benchmark]
    public UserControl CompiledXamlShapeBaseline() => new BaselineView();

    [Benchmark]
    public Window SharpTsInitialMount()
    {
        _application = DesktopBridge.CreateDesktopApplication("explicit");
        _root = _application.CreateWindowRoot(() => { }, null, false, true);
        _root.Render(Tree(0));
        return _root.Window!;
    }

    [Benchmark]
    public void ScalarUpdate()
    {
        EnsureMounted();
        _root!.Render(Tree(++_value));
    }

    [Benchmark(OperationsPerInvoke = 10)]
    public void BatchedScalarUpdates()
    {
        EnsureMounted();
        for (int index = 0; index < 10; index++) _root!.Render(Tree(++_value));
    }

    [Benchmark]
    public void KeyedInsertMoveRemove()
    {
        EnsureMounted();
        _root!.Render(Tree(++_value, reverse: true, includeThird: true));
        _root.Render(Tree(++_value, reverse: false, includeThird: false));
    }

    [Benchmark]
    public void InputToRenderLatency()
    {
        EnsureMounted(withButton: true);
        var button = (Button)_root!.FindControl("input")!;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private void EnsureMounted(bool withButton = false)
    {
        if (_root is not null) return;
        _application = DesktopBridge.CreateDesktopApplication("explicit");
        _root = _application.CreateWindowRoot(() => { }, null, false, true);
        _root.Render(Tree(_value, withButton: withButton));
    }

    private GuiVNode Tree(int value, bool reverse = false, bool includeThird = false, bool withButton = false)
    {
        GuiVNode[] items = reverse
            ? [Text("B " + value, "b"), Text("A " + value, "a")]
            : [Text("A " + value, "a"), Text("B " + value, "b")];
        var children = items.ToList();
        if (includeThird) children.Insert(1, Text("C " + value, "c"));
        if (withButton) children.Add(new GuiVNode("Button", Key: "input", Text: "Update", Click: () => _root!.Render(Tree(++_value))));
        return new GuiVNode("Window", Key: "window", Children: new[] { new GuiVNode("StackPanel", Key: "panel", Children: children.ToArray()) });
    }

    private static GuiVNode Text(string value, string key) => new("TextBlock", Key: key, Text: value);

    private static Window DirectTree() => new()
    {
        Content = new StackPanel { Children = { new TextBlock { Text = "A 0" }, new TextBlock { Text = "B 0" } } },
    };

    private sealed class BenchmarkApplication : Application;
}

internal sealed partial class BaselineView : UserControl
{
    public BaselineView() => AvaloniaXamlLoader.Load(this);
}
