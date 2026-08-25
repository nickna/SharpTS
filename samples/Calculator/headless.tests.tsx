import { createDesktopApplication } from "@sharpts/gui";
import { createDesktopTestDriver, DesktopTestDriver } from "@sharpts/gui/testing";
import { CalculatorShowcase } from "./CalculatorApp";

function expect(name: string, condition: boolean): void {
    if (!condition) throw new Error("Calculator Headless assertion failed: " + name);
}

function buttonKey(id: string): string { return "$" + id + "/0"; }
function controlKey(id: string): string { return id; }
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
function performControls(values: string[], done: () => void, index: number = 0): void {
    if (index >= values.length) { done(); return; }
    driver.click(controlKey(values[index]));
    driver.afterRender(() => performControls(values, done, index + 1));
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
                                    driver.setComboBoxIndex(controlKey("calculator-mode"), 1);
                                    driver.afterRender(() => {
                                        performControls(["scientific-17", "scientific-23", "scientific-18", "scientific-15", "scientific-12", "scientific-equals"], () => {
                                            expect("scientific precedence", driver.getText("display") === "14");
                                            driver.setComboBoxIndex(controlKey("calculator-mode"), 2);
                                            driver.afterRender(() => {
                                                driver.click(controlKey("programmer-digit-1"));
                                                driver.afterRender(() => {
                                                    driver.setComboBoxIndex(controlKey("programmer-operator"), 3);
                                                    driver.afterRender(() => {
                                                        driver.click(controlKey("programmer-digit-3"));
                                                        driver.afterRender(() => {
                                                            driver.click(controlKey("programmer-equals"));
                                                            driver.afterRender(() => {
                                                                const programmerDisplay = driver.getText("display");
                                                                expect("programmer shift received " + programmerDisplay + " status " + driver.getText("status"), programmerDisplay === "8");
                                                                driver.setComboBoxIndex(controlKey("calculator-mode"), 3);
                                                                driver.afterRender(() => {
                                                                    expect("date mode", driver.getText(controlKey("date-difference")) === "0 days · 0 years, 0 months, 0 days");
                                                                    driver.setComboBoxIndex(controlKey("calculator-mode"), 4);
                                                                    driver.afterRender(() => {
                                                                        const converted = driver.getText(controlKey("converter-result"));
                                                                        expect("unit conversion received " + converted, Math.abs(Number.parseFloat(converted) - 0.001) < 0.000000001);
                                                                        driver.setComboBoxIndex(controlKey("converter-family"), 12);
                                                                        driver.afterRender(() => {
                                                                            expect("currency conversion", driver.getText(controlKey("converter-result")) === "0.92");
                                                                            driver.setComboBoxIndex(controlKey("calculator-mode"), 5);
                                                                            driver.afterRender(() => {
                                                                                expect("graph trace", driver.getText(controlKey("graph-trace-result")) === "x=0, y=0");
                                                                                driver.click(controlKey("graph-zoom-in"));
                                                                                driver.afterRender(() => {
                                                                                    driver.setComboBoxIndex(controlKey("calculator-mode"), 0);
                                                                                    driver.afterRender(() => {
                                                                                        driver.click(controlKey("topmost-toggle"));
                                                                                        driver.afterRender(() => {
                                                                                            expect("always on top", driver.getProperty(controlKey("topmost-toggle"), "automationName") === "Back to full view");
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
                                                    });
                                                });
                                            });
                                        });
                                    });
                                });
                            });
                        });
                    });
                });
            });
        });
    });
});
