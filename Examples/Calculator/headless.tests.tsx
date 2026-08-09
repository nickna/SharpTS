import { renderDesktop } from "@sharpts/gui";
import {
    click,
    getActiveSubscriptionCount,
    getIdentity,
    getProperty,
    getText,
    pressKey,
    queueMicrotask,
    trace,
} from "@sharpts/gui/internal-testing";
import { CalculatorShowcase } from "./CalculatorApp";

function expect(name: string, condition: boolean): void {
    if (!condition) throw new Error("Calculator Headless assertion failed: " + name);
}

function buttonKey(id: string): string { return "$" + id + "/0"; }

function perform(values: string[], keyboard: boolean, done: () => void, index: number = 0): void {
    if (index >= values.length) {
        done();
        return;
    }
    if (keyboard) pressKey(values[index]);
    else click(buttonKey(values[index]));
    queueMicrotask(() => perform(values, keyboard, done, index + 1));
}

const root = renderDesktop(<CalculatorShowcase />);
expect("normal entrypoint renders calculator", getText("display") === "0");
queueMicrotask(() => {
    expect("normal entrypoint remains usable", getText("display") === "0");
    const digitIdentity = getIdentity(buttonKey("digit-1"));
    const operatorIdentity = getIdentity(buttonKey("add"));
    const subscriptions = getActiveSubscriptionCount();

    perform(["digit-1", "digit-2", "add"], false, () => {
        expect("active expression", getText("expression") === "12 +");
        expect("active operator palette", getProperty(buttonKey("add"), "background") !== getProperty(buttonKey("divide"), "background"));
        perform(["clear"], false, () => {
            expect("clear active calculation", getText("display") === "0");
            perform(["digit-1", "digit-2", "add", "digit-3", "equals"], false, () => {
                expect("mouse addition", getText("display") === "15");
                expect("result expression", getText("expression") === "12 + 3 =");

                perform(["clear", "digit-8", "divide", "digit-0", "equals"], false, () => {
                    expect("divide by zero", getText("display") === "Error");
                    expect("error status", getText("status") === "Cannot divide by zero · Press C to reset");
                    perform(["clear"], false, () => {
                        expect("clear recovers", getText("display") === "0");

                        perform(["digit-9", "multiply", "digit-4", "equals"], false, () => {
                            const mouseResult = getText("display");
                            perform(["clear"], false, () => {
                                perform(["9", "*", "4", "Enter"], true, () => {
                                    expect("mouse keyboard parity", getText("display") === mouseResult && mouseResult === "36");
                                    expect("digit identity retained", getIdentity(buttonKey("digit-1")) === digitIdentity);
                                    expect("operator identity retained", getIdentity(buttonKey("add")) === operatorIdentity);
                                    expect("subscriptions retained", getActiveSubscriptionCount() === subscriptions);
                                    trace("calculator-headless-complete");
                                    setTimeout((() => root.dispose()) as any, 25);
                                });
                            });
                        });
                    });
                });
            });
        });
    });
});
