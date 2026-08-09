import { TextBlock, Window, renderDesktop } from "@sharpts/gui";
import { inspectDesktopTree } from "@sharpts/gui/devtools";
import { closeWindow, getText } from "@sharpts/gui/internal-testing";

renderDesktop(
    <Window title="SharpTS GUI test" width={320} height={160}>
        <TextBlock key="message">Template Headless test</TextBlock>
    </Window>
);

if (getText("message") !== "Template Headless test") {
    throw new Error("Template Headless assertion failed.");
}
if (inspectDesktopTree().windows.length !== 1) {
    throw new Error("Template inspector assertion failed.");
}

setTimeout((() => closeWindow()) as any, 0);
