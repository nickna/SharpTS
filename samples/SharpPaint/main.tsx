import { createDesktopApplication } from "@sharpts/gui";
import { SharpPaintShowcase } from "./SharpPaintApp";

const application = createDesktopApplication();
let mainWindow: any = null;
mainWindow = application.createWindow(
    <SharpPaintShowcase requestClose={() => mainWindow.close()} />,
    { main: true });
if (process.env.SHARPTS_GUI_SMOKE_CLOSE === "1")
    setTimeout((() => application.dispose()) as any, 50);
