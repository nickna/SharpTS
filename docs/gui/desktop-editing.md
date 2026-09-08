# Desktop editing recipes

SharpPaint demonstrates these compositions. The SDK provides lifecycle and native interaction primitives; applications own document structure, history policy, and validation.

## One command definition

`DesktopCommand` holds a label, shortcut, enabled/checked state, and callback. `commandButton` and `commandMenu` derive native props, including actual menu input gestures. `findCommand` compares all modifiers and skips editable inputs unless `allowInTextInput` is true.

```tsx
const save: DesktopCommand = {
    id: "save", label: "Save", shortcut: "Ctrl+S",
    enabled: !busy, allowInTextInput: true, execute: saveDocument
};
return <Window keyDownRouting="tunnel" onKeyDown={event => {
    const command = findCommand([save], event);
    if (!command) return false;
    void command.execute();
    return true;
}}>
    <Button {...commandButton(save)}>Save</Button>
</Window>;
```

Default `onKeyDown` routing remains child-first (`bubble`). Opt into `tunnel` for a command scope that must intercept Ctrl+Enter before a native multiline field inserts a newline. Respect `event.isTextInput`: printable tool shortcuts and application Undo must not steal native editing. Handle rejected command promises in the application workflow.

## Owned dialogs

`useDesktopWindow()` provides the owning window without exposing Avalonia objects. `showDialog<T>(owner, options)` opens a native modal window and returns `Promise<T | null>`. Its content callback receives `complete(value)` and `cancel()`. Escape cancels; native tab navigation stays within the modal window; closing restores the owner's previous focus.

Set `isDefault` and `isCancel` on the appropriate buttons. Supply `initialFocus` or focus a `useControlRef` in the content component's effect. Composite numeric controls focus their inner editor. Provide a window title, control names, and inline validation. See `NewDocumentDialog.tsx`.

`DesktopWindow.closed` is a guest Promise in both modes. `captureFocus()` returns a restoration callback; `clearFocus()` lets fields finish editing. Focus/blur notifications are deferred guest events, so yield a turn before taking a snapshot after clearing focus.

Owned windows open centered on their owner. Return `true` from `onCloseRequested` to keep the window open while asking for confirmation or saving asynchronously; call the window's `close()` after setting your own flag to allow the subsequent request. The guest close guard runs before host shutdown, including when the callback is added after the window opens.

## Fields and history transactions

For native text editing directly over artwork, use `TextBox appearance="plain"`. It removes input chrome and padding, including focused and hovered backgrounds, while preserving text selection, the caret, and native input behavior. Set `background` and `foreground` for the editing surface; no application-wide theme overrides are needed.

Keep draft strings separate from committed model values. Preserve invalid intermediate input, validate beside the field, commit on Enter/focus loss, and revert on Escape. `controls.tsx` demonstrates a small `TextField` composition.

`onFocus`/`onBlur` track focus entering/leaving a control and its descendants. Moving between parts of one composite control does not end the scope. `Slider.onEditStarted`/`onEditCompleted` bracket pointer gestures and keyboard adjustments; release, capture loss, and focus loss complete an active gesture. Programmatic value updates do not simulate gestures.

Remember the document at edit start, render live values during editing, and add one undo entry at completion. Ignore semantic no-ops. SharpPaint's `beginOpacity`, `opacity`, and `endOpacity` reducer cases show this transaction; `document.ts` supplies bounded immutable history and saved-snapshot identity. Storage limits, operation names, and selection restoration remain application policy.

## Viewport navigation

`ScrollViewer` exposes `offsetX`, `offsetY`, and `onScrollChanged` with actual offsets, viewport size, and extent size. Notifications are coalesced after layout. Unchanged offset props preserve user scrolling; changing them requests a new native offset. `onWheel` receives local coordinates, deltas, and modifiers; return true to suppress native scrolling. `cursor`, `focusable`, `tabIndex`, and `isHitTestVisible` complete the input surface.

`fitZoom` and `anchoredZoomOffset` supply basic arithmetic. Include old/new margins when zooming a centered document, as `EditorCanvas.tsx` does. Keep screen and document coordinates separate. Use an input Border over graphics, disable hit testing on decorative overlays, and retain pointer capture until drag completion.

## Asynchronous work and files

`createSerialTask().run(work)` returns false for an overlapping workflow, releases its guard on completion/failure, and propagates errors. Keep one instance in a ref for Open/Save/Close. SharpPaint composes it with Save / Don't Save / Cancel (`buttons: "saveDiscardCancel"`), a reentrant close guard, recent files, and recovery. Failed saves cancel replacement operations.

`writeTextFileAtomic(path, text)` writes UTF-8 asynchronously, flushes a sibling temporary file, then replaces an existing destination or moves into an unused path. Parent directories must exist. `readTextFile(path, maximumBytes?)` rejects oversized input before reading. Filesystem errors reject the Promise. Atomic replacement protects the destination; it is not a backup or a hardware durability guarantee.

`startDrawingImage`/`startDrawingFill` return `DrawingTask<T>` with `result`, monotonic `progress` (0–1), and `cancel()`. Cancellation rejects the result and is cooperative at operation/scanline boundaries; native filter/codec calls finish before cancellation is observed. Never publish stale or canceled results.

`useLatestTask()` supplies a generation/liveness guard, cancels previous work on replacement, and cancels on unmount. Combine it with document revision and layer identity. `graphics-session.ts` demonstrates debounced preview, progress, cancellation, and applying only a preview matching current parameters. Busy and error messages remain application policy.

## Application styling

Pass shared `styles` to `createDesktopApplication`. Select controls by kind and optional `classes`; use native theme colors for ordinary controls and supply a complete light/dark palette for custom surfaces. Templated controls accept `borderThickness`, `borderBrush`, and uniform `cornerRadius` style setters. SharpPaint's `PAINT_STYLES` uses these to keep buttons consistent; its inline text editor uses `appearance="plain"` so the content origin matches retained text. Keep decorative drawing controls out of hit testing and give their containing buttons accessible names and tooltips.

## Honest acceptance tests

`@sharpts/gui/testing` supports native keys/modifiers, committed text input, focus, owned dialogs, pointer drag/cancel, wheel input, and scaling. `click` invokes a semantic native click; it does not move the mouse or focus the button. Direct setters are state fixtures; typing tests should use focus and key/text input. `afterRender` waits for tracked work without starving timers. Detached background jobs need an explicit completion condition.

Keys inside function compositions can be found by an exact native key or unique terminal key; ambiguous matches fail. Test document content and history as well as labels. Check Tab/Escape in dialogs, shortcut letters in fields, canceled/failed saves, scaling changes, and actual artwork. Native inspection complements headless assertions; neither establishes full assistive-technology, IME, pen, or multi-monitor compatibility.
