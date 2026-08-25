import { ExactNumber, addExact, divideExact, exact, exactToNumber, formatExact, multiplyExact, negateExact, parseExact, subtractExact } from "./exact";

export type Operator = "+" | "-" | "*" | "/";

export interface CalculatorState {
    readonly display: string;
    readonly accumulator: ExactNumber | null;
    readonly pendingOperator: Operator | null;
    readonly waitingForOperand: boolean;
    readonly repeatOperator: Operator | null;
    readonly repeatOperand: ExactNumber | null;
    readonly error: boolean;
    readonly lastExpression: string;
}

export type CalculatorAction =
    | { type: "digit"; digit: string }
    | { type: "decimal" }
    | { type: "operator"; operator: Operator }
    | { type: "equals" }
    | { type: "percent" }
    | { type: "sign" }
    | { type: "backspace" }
    | { type: "clearEntry" }
    | { type: "reciprocal" }
    | { type: "square" }
    | { type: "squareRoot" }
    | { type: "clear" };

export const initialCalculatorState: CalculatorState = {
    display: "0",
    accumulator: null,
    pendingOperator: null,
    waitingForOperand: false,
    repeatOperator: null,
    repeatOperand: null,
    error: false,
    lastExpression: "",
};

export interface CalculatorPresentation {
    readonly expression: string;
    readonly status: string;
}

export function operatorGlyph(operator: Operator | null): string {
    switch (operator) {
        case "+": return "+";
        case "-": return "−";
        case "*": return "×";
        case "/": return "÷";
        case null: return "";
    }
    return "";
}

export function deriveCalculatorPresentation(state: CalculatorState): CalculatorPresentation {
    if (state.error) return {
        expression: state.lastExpression,
        status: "Cannot divide by zero · Press C to reset",
    };
    if (state.pendingOperator !== null && state.accumulator !== null) {
        const accumulator = state.accumulator === null ? exact(0n) : state.accumulator;
        const prefix = formatted(accumulator) + " " + operatorGlyph(state.pendingOperator);
        return {
            expression: state.waitingForOperand ? prefix : prefix + " " + state.display,
            status: state.waitingForOperand ? "Enter the next number" : "Ready to calculate",
        };
    }
    if (state.repeatOperator !== null)
        return { expression: state.lastExpression, status: "Result · Press = to repeat" };
    return {
        expression: state.lastExpression,
        status: state.display === "0" ? "Ready" : "Entering a number",
    };
}

export function calculatorActionForKey(key: string): CalculatorAction | null {
    if (key >= "0" && key <= "9") return { type: "digit", digit: key };
    if (key === "." || key === "Decimal") return { type: "decimal" };
    if (key === "+") return { type: "operator", operator: "+" };
    if (key === "-") return { type: "operator", operator: "-" };
    if (key === "*" || key.toLowerCase() === "x") return { type: "operator", operator: "*" };
    if (key === "/") return { type: "operator", operator: "/" };
    if (key === "Enter" || key === "=") return { type: "equals" };
    if (key === "%") return { type: "percent" };
    if (key === "Backspace") return { type: "backspace" };
    if (key === "Delete") return { type: "clearEntry" };
    if (key.toLowerCase() === "r") return { type: "reciprocal" };
    if (key === "@") return { type: "squareRoot" };
    if (key.toLowerCase() === "q") return { type: "square" };
    if (key === "F9") return { type: "sign" };
    if (key === "Escape" || key.toLowerCase() === "c") return { type: "clear" };
    return null;
}

function numberOf(state: CalculatorState): ExactNumber {
    return parseExact(state.display);
}

function calculateStandard(left: ExactNumber, right: ExactNumber, operator: Operator): ExactNumber | null {
    switch (operator) {
        case "+": return addExact(left, right);
        case "-": return subtractExact(left, right);
        case "*": return multiplyExact(left, right);
        case "/": return right.numerator === 0n ? null : divideExact(left, right);
    }
    return null;
}

function formatted(value: ExactNumber): string {
    return formatExact(value);
}

function resultState(state: CalculatorState, value: ExactNumber | null, operator: Operator | null, operand: ExactNumber | null, left: ExactNumber): CalculatorState {
    const display = value === null ? "Error" : formatted(value);
    const actualOperand = operand === null ? exact(0n) : operand;
    const lastExpression = operator === null || operand === null
        ? state.lastExpression
        : formatted(left) + " " + operatorGlyph(operator) + " " + formatted(actualOperand) + " =";
    if (display === "Error" || value === null) return { ...initialCalculatorState, display, waitingForOperand: true, error: true, lastExpression };
    const actualValue = value === null ? exact(0n) : value;
    return {
        ...state,
        display,
        accumulator: actualValue,
        pendingOperator: null,
        waitingForOperand: true,
        repeatOperator: operator,
        repeatOperand: operand,
        error: false,
        lastExpression,
    };
}

function resetForInput(state: CalculatorState): CalculatorState {
    return state.error ? initialCalculatorState : state;
}

export function calculatorReducer(state: CalculatorState, action: CalculatorAction): CalculatorState {
    switch (action.type) {
        case "clear": return initialCalculatorState;
        case "digit": {
            state = resetForInput(state);
            const digit = action.digit === undefined ? "0" : action.digit;
            if (state.waitingForOperand) return {
                ...state,
                display: digit,
                waitingForOperand: false,
                lastExpression: state.pendingOperator === null ? "" : state.lastExpression,
            };
            const digits = state.display.split("").filter(character => character >= "0" && character <= "9").length;
            if (digits >= 15) return state;
            return { ...state, display: state.display === "0" ? digit : state.display + digit };
        }
        case "decimal": {
            state = resetForInput(state);
            if (state.waitingForOperand) return {
                ...state,
                display: "0.",
                waitingForOperand: false,
                lastExpression: state.pendingOperator === null ? "" : state.lastExpression,
            };
            return state.display.indexOf(".") >= 0 ? state : { ...state, display: state.display + "." };
        }
        case "sign": {
            if (state.error || state.display === "0") return state;
            return { ...state, display: state.display.startsWith("-") ? state.display.slice(1) : "-" + state.display };
        }
        case "backspace": {
            if (state.error || state.waitingForOperand) return state;
            const next = state.display.length <= 1 || (state.display.length === 2 && state.display.startsWith("-"))
                ? "0" : state.display.slice(0, state.display.length - 1);
            return { ...state, display: next };
        }
        case "clearEntry": {
            if (state.error) return initialCalculatorState;
            return { ...state, display: "0", waitingForOperand: false };
        }
        case "reciprocal": {
            if (state.error) return state;
            const current = numberOf(state);
            const value = current.numerator === 0n ? null : divideExact(exact(1n), current);
            return resultState(state, value, null, null, current);
        }
        case "square": {
            if (state.error) return state;
            const current = numberOf(state);
            return resultState(state, multiplyExact(current, current), null, null, current);
        }
        case "squareRoot": {
            if (state.error) return state;
            const current = numberOf(state);
            const numeric = exactToNumber(current);
            if (numeric < 0) return resultState(state, null, null, null, current);
            return resultState(state, parseExact(String(Math.sqrt(numeric))), null, null, current);
        }
        case "operator": {
            if (state.error) return state;
            const operator: Operator = action.operator === undefined ? "+" : action.operator;
            const current = numberOf(state);
            if (state.pendingOperator !== null && state.accumulator !== null && !state.waitingForOperand) {
                const accumulator = state.accumulator === null ? exact(0n) : state.accumulator;
                const value = calculateStandard(accumulator, current, state.pendingOperator);
                const next = resultState(state, value, state.pendingOperator, current, accumulator);
                return next.error ? next : { ...next, pendingOperator: operator, waitingForOperand: true, repeatOperator: null, repeatOperand: null, lastExpression: "" };
            }
            return {
                ...state,
                accumulator: state.accumulator === null || !state.waitingForOperand ? current : state.accumulator,
                pendingOperator: operator,
                waitingForOperand: true,
                repeatOperator: null,
                repeatOperand: null,
                lastExpression: "",
            };
        }
        case "percent": {
            if (state.error) return state;
            const current = numberOf(state);
            const accumulator = state.accumulator === null ? exact(0n) : state.accumulator;
            const oneHundred = exact(100n);
            const value = state.pendingOperator !== null && state.accumulator !== null &&
                (state.pendingOperator === "+" || state.pendingOperator === "-")
                ? divideExact(multiplyExact(accumulator, current), oneHundred)
                : divideExact(current, oneHundred);
            return { ...state, display: formatted(value), waitingForOperand: false };
        }
        case "equals": {
            if (state.error) return state;
            const current = numberOf(state);
            if (state.pendingOperator !== null) {
                const left = state.accumulator === null ? current : state.accumulator;
                const operand = state.waitingForOperand ? left : current;
                return resultState(state, calculateStandard(left, operand, state.pendingOperator), state.pendingOperator, operand, left);
            }
            if (state.repeatOperator !== null && state.repeatOperand !== null) {
                const operand = state.repeatOperand === null ? exact(0n) : state.repeatOperand;
                return resultState(state, calculateStandard(current, operand, state.repeatOperator), state.repeatOperator, operand, current);
            }
            return state;
        }
    }
    return state;
}
