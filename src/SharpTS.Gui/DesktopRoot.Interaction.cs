using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SharpTS.Gui;

public sealed partial class DesktopRoot
{
    private void SynchronizeExtendedInteraction(MountedNode mounted, GuiVNode? next = null)
    {
        GuiVNode node = next ?? mounted.VNode;
        mounted.InteractionNode = node;
        int mask = (node.Focused is not null || node.Blurred is not null ? 1 : 0) |
            (node.Wheel is not null ? 2 : 0) |
            (node.ScrollChanged is not null && mounted.Control is ScrollViewer ? 4 : 0) |
            (node.EditStarted is not null || node.EditCompleted is not null ? 8 : 0);
        if (mask == mounted.InteractionMask) return;
        foreach (Action unsubscribe in mounted.InteractionUnsubscribe) unsubscribe();
        mounted.InteractionUnsubscribe.Clear();
        mounted.InteractionMask = mask;

        void Notify(Action? callback)
        {
            if (!mounted.SuppressEvents && callback is not null) PostGuestNotification(mounted, callback);
        }
        void CompleteEdit()
        {
            if (!mounted.EditActive) return;
            mounted.EditActive = false;
            Notify(mounted.InteractionNode.EditCompleted);
        }
        void BeginEdit()
        {
            if (mounted.EditActive) return;
            mounted.EditActive = true;
            Notify(mounted.InteractionNode.EditStarted);
        }
        if ((mask & 9) != 0)
        {
            EventHandler<AvaloniaPropertyChangedEventArgs> focusWithin = (_, e) =>
            {
                if (e.Property != InputElement.IsKeyboardFocusWithinProperty) return;
                if (mounted.Control.IsKeyboardFocusWithin) Notify(mounted.InteractionNode.Focused);
                else
                {
                    CompleteEdit();
                    Notify(mounted.InteractionNode.Blurred);
                }
            };
            mounted.Control.PropertyChanged += focusWithin;
            mounted.InteractionUnsubscribe.Add(() => mounted.Control.PropertyChanged -= focusWithin);
        }
        if ((mask & 2) != 0)
        {
            EventHandler<PointerWheelEventArgs> wheel = (_, e) =>
            {
                Func<string, bool>? callback = mounted.InteractionNode.Wheel;
                if (callback is null) return;
                Point point = e.GetPosition(mounted.Control);
                KeyModifiers modifiers = e.KeyModifiers;
                string json = JsonSerializer.Serialize(new WheelInput(point.X, point.Y, e.Delta.X, e.Delta.Y,
                    modifiers.HasFlag(KeyModifiers.Control), modifiers.HasFlag(KeyModifiers.Alt),
                    modifiers.HasFlag(KeyModifiers.Shift), modifiers.HasFlag(KeyModifiers.Meta)), InputJsonContext.Default.WheelInput);
                bool handled = false;
                _invokeGuestCallback(() => handled = callback(json));
                if (handled) e.Handled = true;
            };
            mounted.Control.AddHandler(InputElement.PointerWheelChangedEvent, wheel, RoutingStrategies.Tunnel);
            mounted.InteractionUnsubscribe.Add(() => mounted.Control.RemoveHandler(InputElement.PointerWheelChangedEvent, wheel));
        }
        if ((mask & 4) != 0 && mounted.Control is ScrollViewer viewer)
        {
            void QueueScroll()
            {
                if (mounted.ScrollNotificationPending) return;
                mounted.ScrollNotificationPending = true;
                Dispatcher.UIThread.Post(() =>
                {
                    mounted.ScrollNotificationPending = false;
                    if (mounted.Released || _disposed) return;
                    Action<string>? callback = mounted.InteractionNode.ScrollChanged;
                    if (callback is null) return;
                    string json = JsonSerializer.Serialize(new ScrollInput(viewer.Offset.X, viewer.Offset.Y,
                        viewer.Viewport.Width, viewer.Viewport.Height, viewer.Extent.Width, viewer.Extent.Height),
                        InputJsonContext.Default.ScrollInput);
                    PostGuestNotification(mounted, () => callback(json));
                }, DispatcherPriority.Background);
            }
            EventHandler<ScrollChangedEventArgs> changed = (_, _) => QueueScroll();
            viewer.ScrollChanged += changed;
            mounted.InteractionUnsubscribe.Add(() => viewer.ScrollChanged -= changed);
            QueueScroll();
        }
        if ((mask & 8) != 0)
        {
            EventHandler<PointerPressedEventArgs> press = (_, _) => BeginEdit();
            EventHandler<PointerReleasedEventArgs> release = (_, _) => CompleteEdit();
            EventHandler<PointerCaptureLostEventArgs> cancel = (_, _) => CompleteEdit();
            EventHandler<KeyEventArgs> down = (_, e) =>
            {
                if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) BeginEdit();
            };
            EventHandler<KeyEventArgs> up = (_, _) => CompleteEdit();
            mounted.Control.AddHandler(InputElement.PointerPressedEvent, press, RoutingStrategies.Tunnel, true);
            mounted.Control.AddHandler(InputElement.PointerReleasedEvent, release, RoutingStrategies.Bubble, true);
            mounted.Control.PointerCaptureLost += cancel;
            mounted.Control.AddHandler(InputElement.KeyDownEvent, down, RoutingStrategies.Tunnel, true);
            mounted.Control.AddHandler(InputElement.KeyUpEvent, up, RoutingStrategies.Bubble, true);
            mounted.InteractionUnsubscribe.Add(() =>
            {
                mounted.Control.RemoveHandler(InputElement.PointerPressedEvent, press);
                mounted.Control.RemoveHandler(InputElement.PointerReleasedEvent, release);
                mounted.Control.PointerCaptureLost -= cancel;
                mounted.Control.RemoveHandler(InputElement.KeyDownEvent, down);
                mounted.Control.RemoveHandler(InputElement.KeyUpEvent, up);
            });
        }
    }

    private sealed record WheelInput(double X, double Y, double DeltaX, double DeltaY, bool Ctrl, bool Alt, bool Shift, bool Meta);
    private sealed record ScrollInput(double OffsetX, double OffsetY, double ViewportWidth, double ViewportHeight, double ExtentWidth, double ExtentHeight);
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(WheelInput))]
    [JsonSerializable(typeof(ScrollInput))]
    private sealed partial class InputJsonContext : JsonSerializerContext;
}
