import { createDesktopApplication } from "@sharpts/gui";
import { createDesktopTestDriver, DesktopTestDriver } from "@sharpts/gui/testing";
import { existsSync, writeFileSync } from "fs";
import { join } from "path";
import { SharpPaintShowcase } from "./SharpPaintApp";
import { createDocument, serializeProject } from "./document";

function expect(name: string, condition: boolean): void {
    if (!condition) throw new Error("SharpPaint Headless assertion failed: " + name);
}

const application = createDesktopApplication();
let driver: DesktopTestDriver;
let window: any = null;
window = application.createWindow(<SharpPaintShowcase requestClose={() => window.close()} />, { main: true });
driver = createDesktopTestDriver(window);
let eraserWindow: any = null;
eraserWindow = application.createWindow(<SharpPaintShowcase requestClose={() => eraserWindow.close()} />);
const eraserDriver = createDesktopTestDriver(eraserWindow);
let cancelWindow: any = null;
cancelWindow = application.createWindow(<SharpPaintShowcase requestClose={() => cancelWindow.close()} />);
const cancelDriver = createDesktopTestDriver(cancelWindow);
let responsiveWindow: any = null;
responsiveWindow = application.createWindow(<SharpPaintShowcase requestClose={() => responsiveWindow.close()} />);
const responsiveDriver = createDesktopTestDriver(responsiveWindow);
const openProjectPath = join(process.cwd(), "SharpPaint.Headless.Open.sharpaint");
const saveProjectPath = join(process.cwd(), "SharpPaint.Headless.Save.sharpaint");
writeFileSync(openProjectPath, serializeProject(createDocument(320, 240)), "utf8");

expect("initial canvas", driver.getText("command-count") === "1 commands · 1 layers");
expect("paint surface automation", driver.getProperty("paint-surface", "automationName") === "Paint surface");
expect("brush glyph content", driver.getText("brush-glyph") === "✎");
expect("brush label content", driver.getText("brush-label") === "Brush");
expect("filled-shapes label content", driver.getText("filled-label") === "Filled shapes");

responsiveDriver.setWindowClientSize(840, 560);
responsiveDriver.afterRender(() => {
    expect("narrow mode is reported", responsiveDriver.getText("layout-mode") === "narrow");
    expect("narrow layout hides layers", responsiveDriver.getProperty("layers-panel", "isVisible") === "False");
    expect("narrow layout exposes layers toggle", responsiveDriver.getProperty("layers-toggle", "isVisible") === "True");
    expect("short layout hides deferred layer action", responsiveDriver.getProperty("merge-layer", "isVisible") === "False");
    responsiveDriver.click("layers-toggle");
    responsiveDriver.afterRender(() => {
        expect("narrow layers can be opened", responsiveDriver.getProperty("layers-panel", "isVisible") === "True");
        responsiveDriver.setWindowClientSize(1180, 720);
        responsiveDriver.afterRender(() => {
            expect("wide mode is reported", responsiveDriver.getText("layout-mode") === "wide");
            expect("wide layout retains layers", responsiveDriver.getProperty("layers-panel", "isVisible") === "True");
            expect("wide layout hides layers toggle", responsiveDriver.getProperty("layers-toggle", "isVisible") === "False");
            expect("wide layout restores deferred layer action", responsiveDriver.getProperty("merge-layer", "isVisible") === "True");
        });
    });
});

cancelDriver.pressPointer("paint-surface", { x: 16, y: 18 });
cancelDriver.movePointer("paint-surface", { x: 48, y: 52 });
cancelDriver.cancelPointer("paint-surface");
cancelDriver.afterRender(() => {
    expect("cancelled gesture is discarded", cancelDriver.getText("command-count") === "1 commands · 1 layers");
});

driver.dragPointer("paint-surface", [{ x: 12, y: 14 }, { x: 40, y: 42 }, { x: 72, y: 58 }]);
driver.afterRender(() => {
    expect("brush gesture commits once", driver.getText("command-count") === "2 commands · 1 layers");
    driver.click("undo");
    driver.afterRender(() => {
        expect("undo gesture", driver.getText("command-count") === "1 commands · 1 layers");
        driver.click("redo");
        driver.afterRender(() => {
            expect("redo gesture", driver.getText("command-count") === "2 commands · 1 layers");
            driver.click("add-layer");
            driver.afterRender(() => {
                expect("add layer", driver.getText("command-count") === "2 commands · 2 layers");
                driver.setSliderValue("layer-opacity", 0.4);
                driver.afterRender(() => {
                    expect("opacity is a document edit", driver.getText("status") === "Redid document change" || driver.getText("command-count") === "2 commands · 2 layers");
                    eraserDriver.click("eraser");
                    eraserDriver.afterRender(() => {
                        eraserDriver.dragPointer("paint-surface", [{ x: 20, y: 20 }, { x: 60, y: 60 }]);
                        eraserDriver.afterRender(() => {
                            expect("eraser gesture commits", eraserDriver.getText("command-count") === "2 commands · 1 layers");
                            driver.setSliderValue("zoom", 1.25);
                            driver.afterRender(() => {
                                expect("view change preserves command state", driver.getText("command-count") === "2 commands · 2 layers");
                                driver.queueMessageDialogResult("yes");
                                driver.click("new");
                                driver.afterRender(() => {
                                    expect("new button opens dialog (" + driver.getText("status") + ")",
                                        driver.getText("new-width") === "1024");
                                    driver.click("cancel-new");
                                    driver.afterRender(() => {
                                        driver.queueMessageDialogResult("yes");
                                        driver.queueOpenFileDialogResult([openProjectPath]);
                                        driver.click("open");
                                        driver.afterRender(() => {
                                            expect("open button loads project",
                                                driver.getText("status") === "Opened SharpPaint.Headless.Open.sharpaint");
                                            driver.click("save");
                                            driver.afterRender(() => {
                                                expect("save button reports success",
                                                    driver.getText("status") === "Saved SharpPaint.Headless.Open.sharpaint");
                                                driver.queueSaveFileDialogResult(saveProjectPath);
                                                driver.clickMenuItem("menu-save-as");
                                                driver.afterRender(() => {
                                                    expect("save-as menu writes project", existsSync(saveProjectPath));
                                                    driver.clickMenuItem("menu-new");
                                                    driver.afterRender(() => {
                                                        expect("new menu item opens dialog", driver.getText("new-width") === "1024");
                                                        setTimeout((() => application.dispose()) as any, 25);
                                                    });
                                                });
                                            });
                                        });
                                    });
                                });
                            });
                        });
                    });
                });
            });
        });
    });
});
