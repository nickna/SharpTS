import {
    CalculatorAction,
    CalculatorState,
    Operator,
    calculatorReducer,
    calculatorActionForKey,
    deriveCalculatorPresentation,
    initialCalculatorState,
} from "./calculator";
import { addExact, divideExact, formatExact, parseExact } from "./exact";
import { evaluateExpression } from "./expression";
import { programmerReducer, initialProgrammerState } from "./programmer";
import { addCalendarDuration, daysBetweenDates, durationBetweenDates } from "./dateCalculation";
import { convertCurrency, convertUnit, OFFLINE_CURRENCY_RATES, UNIT_FAMILIES } from "./converters";
import { DEFAULT_VIEWPORT, sampleEquation, traceEquation } from "./graphing";

function run(actions: CalculatorAction[]): CalculatorState {
    let state = initialCalculatorState;
    for (const action of actions) state = calculatorReducer(state, action);
    return state;
}

function digit(value: string): CalculatorAction { return { type: "digit", digit: value }; }
function operator(value: Operator): CalculatorAction { return { type: "operator", operator: value }; }
function expectDisplay(name: string, expected: string, actions: CalculatorAction[]): void {
    const actual = run(actions).display;
    if (actual !== expected) throw new Error(name + ": expected " + expected + ", received " + actual);
}
function expectPresentation(name: string, expectedExpression: string, expectedStatus: string, actions: CalculatorAction[]): void {
    const presentation = deriveCalculatorPresentation(run(actions));
    if (presentation.expression !== expectedExpression)
        throw new Error(name + ": expected expression " + expectedExpression + ", received " + presentation.expression);
    if (presentation.status !== expectedStatus)
        throw new Error(name + ": expected status " + expectedStatus + ", received " + presentation.status);
}
function keys(values: string[]): CalculatorAction[] {
    const actions: CalculatorAction[] = [];
    for (const value of values) {
        const action = calculatorActionForKey(value);
        if (action !== null) actions.push(action);
    }
    return actions;
}

expectDisplay("addition", "15", [digit("1"), digit("2"), operator("+"), digit("3"), { type: "equals" }]);
expectDisplay("repeated equals", "18", [digit("1"), digit("2"), operator("+"), digit("3"), { type: "equals" }, { type: "equals" }]);
expectDisplay("chained operations", "20", [digit("2"), operator("+"), digit("3"), operator("*"), digit("4"), { type: "equals" }]);
expectDisplay("contextual percent", "220", [digit("2"), digit("0"), digit("0"), operator("+"), digit("1"), digit("0"), { type: "percent" }, { type: "equals" }]);
expectDisplay("standalone percent", "0.5", [digit("5"), digit("0"), { type: "percent" }]);
expectDisplay("decimal sign and backspace", "-1.2", [digit("1"), { type: "decimal" }, digit("2"), digit("3"), { type: "backspace" }, { type: "sign" }]);
expectDisplay("digit limit", "123456789012345", "1234567890123456".split("").map(value => digit(value)));
expectDisplay("error recovery", "7", [digit("8"), operator("/"), digit("0"), { type: "equals" }, digit("7")]);
expectDisplay("clear active calculation", "0", [digit("1"), digit("2"), operator("+"), digit("3"), { type: "clear" }]);
expectDisplay("clear after error", "0", [digit("8"), operator("/"), digit("0"), { type: "equals" }, { type: "clear" }]);
expectDisplay("repeated clear", "0", [digit("9"), { type: "clear" }, { type: "clear" }, { type: "clear" }]);
expectDisplay("keyboard equivalent", "15", keys(["1", "2", "+", "3", "Enter"]));
expectPresentation("pending expression", "12 + 3", "Ready to calculate", [digit("1"), digit("2"), operator("+"), digit("3")]);
expectPresentation("result expression", "12 + 3 =", "Result · Press = to repeat", [digit("1"), digit("2"), operator("+"), digit("3"), { type: "equals" }]);
expectPresentation("error expression", "8 ÷ 0 =", "Cannot divide by zero · Press C to reset", [digit("8"), operator("/"), digit("0"), { type: "equals" }]);

if (formatExact(addExact(parseExact("0.1"), parseExact("0.2"))) !== "0.3") throw new Error("exact decimal addition failed");
if (formatExact(divideExact(parseExact("1"), parseExact("8"))) !== "0.125") throw new Error("exact division failed");
if (evaluateExpression("2 + 3 * 4") !== 14) throw new Error("scientific precedence failed");
if (Math.abs(evaluateExpression("sin(30)", { angleUnit: "deg" }) - 0.5) > 0.0000000001) throw new Error("degree trig failed");
if (evaluateExpression("5!") !== 120) throw new Error("factorial failed");

let programmer = programmerReducer(initialProgrammerState, { type: "digit", digit: "1" });
programmer = programmerReducer(programmer, { type: "operator", operator: "lsh" });
programmer = programmerReducer(programmer, { type: "digit", digit: "3" });
programmer = programmerReducer(programmer, { type: "equals" });
if (programmer.value !== 8n) throw new Error("programmer shift failed");
programmer = programmerReducer(programmer, { type: "wordSize", wordSize: 8 });
programmer = programmerReducer(programmer, { type: "toggleBit", bit: 7 });
if (programmer.value !== -120n) throw new Error("programmer bit toggle failed");

if (daysBetweenDates("2024-03-09", "2024-03-11") !== 2) throw new Error("date difference must ignore DST");
const duration = durationBetweenDates("2020-01-15", "2022-03-20");
if (duration.years !== 2 || duration.months !== 2 || duration.days !== 5) throw new Error("calendar duration failed");
if (addCalendarDuration("2024-01-31", { years: 0, months: 1, days: 0 }) !== "2024-02-29") throw new Error("calendar month clamp failed");
if (addCalendarDuration("2024-03-31", { years: 0, months: 1, days: 0 }, true) !== "2024-02-29") throw new Error("calendar subtraction failed");

const lengthFamily = UNIT_FAMILIES[0];
if (Math.abs(convertUnit(5, lengthFamily, 1, 5) - 3.1068559611866697) > 0.0000000001) throw new Error("length conversion failed");
if (convertCurrency(1, "USD", "EUR", OFFLINE_CURRENCY_RATES) !== 0.92) throw new Error("currency conversion failed");
if (Math.abs(traceEquation("y=x^2", 3).y - 9) > 0.0000000001) throw new Error("graph trace failed");
if (sampleEquation("y=x", { ...DEFAULT_VIEWPORT, width: 20 }).length !== 11) throw new Error("graph sampling failed");

console.log("Calculator model tests passed.");
