import { TextBlock, Window, renderDesktop } from "@sharpts/gui";
import { closeWindow, getText, queueMicrotask } from "@sharpts/gui/internal-testing";

renderDesktop(
    <Window title="SharpTS GUI test" width={320} height={160}>
        <TextBlock key="message">Template Headless test</TextBlock>
    </Window>
);

if (getText("message") !== "Template Headless test") {
    throw new Error("Template Headless assertion failed.");
}

queueMicrotask(() => closeWindow());
