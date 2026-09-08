using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace SharpTS.Gui;

internal static class DesktopInputProperties
{
    private static readonly Dictionary<string, Cursor> Cursors = new(StringComparer.Ordinal);

    internal static StandardCursorType ParseCursor(string cursor) => cursor switch
    {
        "default" or "arrow" => StandardCursorType.Arrow,
        "cross" => StandardCursorType.Cross,
        "hand" => StandardCursorType.Hand,
        "ibeam" => StandardCursorType.Ibeam,
        "sizeAll" => StandardCursorType.SizeAll,
        "none" => StandardCursorType.None,
        "wait" => StandardCursorType.Wait,
        _ => throw new ArgumentException($"Unsupported cursor '{cursor}'."),
    };

    internal static bool Apply(Control control, GuiVNode node)
    {
        bool changed = false;
        if (node.Cursor == "default") control.ClearValue(InputElement.CursorProperty);
        else
        {
            if (!Cursors.TryGetValue(node.Cursor, out Cursor? cursor))
                Cursors[node.Cursor] = cursor = new Cursor(ParseCursor(node.Cursor));
            if (control.Cursor != cursor) { control.Cursor = cursor; changed = true; }
        }
        if (control.IsHitTestVisible != node.IsHitTestVisible)
        { control.IsHitTestVisible = node.IsHitTestVisible; changed = true; }
        if (node.Focusable is bool focusable) control.Focusable = focusable;
        else control.ClearValue(InputElement.FocusableProperty);
        KeyboardNavigation.SetTabIndex(control, node.TabIndex);
        if (control is Button button)
        {
            button.IsDefault = node.IsDefault;
            button.IsCancel = node.IsCancel;
        }
        if (control is MenuItem menu)
            menu.InputGesture = node.InputGesture is null ? null : KeyGesture.Parse(node.InputGesture);
        return changed;
    }
}

public static partial class DesktopBridge
{
    [ThreadStatic] private static bool _textInputEvent;
    public static bool IsTextInputEvent() => _textInputEvent;

    public static DesktopRef CaptureFocus(DesktopRoot root)
    {
        EnsureOwnerThread();
        var reference = new DesktopRef();
        if (root.Window?.FocusManager?.GetFocusedElement() is Control control)
            reference.Attach(new ControlRef(control));
        return reference;
    }

    public static void ClearFocus(DesktopRoot root)
    {
        EnsureOwnerThread();
        root.Window?.FocusManager?.Focus(null);
    }

    internal static IDisposable EnterKeyContext(object? source, Control target)
    {
        bool previous = _textInputEvent;
        var focus = TopLevel.GetTopLevel(target)?.FocusManager?.GetFocusedElement() as Visual;
        var visual = source as Visual;
        _textInputEvent = IsEditor(visual) || IsEditor(focus);
        return new KeyContext(previous);
    }

    private static bool IsEditor(Visual? visual) => visual is not null &&
        visual.GetSelfAndVisualAncestors().OfType<TextBox>().Any(box => !box.IsReadOnly);

    private sealed class KeyContext(bool previous) : IDisposable
    {
        public void Dispose() => _textInputEvent = previous;
    }

    public static GuiVNode WithTextInput(GuiVNode node, string textWrapping, bool showButtonSpinner) =>
        node with { TextWrapping = textWrapping, ShowButtonSpinner = showButtonSpinner };

    public static GuiVNode WithDesktopInput(GuiVNode node, string cursor, string keyDownRouting, bool hitTestVisible,
        bool hasFocusable, bool focusable, double tabIndex, bool isDefault, bool isCancel,
        string? inputGesture, double offsetX, double offsetY,
        Action? focused, Action? blurred, Action? editStarted, Action? editCompleted,
        Action<string>? scrollChanged, Func<string, bool>? wheel) => node with
    {
        Cursor = cursor, KeyDownRouting = keyDownRouting, IsHitTestVisible = hitTestVisible,
        Focusable = hasFocusable ? focusable : null, TabIndex = ToInteger(tabIndex, nameof(tabIndex)),
        IsDefault = isDefault, IsCancel = isCancel, InputGesture = inputGesture,
        OffsetX = offsetX, OffsetY = offsetY, Focused = focused, Blurred = blurred,
        EditStarted = editStarted, EditCompleted = editCompleted, ScrollChanged = scrollChanged, Wheel = wheel,
    };
}
