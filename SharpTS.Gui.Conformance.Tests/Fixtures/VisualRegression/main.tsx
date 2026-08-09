import { Border, TextBlock, Window, getDesktopDisplays, renderDesktop } from "@sharpts/gui";
import { assertHeadlessSnapshot, inspectDesktopTree } from "@sharpts/gui/devtools";

const root = renderDesktop(
    <Window title="Visual regression" width={320} height={180}>
        <Border background="#cc2233">
            <TextBlock key="message">SharpTS visual baseline</TextBlock>
        </Border>
    </Window>,
);

setTimeout((() => {
    const tree = inspectDesktopTree();
    if (tree.windows.length !== 1 || tree.windows[0].children[0].kind !== "Border") {
        throw new Error("Visual regression inspector failed.");
    }
    const displays = getDesktopDisplays();
    if (displays.length === 0 || displays[0].scaling <= 0 || displays[0].bounds.width <= 0) {
        throw new Error("Desktop display/scaling contract failed.");
    }
    const created = assertHeadlessSnapshot("visual-baseline.png", true);
    const verified = assertHeadlessSnapshot("visual-baseline.png");
    if (created !== verified || created.length !== 64) {
        throw new Error("Visual regression hash verification failed.");
    }
    console.log("VISUAL_SNAPSHOT_" + verified);
    root.dispose();
}) as any, 50);
