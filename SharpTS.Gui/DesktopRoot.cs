
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
    private readonly Action<DesktopRoot, Window> _showWindow;
    private readonly Action<Action> _dispatchGuestCallback;
    private readonly bool _headless;
    private readonly Action _reactiveCleanup;
    private readonly Action<DesktopRoot> _releaseRoot;
    private MountedNode? _mounted;
    private bool _disposed;
    private int _activeSubscriptions;
    private readonly HashSet<Key> _heldKeys = [];
    private KeyEventArgs? _lastKeyDownArgs;
    private bool _lastKeyDownRepeat;
    private int _createOperations;
    private int _descriptorUpdateCalls;
    private int _removeOperations;
    private int _moveOperations;
    private string? _failNextSetterKey;
    private Window? _observedWindow;
    private Window? _closedWindow;
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal DesktopRoot(
        TraceRecorder recorder,
        Action<DesktopRoot, Window> showWindow,
        Action<Action> dispatchGuestCallback,
        bool headless,
        Action reactiveCleanup,
        Action<DesktopRoot> releaseRoot,
        DesktopApplicationSession? application,
        DesktopRoot? owner,
        bool isModal,
        bool isMainWindow)
    {
        _recorder = recorder;
        _showWindow = showWindow;
        _dispatchGuestCallback = dispatchGuestCallback;
        _headless = headless;
        _reactiveCleanup = reactiveCleanup;
        _releaseRoot = releaseRoot;
        Application = application;
        Owner = owner;
        IsModal = isModal;
        IsMainWindow = isMainWindow;
    }

    public Window? Window => _mounted?.Control as Window;
    public DesktopApplicationSession? Application { get; }
    public DesktopRoot? Owner { get; }
    public bool IsModal { get; }
    public bool IsMainWindow { get; }
    public Task Completion => _completion.Task;
    public int ActiveSubscriptions => _activeSubscriptions;
    public bool IsDisposed => _disposed;
    internal RendererOperationCounts OperationCounts =>
        new(_createOperations, _descriptorUpdateCalls, _removeOperations, _moveOperations);
    internal void ResetOperationCounts() =>
        (_createOperations, _descriptorUpdateCalls, _removeOperations, _moveOperations) = (0, 0, 0, 0);
    internal void FailNextSetter(string key) => _failNextSetterKey = key;

    public void Render(GuiVNode root)
    {
        EnsureAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        PreparedNode prepared = VNodeValidator.Prepare(root, requireWindowRoot: true);
        if (_mounted is null)
        {
            MountedNode mounted;
            try
            {
                mounted = BuildDetached(prepared);
            }
            catch (NativeCommitException failure) when (failure.BoundaryPath is not null)
            {
                throw new RecoverableNativeCommitException(failure);
            }
            _mounted = mounted;
            try
            {
                ObserveWindow((Window)mounted.Control);
                _showWindow(this, (Window)mounted.Control);
                ActivateSubtree(mounted);
                _recorder.Record("mount", detail: root.SourceFile);
                _recorder.Record(_headless ? "headless-window-shown" : "real-window-shown");
            }
            catch (Exception commitError)
            {
                try
                {
                    ReleaseSubtree(mounted);
                    DetachNativeTree(mounted);
                    ((Window)mounted.Control).Content = null;
                    CloseNativeWindow((Window)mounted.Control);
                    _mounted = null;
                }
                catch (Exception releaseError)
                {
                    _mounted = null;
                    _disposed = true;
                    _releaseRoot(this);
                    _completion.TrySetResult();
                    throw new AggregateException(
                        "SharpTS GUI initial mount failed and its detached native tree could not be released; the root was disposed.",
                        commitError, releaseError);
                }
                if (commitError is NativeCommitException failure && failure.BoundaryPath is not null)
                    throw new RecoverableNativeCommitException(failure);
                throw;
            }
            return;
        }

        if (CanReuse(_mounted, prepared))
        {
            GuiVNode previousTree = _mounted.VNode;
            try
            {
                ReconcileNode(_mounted, prepared);
            }
            catch (Exception commitError)
            {
                if (commitError is NativeSetterRecoveryException setterRecovery)
                {
                    DisposeDamagedRoot();
                    throw new AggregateException(
                        "SharpTS GUI native commit and rollback both failed; the window root was disposed.",
                        setterRecovery.CommitError,
                        setterRecovery.RecoveryError);
                }
                try
                {
                    ReconcileNode(_mounted, VNodeValidator.Prepare(previousTree, requireWindowRoot: true));
                }
                catch (Exception rollbackError)
                {
                    DisposeDamagedRoot();
                    throw new AggregateException(
                        "SharpTS GUI native commit and rollback both failed; the window root was disposed.",
                        commitError,
                        rollbackError);
                }
                if (commitError is NativeCommitException failure && failure.BoundaryPath is not null)
                    throw new RecoverableNativeCommitException(failure);
                throw commitError is NativeCommitException native ? native.InnerException! : commitError;
            }
            _recorder.Record("render-commit");
            return;
        }

        MountedNode replacement = BuildDetached(prepared);
        MountedNode previous = _mounted;
        ReleaseSubtree(previous);
        DetachNativeTree(previous);
        var previousWindow = (Window)previous.Control;
        previousWindow.Content = null;
        CloseNativeWindow(previousWindow);
        _recorder.Record("reconcile-replace", detail: Describe(previous, prepared));

        _mounted = replacement;
        try
        {
            ObserveWindow((Window)replacement.Control);
            _showWindow(this, (Window)replacement.Control);
            ActivateSubtree(replacement);
            _recorder.Record("render-commit");
        }
        catch
        {
            ReleaseSubtree(replacement);
            ((Window)replacement.Control).Content = null;
            CloseNativeWindow((Window)replacement.Control);
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
                DetachNativeTree(mounted);
                var window = (Window)mounted.Control;
                window.Content = null;
                CloseNativeWindow(window);
                _mounted = null;
            }
            _releaseRoot(this);
            _completion.TrySetResult();
            _recorder.Record("unmount");
        }
    }

    public void Activate()
    {
        EnsureAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        Window?.Activate();
    }

    public void Close()
    {
        EnsureAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        Window?.Close();
    }

    private void ObserveWindow(Window window)
    {
        UnobserveWindow();
        _closedWindow = null;
        _observedWindow = window;
        window.Closed += OnWindowClosed;
    }

    private void UnobserveWindow()
    {
        if (_observedWindow is null)
            return;
        _observedWindow.Closed -= OnWindowClosed;
        _observedWindow = null;
    }

    private void CloseNativeWindow(Window window)
    {
        if (ReferenceEquals(_observedWindow, window))
            UnobserveWindow();
        if (!ReferenceEquals(_closedWindow, window))
            window.Close();
        _closedWindow = null;
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        _closedWindow = sender as Window;
        UnobserveWindow();
        Dispose();
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
        Control control;
        try
        {
            if (ConsumeInjectedFailure(prepared.VNode.Key))
                throw new InvalidOperationException("Injected native create failure for GUI conformance.");
            control = prepared.Descriptor.Create(prepared.VNode);
        }
        catch (Exception error)
        {
            throw NativeCommitException.Wrap(error, prepared.VNode, "create");
        }
        var mounted = new MountedNode(prepared.VNode, prepared.Descriptor, control)
        {
            RefCallback = prepared.VNode.AttachRef,
            RefIdentity = prepared.VNode.RefIdentity,
        };
        UpdateCallbacks(mounted, prepared.VNode);

        try
        {
            foreach (PreparedNode child in prepared.Children)
                mounted.Children.Add(BuildDetached(child));
            InstallDetachedChildren(mounted);
        }
        catch (Exception error)
        {
            try { ReleaseSubtree(mounted); }
            catch (Exception releaseError)
            {
                throw new AggregateException(
                    "Native GUI create failed and its detached partial tree could not be released.",
                    error, releaseError);
            }
            throw error is NativeCommitException
                ? error
                : NativeCommitException.Wrap(error, prepared.VNode, "child collection");
        }
        _recorder.Record("reconcile-create", detail: Describe(mounted));
        _createOperations++;
        return mounted;
    }

    private void ReconcileNode(MountedNode mounted, PreparedNode prepared)
    {
        if (!CanReuse(mounted, prepared))
            throw new InvalidOperationException("The reconciler attempted to update an incompatible node.");

        GuiVNode previousNode = mounted.VNode;
        bool changed = false;
        if (!SameNativeProperties(previousNode, prepared.VNode))
        {
            mounted.SuppressEvents = true;
            try
            {
                _descriptorUpdateCalls++;
                if (ConsumeInjectedFailure(prepared.VNode.Key))
                {
                    throw new InvalidOperationException("Injected native setter failure for GUI conformance.");
                }
                changed = mounted.Descriptor.Update(mounted.Control, previousNode, prepared.VNode);
            }
            catch (Exception error)
            {
                try
                {
                    mounted.Descriptor.Update(mounted.Control, prepared.VNode, previousNode);
                }
                catch (Exception recoveryError)
                {
                    throw new NativeSetterRecoveryException(error, recoveryError);
                }
                throw NativeCommitException.Wrap(error, prepared.VNode, "setter");
            }
            finally
            {
                mounted.SuppressEvents = false;
            }
            SynchronizeEventValue(mounted);
        }
        if (changed)
            _recorder.Record("reconcile-update", detail: Describe(mounted));

        UpdateCallbacks(mounted, prepared.VNode);
        SynchronizeKeyboard(mounted);
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

        MountedNode[] removed = oldChildren
            .Where(oldChild => !desired.Contains(oldChild, ReferenceEqualityComparer.Instance))
            .ToArray();
        try
        {
            foreach (MountedNode oldChild in removed)
            {
                ReleaseSubtree(oldChild);
                RemoveChildControl(parent, oldChild.Control);
                _removeOperations++;
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
                    _moveOperations++;
                    _recorder.Record("reconcile-move", detail: $"{Describe(child)}:{currentIndex}->{index}");
                }
                else
                {
                    InsertChildControl(parent, index, child.Control);
                    if (created.Contains(child))
                        ActivateSubtree(child);
                }
            }
        }
        catch (Exception commitError)
        {
            try
            {
                foreach (MountedNode child in created)
                {
                    RemoveChildControl(parent, child.Control);
                    ReleaseSubtree(child);
                }
                for (int index = 0; index < oldChildren.Count; index++)
                {
                    MountedNode child = oldChildren[index];
                    int currentIndex = IndexOfChildControl(parent, child.Control);
                    if (currentIndex < 0)
                        InsertChildControl(parent, index, child.Control);
                    else if (currentIndex != index)
                        MoveChildControl(parent, currentIndex, index);
                }
                foreach (MountedNode child in removed)
                {
                    MarkSubtreeAlive(child);
                    ActivateSubtree(child);
                }
            }
            catch (Exception recoveryError)
            {
                throw new AggregateException(
                    "Native GUI child mutation and its local recovery both failed.",
                    commitError, recoveryError);
            }
            throw commitError is NativeCommitException
                ? commitError
                : NativeCommitException.Wrap(commitError, parent.VNode, "child collection");
        }

        parent.Children.Clear();
        parent.Children.AddRange(desired);
        foreach (MountedNode oldChild in removed)
        {
            _recorder.Record(
                replaced.Contains(oldChild) ? "reconcile-replace" : "reconcile-remove",
                detail: Describe(oldChild));
        }
    }

    private static void MarkSubtreeAlive(MountedNode mounted)
    {
        mounted.Released = false;
        foreach (MountedNode child in mounted.Children)
            MarkSubtreeAlive(child);
    }

    private void DisposeDamagedRoot()
    {
        _disposed = true;
        if (_mounted is not null)
        {
            MountedNode mounted = _mounted;
            try { ReleaseSubtree(mounted); }
            finally
            {
                DetachNativeTree(mounted);
                var window = (Window)mounted.Control;
                window.Content = null;
                CloseNativeWindow(window);
                _mounted = null;
            }
        }
        _releaseRoot(this);
        _completion.TrySetResult();
        _recorder.Record("fatal-rollback-dispose");
    }

    private void UpdateRef(MountedNode mounted, GuiVNode next)
    {
        if (ReferenceEquals(mounted.RefIdentity, next.RefIdentity))
        {
            mounted.RefCallback = next.AttachRef;
            return;
        }

        try
        {
            DetachRef(mounted);
            mounted.RefIdentity = next.RefIdentity;
            mounted.RefCallback = next.AttachRef;
            AttachRef(mounted);
        }
        catch (Exception error)
        {
            throw NativeCommitException.Wrap(error, next, "ref attach/detach");
        }
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
                    MarkPrimarySubscribed(mounted);
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
                    MarkPrimarySubscribed(mounted);
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
                    MarkPrimarySubscribed(mounted);
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
                    MarkPrimarySubscribed(mounted);
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
                    MarkPrimarySubscribed(mounted);
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
                    MarkPrimarySubscribed(mounted);
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
                    MarkPrimarySubscribed(mounted);
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
                    MarkPrimarySubscribed(mounted);
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
                    MarkPrimarySubscribed(mounted);
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
                    MarkPrimarySubscribed(mounted);
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
                    MarkPrimarySubscribed(mounted);
                    break;
                }
            }

            SynchronizeKeyboard(mounted);
            AttachWindowKeyReset(mounted);
        }

        AttachRef(mounted);
        foreach (MountedNode child in mounted.Children)
            ActivateSubtree(child);
    }

    private void MarkPrimarySubscribed(MountedNode mounted)
    {
        mounted.PrimaryEventAttached = true;
        UpdateSubscriptionState(mounted);
    }

    private void UpdateSubscriptionState(MountedNode mounted)
    {
        bool attached = mounted.PrimaryEventAttached ||
            mounted.KeyDownHandler is not null || mounted.KeyUpHandler is not null;
        if (attached == mounted.EventAttached)
            return;
        mounted.EventAttached = attached;
        _activeSubscriptions += attached ? 1 : -1;
        _recorder.Record(attached ? "subscribe" : "unsubscribe", detail: Describe(mounted));
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
            if (mounted.KeyDownHandler is not null)
                mounted.Control.KeyDown -= mounted.KeyDownHandler;
            if (mounted.KeyUpHandler is not null)
                mounted.Control.KeyUp -= mounted.KeyUpHandler;
            mounted.KeyDownHandler = null;
            mounted.KeyUpHandler = null;
            mounted.PrimaryEventAttached = false;
            mounted.RoutedHandler = null;
            mounted.TextHandler = null;
            mounted.SelectionHandler = null;
            mounted.ValueHandler = null;
            UpdateSubscriptionState(mounted);
        }
        foreach (Action unsubscribe in mounted.ExtraUnsubscribe)
            unsubscribe();
        mounted.ExtraUnsubscribe.Clear();
        mounted.WindowKeyResetAttached = false;
        DetachRef(mounted);
        mounted.Released = true;
    }

    private void SynchronizeKeyboard(MountedNode mounted)
    {
        if (mounted.LatestKeyDown is not null && mounted.KeyDownHandler is null)
        {
            EventHandler<KeyEventArgs> handler = (_, args) => DispatchKey(mounted, args, keyDown: true);
            mounted.Control.KeyDown += handler;
            mounted.KeyDownHandler = handler;
        }
        else if (mounted.LatestKeyDown is null && mounted.KeyDownHandler is not null)
        {
            mounted.Control.KeyDown -= mounted.KeyDownHandler;
            mounted.KeyDownHandler = null;
            ClearHeldKeys();
        }
        bool needsKeyUp = mounted.LatestKeyDown is not null || mounted.LatestKeyUp is not null;
        if (needsKeyUp && mounted.KeyUpHandler is null)
        {
            EventHandler<KeyEventArgs> handler = (_, args) => DispatchKey(mounted, args, keyDown: false);
            mounted.Control.KeyUp += handler;
            mounted.KeyUpHandler = handler;
        }
        else if (!needsKeyUp && mounted.KeyUpHandler is not null)
        {
            mounted.Control.KeyUp -= mounted.KeyUpHandler;
            mounted.KeyUpHandler = null;
            ClearHeldKeys();
        }
        UpdateSubscriptionState(mounted);
    }

    private void AttachWindowKeyReset(MountedNode mounted)
    {
        if (mounted.WindowKeyResetAttached || mounted.Control is not Window window)
            return;
        EventHandler deactivated = (_, _) => ClearHeldKeys();
        EventHandler<RoutedEventArgs> lostFocus = (_, _) => ClearHeldKeys();
        window.Deactivated += deactivated;
        window.LostFocus += lostFocus;
        mounted.ExtraUnsubscribe.Add(() => window.Deactivated -= deactivated);
        mounted.ExtraUnsubscribe.Add(() => window.LostFocus -= lostFocus);
        mounted.WindowKeyResetAttached = true;
    }

    private void DispatchKey(MountedNode mounted, KeyEventArgs args, bool keyDown)
    {
        bool repeat = false;
        if (keyDown)
        {
            if (ReferenceEquals(args, _lastKeyDownArgs))
            {
                repeat = _lastKeyDownRepeat;
            }
            else
            {
                repeat = !_heldKeys.Add(args.Key);
                _lastKeyDownArgs = args;
                _lastKeyDownRepeat = repeat;
            }
        }
        if (!keyDown)
        {
            _heldKeys.Remove(args.Key);
            _lastKeyDownArgs = null;
        }
        Func<string, bool, bool, bool, bool, bool, bool>? latest = keyDown ? mounted.LatestKeyDown : mounted.LatestKeyUp;
        if (latest is null) return;
        KeyModifiers modifiers = args.KeyModifiers;
        bool handled = false;
        _dispatchGuestCallback(() => handled = latest(
            NormalizeKey(args.Key, modifiers),
            modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Alt),
            modifiers.HasFlag(KeyModifiers.Shift),
            modifiers.HasFlag(KeyModifiers.Meta),
            repeat));
        args.Handled = handled;
    }

    private void ClearHeldKeys()
    {
        _heldKeys.Clear();
        _lastKeyDownArgs = null;
        _lastKeyDownRepeat = false;
    }

    private static string NormalizeKey(Key key, KeyModifiers modifiers) => key switch
    {
        Key.D5 when modifiers.HasFlag(KeyModifiers.Shift) => "%",
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
        try { mounted.RefCallback(mounted.Handle); }
        catch (Exception error) { throw NativeCommitException.Wrap(error, mounted.VNode, "ref attach"); }
        mounted.RefAttached = true;
        _recorder.Record("ref-attach", detail: Describe(mounted));
    }

    private void DetachRef(MountedNode mounted)
    {
        if (!mounted.RefAttached || mounted.RefCallback is null)
            return;
        EnsureAccess();
        try { mounted.RefCallback(null); }
        catch (Exception error) { throw NativeCommitException.Wrap(error, mounted.VNode, "ref detach"); }
        mounted.RefAttached = false;
        _recorder.Record("ref-detach", detail: Describe(mounted));
    }

    private static bool CanReuse(MountedNode mounted, PreparedNode prepared) =>
        mounted.Descriptor.Kind == prepared.Descriptor.Kind &&
        string.Equals(mounted.VNode.Key, prepared.VNode.Key, StringComparison.Ordinal);

    private bool ConsumeInjectedFailure(string? nativeKey)
    {
        if (_failNextSetterKey is not { } failureKey ||
            !(string.Equals(failureKey, nativeKey, StringComparison.Ordinal) ||
              string.Equals("$" + failureKey, nativeKey, StringComparison.Ordinal) ||
              nativeKey?.EndsWith("/$" + failureKey, StringComparison.Ordinal) == true))
            return false;
        _failNextSetterKey = null;
        return true;
    }

    private static bool SameNativeProperties(GuiVNode left, GuiVNode right)
    {
        GuiVNode Normalize(GuiVNode node) => node with
        {
            Children = null,
            Items = null,
            SelectedIndices = null,
            Click = null,
            TextChanged = null,
            CheckedChanged = null,
            SelectionChanged = null,
            ValueChanged = null,
            IndicesChanged = null,
            NullableValueChanged = null,
            NullableStringChanged = null,
            KeyDown = null,
            KeyUp = null,
            Loaded = null,
            LoadError = null,
            AttachRef = null,
            RefIdentity = null,
            SourceFile = null,
            SourceLine = 0,
            SourceColumn = 0,
            BoundaryPath = null,
        };
        return Normalize(left) == Normalize(right) &&
            (left.Items ?? []).SequenceEqual(right.Items ?? [], StringComparer.Ordinal) &&
            (left.SelectedIndices ?? []).SequenceEqual(right.SelectedIndices ?? []);
    }

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

    private static void DetachNativeTree(MountedNode mounted)
    {
        foreach (MountedNode child in mounted.Children)
            DetachNativeTree(child);
        switch (mounted.Control)
        {
            case Panel panel:
                panel.Children.Clear();
                break;
            case ItemsControl items when items.ItemsSource is null:
                items.Items.Clear();
                break;
            default:
                SetSingleChild(mounted.Control, null);
                break;
        }
        mounted.Children.Clear();
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
