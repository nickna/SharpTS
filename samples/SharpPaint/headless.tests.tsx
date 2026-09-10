import "./editor-state.tests";
import { createDesktopApplication, DesktopWindow } from "@sharpts/gui";
import { createDesktopTestDriver, DesktopTestDriver } from "@sharpts/gui/testing";
import { captureHeadlessSnapshot } from "@sharpts/gui/devtools";
import { existsSync, unlinkSync, writeFileSync, readFileSync } from "fs";
import { join } from "path";
import { SharpPaintShowcase } from "./SharpPaintApp";
import { runPresentationChecks } from "./presentation.tests";
import { PAINT_STYLES } from "./controls";
import { createDocument, serializeProject, parseProject } from "./document";

function expect(name: string, condition: boolean): void {
    if (!condition) throw new Error("SharpPaint assertion failed: " + name);
}
const app = createDesktopApplication({ styles: PAINT_STYLES });
let window: DesktopWindow;
window = app.createWindow(
    <SharpPaintShowcase initialDocument={createDocument(320, 240)} requestClose={() => window.close()} />,
    { main: true }
);
const driver: DesktopTestDriver = createDesktopTestDriver(window);
const openPath = join(process.cwd(), "SharpPaint.Headless.Open.sharpaint");
const savePath = join(process.cwd(), "SharpPaint.Headless.Save.sharpaint");
function cleanup(): void {
    for (const file of [openPath, savePath]) if (existsSync(file)) unlinkSync(file);
}
process.on("exit", cleanup);
writeFileSync(openPath, serializeProject(createDocument(320, 240)), "utf8");
function delay(ms: number = 25): Promise<void> {
    return new Promise<void>((resolve) => setTimeout(() => resolve(), ms));
}
function rendered(test: DesktopTestDriver = driver): Promise<void> {
    return new Promise<void>((resolve) => test.afterRender(() => resolve()));
}
async function status(value: string): Promise<void> {
    const deadline = Date.now() + 15000;
    while (driver.getText("status") !== value) {
        if (Date.now() > deadline) {
            console.log(driver.getText("status"));
            throw new Error("Timed out: " + value);
        }
        await delay();
    }
}
function count(): string {
    return driver.getText("command-count");
}
function snapshot(name: string): void {
    const directory = process.env.SHARPAINT_SNAPSHOTS;
    if (directory) captureHeadlessSnapshot(join(directory, name + ".png"));
}
async function draw(): Promise<void> {
    driver.dragPointer("paint-surface", [
        { x: 12, y: 14 },
        { x: 40, y: 42 },
        { x: 72, y: 58 }
    ]);
    await rendered();
}
async function run(): Promise<void> {
    console.log("Starting workflows");
    await rendered();
    await delay(100);
    console.log("Initial render ready");
    expect("initial fixture dimensions", driver.getProperty("paint-surface", "width") === "320");
    expect("initial commands", count() === "1 commands · 1 layers");
    driver.click("add-layer");
    await rendered();
    driver.click("undo");
    await rendered();
    await draw();
    expect("Add layer / Undo / Draw retains valid selection", count() === "2 commands · 1 layers");
    driver.click("undo");
    await rendered();
    expect("undo stroke", count() === "1 commands · 1 layers");
    driver.click("redo");
    await rendered();
    expect("redo stroke", count() === "2 commands · 1 layers");

    console.log("Drawing and history passed");
    driver.focus("layer-name");
    driver.pressKey("Ctrl+A");
    driver.typeText("R B text with spaces");
    await rendered();
    expect(
        "typing preserves shortcut letters and spaces",
        driver.getText("layer-name") === "R B text with spaces"
    );
    expect("typing does not change tool", driver.getProperty("brush-tool", "isChecked") === "True");
    driver.pressKey("Enter");
    await rendered();
    driver.click("undo");
    await rendered();
    expect("one rename undo", driver.getText("layer-name") === "Background");
    driver.focus("layer-name");
    driver.pressKey("Ctrl+A");
    driver.typeText("Canceled name");
    await rendered();
    driver.pressKey("Escape");
    await rendered();
    expect("escape cancels rename", driver.getText("layer-name") === "Background");
    driver.focus("paint-surface");

    driver.pressPointer("layer-opacity", { x: 20, y: 12 });
    driver.setSliderValue("layer-opacity", 20);
    driver.setSliderValue("layer-opacity", 45);
    driver.setSliderValue("layer-opacity", 70);
    driver.releasePointer("layer-opacity", { x: 90, y: 12 });
    await rendered();
    driver.click("undo");
    await rendered();
    expect(
        "opacity gesture has one undo entry",
        driver.getProperty("layer-opacity-number", "value") === "100"
    );

    console.log("Fields passed");
    driver.focus("paint-surface");
    driver.click("new");
    await delay(100);
    const dialog = driver.ownedWindow("New document");
    expect("new dimensions use exact input", dialog.getProperty("new-width", "value") === "1024");
    expect("dialog starts in the width field", dialog.isFocused("new-width"));
    dialog.pressKey("Tab");
    expect("tab stays in the dialog", dialog.isFocused("new-height"));
    dialog.pressKey("Escape");
    await rendered();
    expect("cancel new preserves document", count() === "2 commands · 1 layers");
    expect("dialog restores canvas focus", driver.isFocused("paint-surface"));

    console.log("Dialog passed");
    driver.click("swatch-#ef4444");
    await rendered();
    driver.click("fill-tool");
    await rendered();
    driver.pressPointer("paint-surface", { x: 150, y: 150 });
    driver.releasePointer("paint-surface", { x: 150, y: 150 });
    await status("Filled selected region");
    driver.click("picker-tool");
    await rendered();
    driver.pressPointer("paint-surface", { x: 150, y: 150 });
    driver.releasePointer("paint-surface", { x: 150, y: 150 });
    await status("Color #EF4444");
    expect("picker color", driver.getText("custom-color") === "#EF4444");

    driver.click("text-tool");
    console.log("Fill and picker passed");
    await rendered();
    driver.click("swatch-#111827");
    await rendered();
    driver.dragPointer("paint-surface", [
        { x: 30, y: 30 },
        { x: 260, y: 120 }
    ]);
    await rendered();
    driver.typeText("SharpTS مرحبا 日本語");
    await rendered();
    driver.pressKey("Ctrl+Enter");
    await rendered();
    expect("retained text committed", count() === "2 commands · 1 layers");
    snapshot("text");
    driver.pressPointer("paint-surface", { x: 50, y: 50 });
    driver.releasePointer("paint-surface", { x: 50, y: 50 });
    await rendered();
    expect("text reopens", driver.getText("text-editor") === "SharpTS مرحبا 日本語");
    driver.pressKey("Escape");
    await rendered();
    expect("text cancel retains original", count() === "2 commands · 1 layers");

    driver.clickMenuItem("effect-invert");
    console.log("Text passed");
    await status("Invert applied");
    driver.clickMenuItem("effect-blur");
    await status("Effect preview ready");
    snapshot("effect");
    driver.setNumericValue("effect-first-number", 8);
    await status("Effect preview ready");
    driver.click("cancel-effect");
    await rendered();
    expect("cancel effect preserves history", count() === "1 commands · 1 layers");

    driver.queueMessageDialogResult("cancel");
    console.log("Effects passed");
    driver.click("open");
    await rendered();
    expect("cancel replacement preserves document", count() === "1 commands · 1 layers");
    driver.queueMessageDialogResult("discard");
    driver.queueOpenFileDialogResult([openPath]);
    driver.click("open");
    await rendered();
    expect("open project", driver.getText("status") === "Opened SharpPaint.Headless.Open.sharpaint");
    driver.click("brush-tool");
    await rendered();
    await draw();
    driver.queueSaveFileDialogResult(savePath);
    driver.clickMenuItem("menu-save-as");
    await rendered();
    expect(
        "atomic save writes snapshot",
        parseProject(readFileSync(savePath, "utf8") as string).layers[0].commands.length === 2
    );
    driver.click("undo");
    await rendered();
    expect("save keeps undo", count() === "1 commands · 1 layers");
    driver.queueSaveFileDialogResult(join(process.cwd(), "missing-directory", "failure.sharpaint"));
    driver.clickMenuItem("menu-save-as");
    await rendered();
    expect("failed save is visible", driver.getText("document-error").indexOf("Could not save") >= 0);
    expect("failed save preserves document", count() === "1 commands · 1 layers");

    driver.setWindowClientSize(840, 560);
    await delay(100);
    await rendered();
    expect("narrow layout", driver.getText("layout-mode") === "narrow");
    expect("zoom remains available", driver.getProperty("zoom-number", "isVisible") === "True");
    driver.click("layers-toggle");
    await rendered();
    expect("layer panel can be reopened", driver.getProperty("add-layer", "isVisible") === "True");
    snapshot("narrow");
    driver.setRenderScaling(1.5);
    await delay(100);
    driver.click("fit");
    await rendered();
    expect("fit has nonzero canvas", Number(driver.getProperty("paint-surface", "width")) > 0);
    driver.setNumericValue("zoom-number", 200);
    await rendered();
    expect("exact zoom", driver.getProperty("paint-surface", "width") === "640");
    driver.setWindowClientSize(1120, 700);
    await delay(100);
    driver.click("fit");
    await rendered();
    driver.click("add-layer");
    await rendered();
    await draw();
    driver.click("merge-layer");
    await status("Merged layers");
    expect("merge down combines layers", count() === "1 commands · 1 layers");
    console.log("Merge passed");
    driver.queueMessageDialogResult("save");
    driver.queueOpenFileDialogResult([openPath]);
    driver.click("open");
    await rendered();
    expect(
        "Save in replacement prompt writes the current document",
        parseProject(readFileSync(savePath, "utf8") as string).layers[0].commands.length === 1
    );
    expect(
        "Save in replacement prompt continues opening",
        driver.getText("status") === "Opened SharpPaint.Headless.Open.sharpaint"
    );
    console.log("Save replacement passed");
    driver.queueOpenFileDialogResult([savePath]);
    driver.clickMenuItem("recover");
    await rendered();
    expect(
        "recovery opens an unsaved document",
        driver.getText("status") === "Recovered document · Save As to keep a new copy"
    );
    console.log("Recovery passed");
    driver.queueMessageDialogResult("save");
    driver.queueSaveFileDialogResult(null);
    driver.click("open");
    await rendered();
    expect(
        "canceling Save As cancels document replacement",
        driver.getText("status") === "Recovered document · Save As to keep a new copy"
    );
    await runPresentationChecks(app);
    console.log("SharpPaint headless workflows passed.");
}
setTimeout(() => {
    void run().then(
        () => {
            app.dispose();
            cleanup();
        },
        (error) => {
            console.error(String(error));
            try {
                console.error(driver.getText("document-error"));
                console.error(driver.getText("document-error-details"));
            } catch (_) {}
            try {
                console.error(driver.getText("fatal-error"));
            } catch (_) {}
            cleanup();
            app.shutdown(1);
        }
    );
}, 30);
