import { StackPanel, TextBlock, Window, renderDesktop } from "@sharpts/gui";
import { closeWindow, trace } from "@sharpts/gui/internal-testing";

process.on("beforeExit", () => {
    trace("tla-before-exit");
    queueMicrotask(() => trace("tla-before-exit-microtask"));
});
process.on("exit", () => trace("tla-exit"));

trace("tla-main-start");
try {
    const rejectedCompound = 1 + await Promise.reject(new Error("compound-rejected"));
    trace(`unexpected-compound-${rejectedCompound}`);
} catch (error) {
    trace("tla-compound-rejected");
}
try {
    const rejectedConditional = true
        ? await Promise.reject(new Error("conditional-rejected"))
        : 0;
    trace(`unexpected-conditional-${rejectedConditional}`);
} catch (error) {
    trace("tla-conditional-rejected");
}
try {
    for (let index = 0; index < 1; index++) {
        await Promise.reject(new Error("loop-rejected"));
    }
    trace("unexpected-loop");
} catch (error) {
    trace("tla-loop-rejected");
}
try {
    await import("./rejected");
    trace("unexpected-dynamic-import");
} catch (error) {
    trace("tla-dynamic-import-rejected");
}
const compound = 2 + await Promise.resolve(3);
const conditional = compound === 5 ? await Promise.resolve(7) : 0;
let loop = 0;
for (let index = 1; index <= 3; index++) {
    loop += await Promise.resolve(index);
}
const lazyPath = await Promise.resolve("./lazy");
const loaded = await import(await Promise.resolve(lazyPath));
trace(`tla-main-resume-${compound}-${conditional}-${loop}-${loaded.value}`);

renderDesktop(
    <Window title="Hosted top-level await" width={360} height={180}>
        <StackPanel>
            <TextBlock>{`Ready ${loaded.value}`}</TextBlock>
        </StackPanel>
    </Window>
);
trace("tla-window-mounted");
setTimeout((() => closeWindow()) as any, 1);
