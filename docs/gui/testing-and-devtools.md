# GUI testing and developer tools

SharpTS GUI separates supported Headless interaction from diagnostic inspection. Import the
window-scoped test driver from `@sharpts/gui/testing` and read-only inspection or snapshot tools
from `@sharpts/gui/devtools`. Both subpaths are included in the SDK package.

## Headless interaction tests

The SDK template includes `headless.tests.tsx`. Run a Headless entry point in either execution
mode:

```powershell
dotnet run -p:SharpTSEntryPoint=headless.tests.tsx -- --mode interpreted --headless
dotnet run -p:SharpTSEntryPoint=headless.tests.tsx -- --mode compiled --headless
```

Create the driver from the `DesktopWindow` returned by `createWindow`:

```tsx
import { Button, StackPanel, TextBlock, Window, createDesktopApplication, useState } from "@sharpts/gui";
import { createDesktopTestDriver } from "@sharpts/gui/testing";

function App(): JSX.Element {
    const [count, setCount] = useState(0);
    return (
        <Window title="Counter">
            <StackPanel>
                <TextBlock key="count">Count: {count}</TextBlock>
                <Button key="increment" onClick={() => setCount(value => value + 1)}>
                    Increment
                </Button>
            </StackPanel>
        </Window>
    );
}

const application = createDesktopApplication();
const window = application.createWindow(<App />, { main: true });
const driver = createDesktopTestDriver(window);

driver.click("increment");
driver.afterRender(() => {
    if (driver.getText("count") !== "Count: 1") throw new Error("Count did not update");
    application.dispose();
});
```

The driver is available only under `--headless`. It supports keyed clicks, window keyboard input,
text and allow-listed property queries, form value changes, and text drag/drop. A driver is scoped
to one window and cannot resolve keys from another. `afterRender` schedules its callback after the
interaction's pending render commits.

Scheduler control, native-failure injection, renderer identity, subscription counters, and trace
staging are repository conformance facilities rather than supported application test APIs.

## Structural inspection

`inspectDesktopTree()` returns every mounted window and its logical kind and key, concrete Avalonia
type, TSX source location, arranged bounds, visibility and enabled state, classes, selected props,
and children:

```tsx
import { inspectDesktopTree } from "@sharpts/gui/devtools";

const snapshot = inspectDesktopTree();
if (snapshot.windows.length !== 1) throw new Error("Expected one window");
```

Inspection is read-only and works in normal and Headless applications. Prefer structural
assertions when native pixel identity is not the contract under test.

## Visual regression snapshots

Mount the window in a Headless entry point and compare it with a committed PNG:

```tsx
import { assertHeadlessSnapshot } from "@sharpts/gui/devtools";

assertHeadlessSnapshot("Snapshots/main.png");
```

Pass `true` as the second argument only when intentionally creating or updating the baseline.
Comparison is byte-exact. A mismatch preserves `Snapshots/main.actual.png` and reports the expected
and actual SHA-256 values. `captureHeadlessSnapshot(path)` always writes a PNG and returns its
lowercase SHA-256. Pixel capture is rejected outside `--headless` mode.

The host uses Avalonia Headless with the Skia drawing backend, so snapshots contain rendered pixels
rather than a mock drawing command stream. Generate and compare baselines on the same supported OS,
font set, runtime, and rendering environment. Review both the PNG change and the structural tree
when accepting a visual update.
