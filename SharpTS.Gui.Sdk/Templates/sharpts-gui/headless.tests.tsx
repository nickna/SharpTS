import { TextBlock, Window, createDesktopApplication } from "@sharpts/gui";
import { inspectDesktopTree } from "@sharpts/gui/devtools";
import { createDesktopTestDriver } from "@sharpts/gui/testing";

const application = createDesktopApplication();
const window = application.createWindow(
    <Window title="SharpTS GUI test" width={320} height={160}>
        <TextBlock key="message">Template Headless test</TextBlock>
    </Window>,
    { main: true },
);
const driver = createDesktopTestDriver(window);

if (driver.getText("message") !== "Template Headless test") {
    throw new Error("Template Headless assertion failed.");
}
if (inspectDesktopTree().windows.length !== 1) {
    throw new Error("Template inspector assertion failed.");
}

setTimeout((() => application.dispose()) as any, 0);
