import { createDesktopApplication, DesktopWindow } from "@sharpts/gui";
import { createDesktopTestDriver } from "@sharpts/gui/testing";
import { join } from "path";
import { SharpPaintShowcase } from "./SharpPaintApp";
import { createDocument } from "./document";
import { PAINT_STYLES } from "./controls";

// Run in a fresh host. This measures synthetic native gestures through a completed
// headless frame, including PNG capture; it is not physical pen-to-display latency.
const app = createDesktopApplication({ styles: PAINT_STYLES });
let window: DesktopWindow;
window = app.createWindow(
    <SharpPaintShowcase initialDocument={createDocument(2048, 2048)} requestClose={() => window.close()} />,
    { main: true }
);
const driver = createDesktopTestDriver(window);
const rendered = (): Promise<void> => new Promise<void>((resolve) => driver.afterRender(() => resolve()));
async function waitForStatus(expected: string): Promise<void> {
    const deadline = Date.now() + 30000;
    while (driver.getText("status") !== expected) {
        if (Date.now() > deadline) throw new Error("Timed out waiting for " + expected);
        await new Promise<void>((resolve) => setTimeout(() => resolve(), 10));
    }
}
function report(scenario: string, samples: number[], warmup: number): void {
    samples.sort((a, b) => a - b);
    console.log(
        JSON.stringify({
            scenario,
            warmup,
            measured: samples.length,
            p50Milliseconds: samples[Math.ceil(samples.length * 0.5) - 1],
            p95Milliseconds: samples[Math.ceil(samples.length * 0.95) - 1],
            samplesMilliseconds: samples
        })
    );
}
async function run(): Promise<void> {
    await rendered();
    await new Promise<void>((resolve) => setTimeout(() => resolve(), 100));
    const samples: number[] = [];
    const points: { x: number; y: number }[] = [];
    for (let i = 0; i < 100; i++) points.push({ x: 20 + i * 2, y: 120 + Math.sin(i / 9) * 65 });
    for (let stroke = 0; stroke < 12; stroke++) {
        const start = Date.now();
        driver.dragPointer("paint-surface", points);
        await rendered();
        driver.captureSnapshot(join(process.cwd(), "SharpPaint.Performance.frame.png"));
        if (stroke >= 2) samples.push(Date.now() - start);
    }
    if (driver.getText("command-count") !== "13 commands · 1 layers")
        throw new Error("The measured strokes did not reach the document.");
    report("2048px document, 100 native points per stroke + settled frame and PNG capture", samples, 2);
    const previews: number[] = [];
    for (let iteration = 0; iteration < 6; iteration++) {
        const start = Date.now();
        driver.clickMenuItem("effect-blur");
        await rendered();
        for (const radius of [2, 12, 5, 8]) driver.setNumericValue("effect-first-number", radius);
        await rendered();
        await waitForStatus("Effect preview ready");
        driver.captureSnapshot(join(process.cwd(), "SharpPaint.Performance.preview.png"));
        if (iteration > 0) previews.push(Date.now() - start);
        driver.click("cancel-effect");
        await rendered();
    }
    report(
        "Open blur inspector, replace four preview parameters, settled frame and PNG capture",
        previews,
        1
    );
    const exports: number[] = [];
    for (let iteration = 0; iteration < 4; iteration++) {
        const name = "SharpPaint.Performance.export-" + iteration + ".png";
        driver.queueSaveFileDialogResult(join(process.cwd(), name));
        const start = Date.now();
        driver.clickMenuItem("menu-export");
        await rendered();
        await waitForStatus("Exported " + name);
        if (iteration > 0) exports.push(Date.now() - start);
    }
    report("2048px PNG export through document command with scripted file dialog", exports, 1);
    console.log("SharpPaint performance checks passed.");
    app.dispose();
}
setTimeout(() => {
    void run().catch((error) => {
        console.error(String(error));
        app.shutdown(1);
    });
}, 30);
