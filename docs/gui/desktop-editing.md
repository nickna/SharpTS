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
    return commandKeyHandler([save], reportError)(event);
}}>
    <Button {...commandButton(save)}>Save</Button>
</Window>;
```

Default `onKeyDown` routing remains child-first (`bubble`). Opt into `tunnel` for a command scope that must intercept Ctrl+Enter before a native multiline field inserts a newline. Respect `event.isTextInput`: printable tool shortcuts and application Undo must not steal native editing. Handle rejected command promises in the application workflow.

## Owned dialogs

`useDesktopWindow()` provides the owning window without exposing Avalonia objects. `showDialog<T>(owner, options)` opens a native modal window and returns `Promise<T | null>`. Its content callback receives `complete(value)` and `cancel()`. The dialog inherits the owner's resolved theme unless `options.theme` overrides it. Escape cancels; native tab navigation stays within the modal window; closing restores the owner's previous focus.

Set `isDefault` and `isCancel` on the appropriate buttons. Supply `initialFocus` or focus a `useControlRef` in the content component's effect. Composite numeric controls focus their inner editor. Provide a window title, control names, and inline validation. See `NewDocumentDialog.tsx`.

`DesktopWindow.closed` is a guest Promise in both modes. `captureFocus()` returns a restoration callback. Before taking a document snapshot, `await commitDesktopEdits(owner, commitCurrentEdit)` clears focus, drains deferred blur edits, runs your commit policy, and lets its state update finish. Keep validation and the decision to commit or cancel in the application.

Owned windows open centered on their owner. Return `true` from `onCloseRequested` to keep the window open while asking for confirmation or saving asynchronously; call the window's `close()` after setting your own flag to allow the subsequent request. The guest close guard runs before host shutdown, including when the callback is added after the window opens.

## Fields and history transactions

For native text editing directly over artwork, use `TextBox appearance="plain"`. It removes input chrome and padding, including focused and hovered backgrounds, while preserving text selection, the caret, and native input behavior. Set `background` and `foreground` for the editing surface; no application-wide theme overrides are needed.

The SDK exports opt-in `Field`, `TextField`, `NumberField`, and `Inspector` compositions. `useTextDraft` also works with a custom editor. Draft strings remain editable; validation appears beside the field, Enter/focus loss commits valid input, and Escape restores the model. A controlled native value is restored when its change callback rejects it, including callbacks that do not schedule a render. Unspecified values retain their native behavior.

```tsx
function Properties(): JSX.Element {
    const [name, setName] = useState<string>("Untitled");
    const [opacity, setOpacity] = useState<number>(100);
    return <Inspector title="Properties" actions={<Button onClick={save}>Save</Button>}>
        <StackPanel spacing={12}>
            <TextField id="name" label="Name" value={name} onCommit={setName}
                validate={text => text.trim() ? null : "Enter a name."} />
            <NumberField id="opacity" label="Opacity (%)" value={opacity}
                minimum={0} maximum={100} step={1} onChange={setOpacity} />
        </StackPanel>
    </Inspector>;
}
```

`NumberField` quantizes both its slider and numeric editor to the same step. `precision` controls rounding/display; its default is zero for integer steps and two for fractional steps. For other precision, supply it explicitly. Native `NumericUpDown.formatString` controls display without changing model precision. `Inspector` gives its content one scroll region and keeps actions pinned below it.

`onFocus`/`onBlur` track focus entering/leaving a control and its descendants. Moving between parts of one composite control does not end the scope. `Slider.onEditStarted`/`onEditCompleted` bracket pointer gestures and keyboard adjustments; release, capture loss, and focus loss complete an active gesture. Programmatic value updates do not simulate gestures.

Remember the document at edit start, render live values during editing, and add one undo entry at completion. Ignore semantic no-ops. SharpPaint's `beginOpacity`, `opacity`, and `endOpacity` reducer cases show this transaction; `document.ts` supplies bounded immutable history and saved-snapshot identity. Storage limits, operation names, and selection restoration remain application policy.

## Viewport navigation

`ScrollViewer` exposes `offsetX`, `offsetY`, and `onScrollChanged` with actual offsets, viewport size, and extent size. Notifications are coalesced after layout. Unchanged offset props preserve user scrolling; changing them requests a new native offset. `onWheel` receives local coordinates, deltas, and modifiers; return true to suppress native scrolling. `cursor`, `focusable`, `tabIndex`, and `isHitTestVisible` complete the input surface.

`useViewportNavigation({ width, height, zoom, fit, resetKey, onZoom })` manages centered margins, scroll offsets, continuous Fit, centered numeric zoom, pointer-anchored wheel zoom, and Space/middle-button pan state. Bind its viewport callback and offsets to `ScrollViewer`; pass viewport-local pointer coordinates to its pan handlers and call `cancel` on capture/focus loss. Use `zoomAt(delta, x, y)` for Ctrl+wheel. A changed `resetKey` resets document navigation. The callback's `automatic` flag lets the application distinguish Fit updates from manual zoom.

`viewportPoint`, `centeredZoomOffset`, `fitZoom`, and `anchoredZoomOffset` are available separately. Keep paint gestures, checkerboards, selection, and document history in the sample. `EditorCanvas.tsx` shows the complete wiring, including pan initiation in the surrounding workspace. Decorative overlays disable hit testing. `onPointerEnter` and `onPointerLeave` support hover feedback without estimating whether a point is outside the control.

## Asynchronous work and files

`useSerialTask()` exposes reactive `busy` state and `run(work)`; it reuses `createSerialTask`'s reentrancy guard. An overlapping run returns false, and a completed or failed run releases the guard. Errors propagate to the caller. Use `busy` to disable conflicting commands and show file-operation feedback. SharpPaint composes this with Save / Don't Save / Cancel (`buttons: "saveDiscardCancel"`), a reentrant close guard, recent files, and recovery. Failed saves cancel replacement operations.

`writeTextFileAtomic(path, text)` writes UTF-8 asynchronously, flushes a sibling temporary file, then replaces an existing destination or moves into an unused path. Parent directories must exist. `readTextFile(path, maximumBytes?)` rejects oversized input before reading. Filesystem errors reject the Promise. Atomic replacement protects the destination; it is not a backup or a hardware durability guarantee.

`startDrawingImage`/`startDrawingFill` return `DrawingTask<T>` with `result`, monotonic `progress` (0–1), and `cancel()`. Cancellation rejects the result and is cooperative at operation/scanline boundaries; native filter/codec calls finish before cancellation is observed. Never publish stale or canceled results.

`useLatestTask()` supplies a generation/liveness guard, cancels previous work on replacement, and cancels on unmount. Combine it with document revision and layer identity. `graphics-session.ts` demonstrates debounced preview, progress, cancellation, and applying only a preview matching current parameters. Busy and error messages remain application policy.

## Application styling

Pass shared `styles` to `createDesktopApplication`. `DESKTOP_STYLES` is a small opt-in set with `primary` and `subtle` button classes. `useTheme()` follows the owning window's resolved light/dark theme; `useDesktopPalette()` supplies semantic colors for custom surfaces. Native control themes continue to provide focus and interaction feedback.

Selectors accept control kinds, classes, and the reviewed `states` values: `pointerover`, `pressed`, `checked`, `disabled`, `focus-visible`, and `focus-within`. State selectors update natively. Resource dictionaries remain startup configuration; resolved palette hooks provide dynamic colors in compositions.

`PathIcon` takes filled SVG path geometry and inherits its foreground. `IconButton` combines a path with a tooltip and accessible name. Paint-specific icon paths remain in SharpPaint. `ColorView` embeds a native spectrum/component editor; `ColorPicker` opens it in a dropdown. Both accept `color` and emit canonical `#AARRGGBB` from `onColorChanged`. Their exact input uses leading alpha, matching drawing colors. `normalizeHexColor` validates RGB/ARGB and collapses opaque ARGB to RGB. Recent colors and foreground/background rules remain application state.

## Honest acceptance tests

`@sharpts/gui/testing` supports native keys/modifiers, committed text input, focus, owned dialogs, pointer hover/drag/cancel, wheel input, and scaling. `click` invokes a semantic native click; it does not move the mouse or focus the button. Direct setters are state fixtures; typing tests should use focus and key/text input. `afterRender` waits for tracked work without starving timers. Detached background jobs need an explicit completion condition.

Keys inside function compositions can be found by an exact native key or unique terminal key; ambiguous matches fail. `isInViewport(key)` requires the entire arranged control to be inside its window and clipping ancestors. It does not detect occlusion by overlapping siblings. Inspector bounds are window-relative; visibility and enabled state include ancestors. `driver.captureSnapshot(path)` and `driver.assertSnapshot(baseline, update?, maxDifferentPixels?)` target that driver's window, including owned dialogs. Comparisons are exact by default; a small explicit pixel budget can accommodate native edge rasterization. Image dimensions must always match.

Test document content and history as well as labels. SharpPaint's presentation checks cover both themes, compact inspectors, 150% scaling, and dialog reachability; its reviewed baselines have an explicit font/platform contract. Native inspection complements headless assertions; neither establishes full assistive-technology, IME, pen, or multi-monitor compatibility.
