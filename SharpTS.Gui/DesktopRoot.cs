
using System.Collections;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Controls.Primitives;

namespace SharpTS.Gui;

public sealed class DesktopRoot : IDisposable
{
    private readonly TraceRecorder _recorder;
    private readonly Action<Window> _showWindow;
    private readonly Action<Action> _dispatchGuestCallback;
    private readonly bool _headless;
    private readonly Action _reactiveCleanup;
    private readonly Action<DesktopRoot> _releaseRoot;
    private MountedNode? _mounted;
    private bool _disposed;
    private int _activeSubscriptions;

    internal DesktopRoot(
        TraceRecorder recorder,
        Action<Window> showWindow,
        Action<Action> dispatchGuestCallback,
        bool headless,
        Action reactiveCleanup,
        Action<DesktopRoot> releaseRoot)
    {
        _recorder = recorder;
        _showWindow = showWindow;
        _dispatchGuestCallback = dispatchGuestCallback;
        _headless = headless;
        _reactiveCleanup = reactiveCleanup;
        _releaseRoot = releaseRoot;
    }

    public Window? Window => _mounted?.Control as Window;
    public int ActiveSubscriptions => _activeSubscriptions;
    public bool IsDisposed => _disposed;

    public void Render(GuiVNode root)
    {
        EnsureAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        PreparedNode prepared = VNodeValidator.Prepare(root, requireWindowRoot: true);
        if (_mounted is null)
        {
            MountedNode mounted = BuildDetached(prepared);
            _mounted = mounted;
            try
            {
                _showWindow((Window)mounted.Control);
                ActivateSubtree(mounted);
                _recorder.Record("mount", detail: root.SourceFile);
                _recorder.Record(_headless ? "headless-window-shown" : "real-window-shown");
            }
            catch
            {
                ReleaseSubtree(mounted);
                ((Window)mounted.Control).Content = null;
                ((Window)mounted.Control).Close();
                _mounted = null;
                throw;
            }
            return;
        }

        if (CanReuse(_mounted, prepared))
        {
            ReconcileNode(_mounted, prepared);
            _recorder.Record("render-commit");
            return;
        }

        MountedNode replacement = BuildDetached(prepared);
        MountedNode previous = _mounted;
        ReleaseSubtree(previous);
        var previousWindow = (Window)previous.Control;
        previousWindow.Content = null;
        previousWindow.Close();
        _recorder.Record("reconcile-replace", detail: Describe(previous, prepared));

        _mounted = replacement;
        try
        {
            _showWindow((Window)replacement.Control);
            ActivateSubtree(replacement);
            _recorder.Record("render-commit");
        }
        catch
        {
            ReleaseSubtree(replacement);
            ((Window)replacement.Control).Content = null;
            ((Window)replacement.Control).Close();
            _mounted = null;
            throw;
        }
    }

    public void Dispose()
    {
        EnsureAccess();
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _reactiveCleanup();
        }
        finally
        {
            if (_mounted is not null)
            {
                MountedNode mounted = _mounted;
                ReleaseSubtree(mounted);
                var window = (Window)mounted.Control;
                window.Content = null;
                window.Close();
                _mounted = null;
            }
            _releaseRoot(this);
            _recorder.Record("unmount");
        }
    }

    internal Button? FindFirstButton() =>
        _mounted is null ? null : FindFirstButton(_mounted);

    internal string GetKeyedControlIdentities()
    {
        if (_mounted is null)
            return string.Empty;
        var identities = new List<string>();
        CollectIdentities(_mounted, identities);
        return string.Join(";", identities);
    }

    internal Control? FindControl(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _mounted is null ? null : FindControl(_mounted, key);
    }

    private MountedNode BuildDetached(PreparedNode prepared)
    {
        Control control = prepared.Descriptor.Create(prepared.VNode);
        var mounted = new MountedNode(prepared.VNode, prepared.Descriptor, control)
        {
            RefCallback = prepared.VNode.AttachRef,
            RefIdentity = prepared.VNode.RefIdentity,
        };
        UpdateCallbacks(mounted, prepared.VNode);

        foreach (PreparedNode child in prepared.Children)
            mounted.Children.Add(BuildDetached(child));
        InstallDetachedChildren(mounted);
        _recorder.Record("reconcile-create", detail: Describe(mounted));
        return mounted;
    }

    private void ReconcileNode(MountedNode mounted, PreparedNode prepared)
    {
        if (!CanReuse(mounted, prepared))
            throw new InvalidOperationException("The reconciler attempted to update an incompatible node.");

        bool changed;
        mounted.SuppressEvents = true;
        try
        {
            changed = mounted.Descriptor.Update(mounted.Control, mounted.VNode, prepared.VNode);
        }
        finally
        {
            mounted.SuppressEvents = false;
        }
        SynchronizeEventValue(mounted);
        if (changed)
            _recorder.Record("reconcile-update", detail: Describe(mounted));

        UpdateCallbacks(mounted, prepared.VNode);
        ReconcileChildren(mounted, prepared.Children);
        UpdateRef(mounted, prepared.VNode);
        mounted.VNode = prepared.VNode;
    }

    private static void UpdateCallbacks(MountedNode mounted, GuiVNode node)
    {
        mounted.LatestClick = node.Click;
        mounted.LatestTextChanged = node.TextChanged;
        mounted.LatestCheckedChanged = node.CheckedChanged;
        mounted.LatestSelectionChanged = node.SelectionChanged;
        mounted.LatestValueChanged = node.ValueChanged;
        mounted.LatestIndicesChanged = node.IndicesChanged;
        mounted.LatestNullableValueChanged = node.NullableValueChanged;
        mounted.LatestNullableStringChanged = node.NullableStringChanged;
        mounted.LatestKeyDown = node.KeyDown;
        mounted.LatestKeyUp = node.KeyUp;
    }

    private static void SynchronizeEventValue(MountedNode mounted)
    {
        switch (mounted.Control)
        {
            case TextBox textBox:
                mounted.LastTextValue = textBox.Text ?? string.Empty;
                break;
            case ToggleButton checkBox:
                mounted.LastCheckedValue = checkBox.IsChecked == true;
                break;
            case ComboBox comboBox:
                mounted.LastSelectionValue = comboBox.SelectedIndex;
                break;
            case Slider slider:
                mounted.LastNumberValue = slider.Value;
                break;
        }
    }

    private void ReconcileChildren(MountedNode parent, IReadOnlyList<PreparedNode> newChildren)
    {
        IReadOnlyList<MountedNode> oldChildren = parent.Children.ToArray();
        var keyed = new Dictionary<string, MountedNode>(StringComparer.Ordinal);
        foreach (MountedNode oldChild in oldChildren)
            if (oldChild.VNode.Key is not null)
                keyed.Add(oldChild.VNode.Key, oldChild);

        var used = new HashSet<MountedNode>(ReferenceEqualityComparer.Instance);
        var desired = new List<MountedNode>(newChildren.Count);
        var created = new HashSet<MountedNode>(ReferenceEqualityComparer.Instance);
        var replaced = new HashSet<MountedNode>(ReferenceEqualityComparer.Instance);

        for (int index = 0; index < newChildren.Count; index++)
        {
            PreparedNode prepared = newChildren[index];
            MountedNode? candidate = null;
            if (prepared.VNode.Key is not null)
            {
                keyed.TryGetValue(prepared.VNode.Key, out candidate);
            }
            else if (index < oldChildren.Count &&
                oldChildren[index].VNode.Key is null &&
                !used.Contains(oldChildren[index]))
            {
                candidate = oldChildren[index];
            }

            if (candidate is not null && used.Add(candidate))
            {
                if (CanReuse(candidate, prepared))
                {
                    ReconcileNode(candidate, prepared);
                    desired.Add(candidate);
                    continue;
                }
                replaced.Add(candidate);
            }

            MountedNode newNode = BuildDetached(prepared);
            desired.Add(newNode);
            created.Add(newNode);
        }

        foreach (MountedNode oldChild in oldChildren)
        {
            if (desired.Contains(oldChild, ReferenceEqualityComparer.Instance))
                continue;
            ReleaseSubtree(oldChild);
            RemoveChildControl(parent, oldChild.Control);
            _recorder.Record(
                replaced.Contains(oldChild) ? "reconcile-replace" : "reconcile-remove",
                detail: Describe(oldChild));
        }

        for (int index = 0; index < desired.Count; index++)
        {
            MountedNode child = desired[index];
            int currentIndex = IndexOfChildControl(parent, child.Control);
            if (currentIndex == index)
                continue;

            if (currentIndex >= 0)
            {
                MoveChildControl(parent, currentIndex, index);
                _recorder.Record("reconcile-move", detail: $"{Describe(child)}:{currentIndex}->{index}");
            }
            else
            {
                InsertChildControl(parent, index, child.Control);
                if (created.Contains(child))
                    ActivateSubtree(child);
            }
        }

        parent.Children.Clear();
        parent.Children.AddRange(desired);
    }

    private void UpdateRef(MountedNode mounted, GuiVNode next)
    {
        if (ReferenceEquals(mounted.RefIdentity, next.RefIdentity))
        {
            mounted.RefCallback = next.AttachRef;
            return;
        }

        DetachRef(mounted);
        mounted.RefIdentity = next.RefIdentity;
        mounted.RefCallback = next.AttachRef;
        AttachRef(mounted);
    }

    private void ActivateSubtree(MountedNode mounted)
    {
        if (!mounted.EventAttached)
        {
            switch (mounted.Control)
            {
                case TextBox textBox:
                {
                    mounted.LastTextValue = textBox.Text ?? string.Empty;
                    EventHandler<TextChangedEventArgs> handler = (_, _) =>
                    {
                        if (mounted.SuppressEvents)
                            return;
                        EnsureAccess();
                        string value = textBox.Text ?? string.Empty;
                        if (string.Equals(mounted.LastTextValue, value, StringComparison.Ordinal))
                            return;
                        mounted.LastTextValue = value;
                        _recorder.Record("text-changed-event", detail: mounted.VNode.Key);
                        Action<string>? latest = mounted.LatestTextChanged;
                        if (latest is not null)
                            _dispatchGuestCallback(() => latest(value));
                    };
                    mounted.TextHandler = handler;
                    textBox.TextChanged += handler;
                    MarkSubscribed(mounted);
                    break;
                }
                case ToggleButton checkBox:
                {
                    mounted.LastCheckedValue = checkBox.IsChecked == true;
                    EventHandler<RoutedEventArgs> handler = (_, _) =>
                    {
                        if (mounted.SuppressEvents)
                            return;
                        EnsureAccess();
                        bool value = checkBox.IsChecked == true;
                        if (mounted.LastCheckedValue == value)
                            return;
                        mounted.LastCheckedValue = value;
                        _recorder.Record("checked-changed-event", detail: mounted.VNode.Key);
                        Action<bool>? latest = mounted.LatestCheckedChanged;
                        if (latest is not null)
                            _dispatchGuestCallback(() => latest(value));
                    };
                    mounted.RoutedHandler = handler;
                    checkBox.IsCheckedChanged += handler;
                    MarkSubscribed(mounted);
                    break;
                }
                case Button button:
                {
                    EventHandler<RoutedEventArgs> handler = (_, _) =>
                    {
                        if (mounted.SuppressEvents)
                            return;
                        EnsureAccess();
                        _recorder.Record("button-click-event", detail: mounted.VNode.Key);
                        Action? latest = mounted.LatestClick;
                        if (latest is not null)
                            _dispatchGuestCallback(latest);
                    };
                    mounted.RoutedHandler = handler;
                    button.Click += handler;
                    MarkSubscribed(mounted);
                    break;
                }
                case MenuItem menuItem:
                {
                    EventHandler<RoutedEventArgs> handler = (_, _) =>
                    {
                        if (mounted.SuppressEvents)
                            return;
                        EnsureAccess();
                        _recorder.Record("menu-click-event", detail: mounted.VNode.Key);
                        Action? latest = mounted.LatestClick;
                        if (latest is not null)
                            _dispatchGuestCallback(latest);
                    };
                    menuItem.Click += handler;
                    mounted.ExtraUnsubscribe.Add(() => menuItem.Click -= handler);
                    MarkSubscribed(mounted);
                    break;
                }
                case ComboBox comboBox:
                {
                    mounted.LastSelectionValue = comboBox.SelectedIndex;
                    EventHandler<SelectionChangedEventArgs> handler = (_, _) =>
                    {
                        if (mounted.SuppressEvents)
                            return;
                        EnsureAccess();
                        int selectedIndex = comboBox.SelectedIndex;
                        if (mounted.LastSelectionValue == selectedIndex)
                            return;
                        mounted.LastSelectionValue = selectedIndex;
                        double value = selectedIndex;
                        _recorder.Record("selection-changed-event", detail: mounted.VNode.Key);
                        Action<double>? latest = mounted.LatestSelectionChanged;
                        if (latest is not null)
                            _dispatchGuestCallback(() => latest(value));
                    };
                    mounted.SelectionHandler = handler;
                    comboBox.SelectionChanged += handler;
                    MarkSubscribed(mounted);
                    break;
                }
                case Slider slider:
                {
                    mounted.LastNumberValue = slider.Value;
                    EventHandler<RangeBaseValueChangedEventArgs> handler = (_, args) =>
                    {
                        if (mounted.SuppressEvents)
                            return;
                        EnsureAccess();
                        double value = args.NewValue;
                        if (mounted.LastNumberValue.Equals(value))
                            return;
                        mounted.LastNumberValue = value;
                        _recorder.Record("value-changed-event", detail: mounted.VNode.Key);
                        Action<double>? latest = mounted.LatestValueChanged;
                        if (latest is not null)
                            _dispatchGuestCallback(() => latest(value));
                    };
                    mounted.ValueHandler = handler;
                    slider.ValueChanged += handler;
                    MarkSubscribed(mounted);
                    break;
                }
                case ListBox listBox:
                {
                    mounted.LastIndices = SelectedIndices(listBox);
                    EventHandler<SelectionChangedEventArgs> handler = (_, _) =>
                    {
                        if (mounted.SuppressEvents) return;
                        int[] indices = SelectedIndices(listBox);
                        if (mounted.LastIndices.SequenceEqual(indices)) return;
                        mounted.LastIndices = indices;
                        Action<int[]>? latest = mounted.LatestIndicesChanged;
                        if (latest is not null) _dispatchGuestCallback(() => latest(indices));
                    };
                    listBox.SelectionChanged += handler;
                    mounted.ExtraUnsubscribe.Add(() => listBox.SelectionChanged -= handler);
                    MarkSubscribed(mounted);
                    break;
                }
                case NumericUpDown numeric:
                {
                    mounted.LastNullableNumberValue = numeric.Value is decimal value ? (double)value : null;
                    EventHandler<NumericUpDownValueChangedEventArgs> handler = (_, _) =>
                    {
                        if (mounted.SuppressEvents) return;
                        double? value = numeric.Value is decimal number ? (double)number : null;
                        if (mounted.LastNullableNumberValue == value) return;
                        mounted.LastNullableNumberValue = value;
                        Action<double?>? latest = mounted.LatestNullableValueChanged;
                        if (latest is not null) _dispatchGuestCallback(() => latest(value));
                    };
                    numeric.ValueChanged += handler;
                    mounted.ExtraUnsubscribe.Add(() => numeric.ValueChanged -= handler);
                    MarkSubscribed(mounted);
                    break;
                }
                case DatePicker date:
                {
                    EventHandler<DatePickerSelectedValueChangedEventArgs> handler = (_, _) =>
                    {
                        if (mounted.SuppressEvents) return;
                        string? value = date.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        Action<string?>? latest = mounted.LatestNullableStringChanged;
                        if (latest is not null) _dispatchGuestCallback(() => latest(value));
                    };
                    date.SelectedDateChanged += handler;
                    mounted.ExtraUnsubscribe.Add(() => date.SelectedDateChanged -= handler);
                    MarkSubscribed(mounted);
                    break;
                }
                case TimePicker time:
                {
                    EventHandler<TimePickerSelectedValueChangedEventArgs> handler = (_, _) =>
                    {
                        if (mounted.SuppressEvents) return;
                        string? value = time.SelectedTime is TimeSpan selected
                            ? selected.ToString(time.UseSeconds ? @"hh\:mm\:ss" : @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture)
                            : null;
                        Action<string?>? latest = mounted.LatestNullableStringChanged;
                        if (latest is not null) _dispatchGuestCallback(() => latest(value));
                    };
                    time.SelectedTimeChanged += handler;
                    mounted.ExtraUnsubscribe.Add(() => time.SelectedTimeChanged -= handler);
                    MarkSubscribed(mounted);
                    break;
                }
                case TabControl tabs:
                {
                    mounted.LastSelectionValue = tabs.SelectedIndex;
                    EventHandler<SelectionChangedEventArgs> handler = (_, _) =>
                    {
                        if (mounted.SuppressEvents || mounted.LastSelectionValue == tabs.SelectedIndex) return;
                        mounted.LastSelectionValue = tabs.SelectedIndex;
                        Action<double>? latest = mounted.LatestSelectionChanged;
                        if (latest is not null) _dispatchGuestCallback(() => latest(tabs.SelectedIndex));
                    };
                    tabs.SelectionChanged += handler;
                    mounted.ExtraUnsubscribe.Add(() => tabs.SelectionChanged -= handler);
                    MarkSubscribed(mounted);
                    break;
                }
            }

            AttachKeyboard(mounted);
        }

        AttachRef(mounted);
        foreach (MountedNode child in mounted.Children)
            ActivateSubtree(child);
    }

    private void MarkSubscribed(MountedNode mounted)
    {
        mounted.EventAttached = true;
        _activeSubscriptions++;
        _recorder.Record("subscribe", detail: Describe(mounted));
    }

    private void ReleaseSubtree(MountedNode mounted)
    {
        if (mounted.Released)
            return;
        foreach (MountedNode child in mounted.Children)
            ReleaseSubtree(child);

        if (mounted.EventAttached)
        {
            switch (mounted.Control)
            {
                case ToggleButton checkBox when mounted.RoutedHandler is not null:
                    checkBox.IsCheckedChanged -= mounted.RoutedHandler;
                    break;
                case Button button when mounted.RoutedHandler is not null:
                    button.Click -= mounted.RoutedHandler;
                    break;
                case TextBox textBox when mounted.TextHandler is not null:
                    textBox.TextChanged -= mounted.TextHandler;
                    break;
                case ComboBox comboBox when mounted.SelectionHandler is not null:
                    comboBox.SelectionChanged -= mounted.SelectionHandler;
                    break;
                case Slider slider when mounted.ValueHandler is not null:
                    slider.ValueChanged -= mounted.ValueHandler;
                    break;
            }
            foreach (Action unsubscribe in mounted.ExtraUnsubscribe)
                unsubscribe();
            mounted.ExtraUnsubscribe.Clear();
            mounted.EventAttached = false;
            mounted.RoutedHandler = null;
            mounted.TextHandler = null;
            mounted.SelectionHandler = null;
            mounted.ValueHandler = null;
            _activeSubscriptions--;
            _recorder.Record("unsubscribe", detail: Describe(mounted));
        }
        DetachRef(mounted);
        mounted.Released = true;
    }

    private void AttachKeyboard(MountedNode mounted)
    {
        if (mounted.LatestKeyDown is not null)
        {
            EventHandler<KeyEventArgs> handler = (_, args) => DispatchKey(mounted, args, keyDown: true);
            mounted.Control.KeyDown += handler;
            mounted.ExtraUnsubscribe.Add(() => mounted.Control.KeyDown -= handler);
            if (!mounted.EventAttached) MarkSubscribed(mounted);
        }
        if (mounted.LatestKeyUp is not null)
        {
            EventHandler<KeyEventArgs> handler = (_, args) => DispatchKey(mounted, args, keyDown: false);
            mounted.Control.KeyUp += handler;
            mounted.ExtraUnsubscribe.Add(() => mounted.Control.KeyUp -= handler);
            if (!mounted.EventAttached) MarkSubscribed(mounted);
        }
    }

    private void DispatchKey(MountedNode mounted, KeyEventArgs args, bool keyDown)
    {
        Func<string, bool, bool, bool, bool, bool, bool>? latest = keyDown ? mounted.LatestKeyDown : mounted.LatestKeyUp;
        if (latest is null) return;
        KeyModifiers modifiers = args.KeyModifiers;
        bool handled = false;
        _dispatchGuestCallback(() => handled = latest(
            NormalizeKey(args.Key),
            modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Alt),
            modifiers.HasFlag(KeyModifiers.Shift),
            modifiers.HasFlag(KeyModifiers.Meta),
            args.KeyModifiers.HasFlag(KeyModifiers.None) && false));
        args.Handled = handled;
    }

    private static string NormalizeKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)key - (int)Key.NumPad0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        Key.Add or Key.OemPlus => "+",
        Key.Subtract or Key.OemMinus => "-",
        Key.Multiply => "*",
        Key.Divide => "/",
        Key.Decimal or Key.OemPeriod => ".",
        Key.Return => "Enter",
        Key.Escape => "Escape",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        _ => key.ToString(),
    };

    private static int[] SelectedIndices(ListBox listBox)
    {
        object[] items = listBox.ItemsView.Cast<object>().ToArray();
        return (listBox.SelectedItems?.Cast<object>() ?? [])
            .Select(item => Array.IndexOf(items, item)).Where(index => index >= 0).Order().ToArray();
    }

    private void AttachRef(MountedNode mounted)
    {
        if (mounted.RefAttached || mounted.RefCallback is null)
            return;
        EnsureAccess();
        mounted.RefCallback(mounted.Handle);
        mounted.RefAttached = true;
        _recorder.Record("ref-attach", detail: Describe(mounted));
    }

    private void DetachRef(MountedNode mounted)
    {
        if (!mounted.RefAttached || mounted.RefCallback is null)
            return;
        EnsureAccess();
        mounted.RefCallback(null);
        mounted.RefAttached = false;
        _recorder.Record("ref-detach", detail: Describe(mounted));
    }

    private static bool CanReuse(MountedNode mounted, PreparedNode prepared) =>
        mounted.Descriptor.Kind == prepared.Descriptor.Kind &&
        string.Equals(mounted.VNode.Key, prepared.VNode.Key, StringComparison.Ordinal);

    private static void InstallDetachedChildren(MountedNode parent)
    {
        if (parent.Control is Panel panel)
        {
            foreach (MountedNode child in parent.Children)
                panel.Children.Add(child.Control);
            return;
        }
        if (parent.Control is ItemsControl items)
        {
            foreach (MountedNode child in parent.Children)
                items.Items.Add(child.Control);
            return;
        }
        if (parent.Children.Count > 0)
            SetSingleChild(parent.Control, parent.Children[0].Control);
    }

    private static void RemoveChildControl(MountedNode parent, Control child)
    {
        if (parent.Control is Panel panel)
        {
            panel.Children.Remove(child);
            return;
        }
        if (parent.Control is ItemsControl items)
        {
            items.Items.Remove(child);
            return;
        }
        if (ReferenceEquals(GetSingleChild(parent.Control), child))
            SetSingleChild(parent.Control, null);
    }

    private static int IndexOfChildControl(MountedNode parent, Control child)
    {
        if (parent.Control is Panel panel)
            return panel.Children.IndexOf(child);
        if (parent.Control is ItemsControl items)
            return items.Items.IndexOf(child);
        return ReferenceEquals(GetSingleChild(parent.Control), child) ? 0 : -1;
    }

    private static void InsertChildControl(MountedNode parent, int index, Control child)
    {
        if (parent.Control is Panel panel)
        {
            panel.Children.Insert(index, child);
            return;
        }
        if (parent.Control is ItemsControl items)
        {
            items.Items.Insert(index, child);
            return;
        }
        if (index != 0 || GetSingleChild(parent.Control) is not null)
            throw new InvalidOperationException($"{parent.Descriptor.Kind} content insertion violated its cardinality.");
        SetSingleChild(parent.Control, child);
    }

    private static void MoveChildControl(MountedNode parent, int oldIndex, int newIndex)
    {
        if (parent.Control is Panel panel)
        {
            Control child = panel.Children[oldIndex];
            panel.Children.RemoveAt(oldIndex);
            panel.Children.Insert(newIndex, child);
        }
        else if (parent.Control is ItemsControl items)
        {
            object? child = items.Items[oldIndex];
            items.Items.RemoveAt(oldIndex);
            items.Items.Insert(newIndex, child);
        }
    }

    private static Control? GetSingleChild(Control control) => control switch
    {
        Window window => window.Content as Control,
        Border border => border.Child,
        ScrollViewer scrollViewer => scrollViewer.Content as Control,
        ContentControl contentControl => contentControl.Content as Control,
        _ => null,
    };

    private static void SetSingleChild(Control control, Control? child)
    {
        switch (control)
        {
            case Window window:
                window.Content = child;
                break;
            case Border border:
                border.Child = child;
                break;
            case ScrollViewer scrollViewer:
                scrollViewer.Content = child;
                break;
            case ContentControl contentControl:
                contentControl.Content = child;
                break;
            default:
                if (child is not null)
                    throw new InvalidOperationException($"{control.GetType().Name} does not accept child controls.");
                break;
        }
    }

    private void EnsureAccess()
    {
        if (Environment.CurrentManagedThreadId != _recorder.OwnerThreadId)
        {
            throw new InvalidOperationException(
                $"DesktopRoot ran on managed thread {Environment.CurrentManagedThreadId}; " +
                $"owner is {_recorder.OwnerThreadId}.");
        }
    }

    private static string Describe(MountedNode node) =>
        node.VNode.Key is null ? node.Descriptor.Kind : $"{node.Descriptor.Kind}#{node.VNode.Key}";

    private static string Describe(MountedNode oldNode, PreparedNode next) =>
        $"{Describe(oldNode)}->{next.Descriptor.Kind}";

    private static Button? FindFirstButton(MountedNode node)
    {
        if (node.Control is Button button && node.Control is not CheckBox)
            return button;
        foreach (MountedNode child in node.Children)
            if (FindFirstButton(child) is Button found)
                return found;
        return null;
    }

    private static Control? FindControl(MountedNode node, string key)
    {
        if (string.Equals(node.VNode.Key, key, StringComparison.Ordinal))
            return node.Control;
        foreach (MountedNode child in node.Children)
            if (FindControl(child, key) is Control found)
                return found;
        return null;
    }

    private static void CollectIdentities(MountedNode node, List<string> identities)
    {
        if (node.VNode.Key is not null)
            identities.Add($"{node.VNode.Key}={RuntimeHelpers.GetHashCode(node.Control)}:{node.Descriptor.Kind}");
        foreach (MountedNode child in node.Children)
            CollectIdentities(child, identities);
    }
}
