import { TextBlock, Window, createDesktopApplication } from "@sharpts/gui";

const version = 1;
const application = createDesktopApplication({ shutdownMode: "explicit" });
application.createWindow(
    <Window title={"Hot reload " + version}><TextBlock>{"Version " + version}</TextBlock></Window>,
    { main: true },
);
console.log("HOT_RELOAD_VERSION_" + version);
if (version === 2) setTimeout((() => application.shutdown(0)) as any, 100);
