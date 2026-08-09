import { TextBlock, Window, renderDesktop } from "@sharpts/gui";
import { inspectDesktopTree } from "@sharpts/gui/devtools";
import { createDesktopTestDriver } from "@sharpts/gui/testing";

const root = renderDesktop(
    <Window title="SharpTS GUI test" width={320} height={160}>
        <TextBlock key="message">Template Headless test</TextBlock>
    </Window>
);
const driver = createDesktopTestDriver(root);

if (driver.getText("message") !== "Template Headless test") {
    throw new Error("Template Headless assertion failed.");
}
if (inspectDesktopTree().windows.length !== 1) {
    throw new Error("Template inspector assertion failed.");
}

setTimeout((() => root.dispose()) as any, 0);
