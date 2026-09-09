import { DesktopApplication, DesktopWindow } from "@sharpts/gui";
import { createDesktopTestDriver, DesktopTestDriver } from "@sharpts/gui/testing";
import { join } from "path";
import { SharpPaintShowcase } from "./SharpPaintApp";
import { createDemoDocument } from "./demo";

function expect(name: string, condition: boolean): void {
    if (!condition) throw new Error("Presentation: " + name);
}
async function rendered(driver: DesktopTestDriver): Promise<void> {
    await new Promise<void>((resolve) => driver.afterRender(() => resolve()));
    await new Promise<void>((resolve) => setTimeout(() => resolve(), 100));
    await new Promise<void>((resolve) => driver.afterRender(() => resolve()));
}
async function settled(driver: DesktopTestDriver): Promise<void> {
    // Native Fluent focus/enable transitions continue after the guest render finishes.
    await new Promise<void>((resolve) => setTimeout(() => resolve(), 500));
    await rendered(driver);
}
export async function runPresentationChecks(app: DesktopApplication): Promise<void> {
    for (const theme of ["light", "dark"] as const) {
        let window: DesktopWindow;
        window = app.createWindow(
            <SharpPaintShowcase
                theme={theme}
                initialDocument={createDemoDocument()}
                requestClose={() => window.close()}
            />
        );
        const driver = createDesktopTestDriver(window);
        await rendered(driver);
        const snapshot = (name: string): void => {
            console.log("Presentation: " + theme + " / " + name);
            if (process.env.SHARPAINT_SNAPSHOTS)
                driver.captureSnapshot(join(process.env.SHARPAINT_SNAPSHOTS, theme + "-" + name + ".png"));
            if (process.env.SHARPAINT_VISUAL_BASELINES)
                driver.assertSnapshot(
                    join(process.env.SHARPAINT_VISUAL_BASELINES, theme + "-" + name + ".png"),
                    false,
                    128
                );
        };
        expect("layer actions visible", driver.isInViewport("add-layer"));
        expect("opacity visible", driver.isInViewport("layer-opacity-number"));
        driver.click("brush-tool");
        await rendered(driver);
        expect("reselecting brush remains checked", driver.getProperty("brush-tool", "isChecked") === "True");
        driver.setNumericValue("brush-size-number", 1);
        await rendered(driver);
        driver.setNumericValue("brush-size-number", 0);
        await rendered(driver);
        expect("brush rejects zero", driver.getProperty("brush-size-number", "value") === "1");
        // Move focus off text editors to exclude caret blinking.
        window.clearFocus();
        driver.hoverPointer("fit", { x: -20, y: 8 });
        await settled(driver);
        snapshot("workspace");
        driver.pressPointer("foreground-color", { x: 48, y: 16 });
        driver.releasePointer("foreground-color", { x: 48, y: 16 });
        driver.hoverPointer("fit", { x: -20, y: 8 });
        await settled(driver);
        snapshot("color-picker");
        driver.pressKey("Escape");
        await rendered(driver);
        driver.clickMenuItem("effect-brightness");
        await rendered(driver);
        await new Promise<void>((resolve) => setTimeout(() => resolve(), 600));
        await rendered(driver);
        expect("Apply visible", driver.isInViewport("apply-effect"));
        expect("Cancel visible", driver.isInViewport("cancel-effect"));
        window.clearFocus();
        driver.hoverPointer("fit", { x: -20, y: 8 });
        await settled(driver);
        snapshot("effect");
        driver.focus("effect-first-number");
        driver.pressKey("Escape");
        await rendered(driver);
        expect("Escape inside numeric editor cancels effect", driver.isInViewport("add-layer"));
        driver.setWindowClientSize(720, 480);
        await rendered(driver);
        expect("fit after resize", Number(driver.getProperty("paint-surface", "width")) < 672);
        expect("fit control visible", driver.isInViewport("fit"));
        window.clearFocus();
        driver.hoverPointer("fit", { x: -20, y: 8 });
        await settled(driver);
        snapshot("compact");
        driver.click("layers-toggle");
        await rendered(driver);
        expect("compact layer actions visible", driver.isInViewport("add-layer"));
        window.clearFocus();
        driver.hoverPointer("fit", { x: -20, y: 8 });
        await settled(driver);
        snapshot("compact-layers");
        driver.click("layer-properties");
        await rendered(driver);
        expect("compact opacity visible", driver.isInViewport("layer-opacity-number"));
        driver.click("layer-properties");
        await rendered(driver);
        driver.clickMenuItem("effect-brightness");
        await rendered(driver);
        expect("compact Apply visible", driver.isInViewport("apply-effect"));
        expect("compact Cancel visible", driver.isInViewport("cancel-effect"));
        window.clearFocus();
        driver.hoverPointer("fit", { x: -20, y: 8 });
        await settled(driver);
        snapshot("compact-effect");
        driver.click("cancel-effect");
        await rendered(driver);
        driver.setWindowClientSize(1120, 700);
        driver.setRenderScaling(1.5);
        await rendered(driver);
        window.clearFocus();
        driver.hoverPointer("fit", { x: -20, y: 8 });
        await settled(driver);
        snapshot("dpi150");
        driver.click("new");
        await new Promise<void>((resolve) => setTimeout(() => resolve(), 150));
        const dialog = driver.ownedWindow("New document");
        await rendered(dialog);
        expect("dialog create visible", dialog.isInViewport("create-new"));
        dialog.focus("cancel-new");
        await rendered(dialog);
        if (process.env.SHARPAINT_SNAPSHOTS)
            dialog.captureSnapshot(join(process.env.SHARPAINT_SNAPSHOTS, theme + "-new-dialog.png"));
        if (process.env.SHARPAINT_VISUAL_BASELINES)
            dialog.assertSnapshot(join(process.env.SHARPAINT_VISUAL_BASELINES, theme + "-new-dialog.png"));
        dialog.click("cancel-new");
        await rendered(driver);
        driver.setRenderScaling(1);
        driver.setWindowClientSize(1120, 700);
        driver.click("fit");
        await rendered(driver);
        const before = driver.getText("command-count");
        driver.click("text-tool");
        await rendered(driver);
        driver.dragPointer("paint-surface", [
            { x: 20, y: 20 },
            { x: 250, y: 100 }
        ]);
        await rendered(driver);
        driver.typeText("Preserve this draft");
        await rendered(driver);
        driver.click("brush-tool");
        await rendered(driver);
        expect("switching tool commits text", driver.getText("command-count") !== before);
        driver.click("undo");
        await rendered(driver);
        expect("committed text is one undo", driver.getText("command-count") === before);
        expect("layer list receives focus", driver.focus("layer-list"));
        await rendered(driver);
        expect("layer list retains focus", driver.isFocused("layer-list"));
        driver.pressKey("Home");
        await rendered(driver);
        expect(
            "native layer keyboard selection: " + driver.getText("layer-name"),
            driver.getText("layer-name") === "Editable typography"
        );
        window.dispose();
    }
    console.log("SharpPaint presentation checks passed.");
}
