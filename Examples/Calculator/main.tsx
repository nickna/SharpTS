import { createDesktopApplication } from "@sharpts/gui";
import { CalculatorShowcase } from "./CalculatorApp";

const application = createDesktopApplication();
application.createWindow(<CalculatorShowcase />, { main: true });
if (process.env.SHARPTS_GUI_SMOKE_CLOSE === "1") {
    setTimeout((() => application.dispose()) as any, 25);
}
