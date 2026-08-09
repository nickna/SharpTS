import { renderDesktop } from "@sharpts/gui";
import { CalculatorShowcase } from "./CalculatorApp";

const root = renderDesktop(<CalculatorShowcase />);
if (process.env.SHARPTS_GUI_SMOKE_CLOSE === "1") {
    setTimeout((() => root.dispose()) as any, 25);
}
