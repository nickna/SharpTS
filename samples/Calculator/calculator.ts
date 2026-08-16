export type Operator = "+" | "-" | "*" | "/";

export interface CalculatorState {
    readonly display: string;
    readonly accumulator: number | null;
    readonly pendingOperator: Operator | null;
    readonly waitingForOperand: boolean;
    readonly repeatOperator: Operator | null;
    readonly repeatOperand: number | null;
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
        const prefix = formatted(state.accumulator) + " " + operatorGlyph(state.pendingOperator);
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
    if (key === "Backspace" || key === "Delete") return { type: "backspace" };
    if (key === "Escape" || key.toLowerCase() === "c") return { type: "clear" };
    return null;
}

function numberOf(state: CalculatorState): number {
    return Number.parseFloat(state.display);
}

function calculate(left: number, right: number, operator: Operator): number {
    switch (operator) {
        case "+": return left + right;
        case "-": return left - right;
        case "*": return left * right;
        case "/": return right === 0 ? Number.NaN : left / right;
    }
    return Number.NaN;
}

function formatted(value: number): string {
    if (!Number.isFinite(value)) return "Error";
    if (Object.is(value, -0)) return "0";
    return Number.parseFloat(value.toPrecision(12)).toString();
}

function resultState(state: CalculatorState, value: number, operator: Operator | null, operand: number | null, left: number): CalculatorState {
    const display = formatted(value);
    const lastExpression = operator === null || operand === null
        ? state.lastExpression
        : formatted(left) + " " + operatorGlyph(operator as Operator) + " " + formatted(operand as number) + " =";
    if (display === "Error") return { ...initialCalculatorState, display, waitingForOperand: true, error: true, lastExpression };
    return {
        ...state,
        display,
        accumulator: value,
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
        case "operator": {
            if (state.error) return state;
            const operator: Operator = action.operator === undefined ? "+" : action.operator;
            const current = numberOf(state);
            if (state.pendingOperator !== null && state.accumulator !== null && !state.waitingForOperand) {
                const value = calculate(state.accumulator, current, state.pendingOperator);
                const next = resultState(state, value, state.pendingOperator, current, state.accumulator);
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
            const accumulator = state.accumulator === null ? 0 : state.accumulator;
            const value = state.pendingOperator !== null && state.accumulator !== null &&
                (state.pendingOperator === "+" || state.pendingOperator === "-")
                ? accumulator * current / 100
                : current / 100;
            return { ...state, display: formatted(value), waitingForOperand: false };
        }
        case "equals": {
            if (state.error) return state;
            const current = numberOf(state);
            if (state.pendingOperator !== null) {
                const left = state.accumulator === null ? current : state.accumulator;
                const operand = state.waitingForOperand ? left : current;
                return resultState(state, calculate(left, operand, state.pendingOperator), state.pendingOperator, operand, left);
            }
            if (state.repeatOperator !== null && state.repeatOperand !== null)
                return resultState(state, calculate(current, state.repeatOperand, state.repeatOperator), state.repeatOperator, state.repeatOperand, current);
            return state;
        }
    }
    return state;
}
