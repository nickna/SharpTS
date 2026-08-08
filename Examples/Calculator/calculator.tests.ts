import {
    CalculatorAction,
    CalculatorState,
    Operator,
    calculatorReducer,
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

expectDisplay("addition", "15", [digit("1"), digit("2"), operator("+"), digit("3"), { type: "equals" }]);
expectDisplay("repeated equals", "18", [digit("1"), digit("2"), operator("+"), digit("3"), { type: "equals" }, { type: "equals" }]);
expectDisplay("chained operations", "20", [digit("2"), operator("+"), digit("3"), operator("*"), digit("4"), { type: "equals" }]);
expectDisplay("contextual percent", "220", [digit("2"), digit("0"), digit("0"), operator("+"), digit("1"), digit("0"), { type: "percent" }, { type: "equals" }]);
expectDisplay("standalone percent", "0.5", [digit("5"), digit("0"), { type: "percent" }]);
expectDisplay("decimal sign and backspace", "-1.2", [digit("1"), { type: "decimal" }, digit("2"), digit("3"), { type: "backspace" }, { type: "sign" }]);
expectDisplay("digit limit", "123456789012345", "1234567890123456".split("").map(value => digit(value)));
expectDisplay("error recovery", "7", [digit("8"), operator("/"), digit("0"), { type: "equals" }, digit("7")]);

console.log("Calculator model tests passed.");
