import { createDesktopApplication } from "@sharpts/gui";
import { createDesktopTestDriver, DesktopTestDriver } from "@sharpts/gui/testing";
import { CalculatorShowcase } from "./CalculatorApp";

function expect(name: string, condition: boolean): void {
    if (!condition) throw new Error("Calculator Headless assertion failed: " + name);
}

function buttonKey(id: string): string { return "$" + id + "/0"; }
let driver: DesktopTestDriver;

function perform(values: string[], keyboard: boolean, done: () => void, index: number = 0): void {
    if (index >= values.length) {
        done();
        return;
    }
    if (keyboard) driver.pressKey(values[index]);
    else driver.click(buttonKey(values[index]));
    driver.afterRender(() => perform(values, keyboard, done, index + 1));
}

const application = createDesktopApplication();
const window = application.createWindow(<CalculatorShowcase />, { main: true });
driver = createDesktopTestDriver(window);
expect("normal entrypoint renders calculator", driver.getText("display") === "0");
driver.afterRender(() => {
    expect("normal entrypoint remains usable", driver.getText("display") === "0");

    perform(["digit-1", "digit-2", "add"], false, () => {
        expect("active expression", driver.getText("expression") === "12 +");
        expect("active operator palette", driver.getProperty(buttonKey("add"), "background") !== driver.getProperty(buttonKey("divide"), "background"));
        perform(["clear"], false, () => {
            expect("clear active calculation", driver.getText("display") === "0");
            perform(["digit-1", "digit-2", "add", "digit-3", "equals"], false, () => {
                expect("mouse addition", driver.getText("display") === "15");
                expect("result expression", driver.getText("expression") === "12 + 3 =");

                perform(["clear", "digit-8", "divide", "digit-0", "equals"], false, () => {
                    expect("divide by zero", driver.getText("display") === "Error");
                    expect("error status", driver.getText("status") === "Cannot divide by zero · Press C to reset");
                    perform(["clear"], false, () => {
                        expect("clear recovers", driver.getText("display") === "0");

                        perform(["digit-9", "multiply", "digit-4", "equals"], false, () => {
                            const mouseResult = driver.getText("display");
                            perform(["clear"], false, () => {
                                perform(["9", "*", "4", "Enter"], true, () => {
                                    expect("mouse keyboard parity", driver.getText("display") === mouseResult && mouseResult === "36");
                                    setTimeout((() => application.dispose()) as any, 25);
                                });
                            });
                        });
                    });
                });
            });
        });
    });
});
