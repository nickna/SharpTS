# GUI inspector and visual regression

Import developer tooling from its dedicated package subpath:

```tsx
import {
    assertHeadlessSnapshot,
    captureHeadlessSnapshot,
    inspectDesktopTree,
} from "@sharpts/gui/devtools";
```

`inspectDesktopTree()` returns every mounted window and its logical kind/key, concrete Avalonia
type, TSX source location, arranged bounds, visibility/enabled state, classes, selected props, and
children. It is read-only and works in normal and Headless applications.

For a visual baseline, mount the window in a Headless entry point and call:

```tsx
assertHeadlessSnapshot("Snapshots/main.png");
```

Pass `true` as the second argument only when intentionally creating or updating the committed PNG.
Comparison is byte-exact. A mismatch preserves `Snapshots/main.actual.png` and reports the expected
and actual SHA-256 values. `captureHeadlessSnapshot(path)` writes an unconditionally captured PNG
and returns its lowercase SHA-256. Pixel capture is rejected outside `--headless` mode.

The host uses Avalonia Headless with the Skia drawing backend so snapshots contain actual rendered
pixels rather than the mock drawing command stream. Keep baseline generation and CI comparison on
the same supported OS/font environment; structural assertions should use `inspectDesktopTree()`
when pixel identity is not the contract under test.
