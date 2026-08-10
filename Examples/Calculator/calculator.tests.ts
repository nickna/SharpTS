import {
    CalculatorAction,
    CalculatorState,
    Operator,
    calculatorReducer,
    calculatorActionForKey,
    deriveCalculatorPresentation,
    initialCalculatorState,
} from "./calculator";

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

console.log("Calculator model tests passed.");
