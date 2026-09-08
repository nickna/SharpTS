import { createDesktopApplication, DesktopWindow } from "@sharpts/gui";
import { SharpPaintShowcase } from "./SharpPaintApp";
import { PAINT_STYLES } from "./controls";

const application = createDesktopApplication({ styles: PAINT_STYLES });
let mainWindow: DesktopWindow;
mainWindow = application.createWindow(<SharpPaintShowcase requestClose={() => mainWindow.close()} />, {
    main: true
});
if (process.env.SHARPTS_GUI_SMOKE_CLOSE === "1") setTimeout(() => application.dispose(), 50);
