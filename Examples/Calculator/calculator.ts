export type Operator = "+" | "-" | "*" | "/";

export interface CalculatorState {
    readonly display: string;
    readonly accumulator: number | null;
    readonly pendingOperator: Operator | null;
    readonly waitingForOperand: boolean;
    readonly repeatOperator: Operator | null;
    readonly repeatOperand: number | null;
    readonly error: boolean;
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
};

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

function resultState(state: CalculatorState, value: number, operator: Operator | null, operand: number | null): CalculatorState {
    const display = formatted(value);
    if (display === "Error") return { ...initialCalculatorState, display, waitingForOperand: true, error: true };
    return {
        ...state,
        display,
        accumulator: value,
        pendingOperator: null,
        waitingForOperand: true,
        repeatOperator: operator,
        repeatOperand: operand,
        error: false,
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
            if (state.waitingForOperand) return { ...state, display: digit, waitingForOperand: false };
            const digits = state.display.split("").filter(character => character >= "0" && character <= "9").length;
            if (digits >= 15) return state;
            return { ...state, display: state.display === "0" ? digit : state.display + digit };
        }
        case "decimal": {
            state = resetForInput(state);
            if (state.waitingForOperand) return { ...state, display: "0.", waitingForOperand: false };
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
                const next = resultState(state, value, null, null);
                return next.error ? next : { ...next, pendingOperator: operator, waitingForOperand: true, repeatOperator: null, repeatOperand: null };
            }
            return {
                ...state,
                accumulator: state.accumulator === null || !state.waitingForOperand ? current : state.accumulator,
                pendingOperator: operator,
                waitingForOperand: true,
                repeatOperator: null,
                repeatOperand: null,
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
                return resultState(state, calculate(left, operand, state.pendingOperator), state.pendingOperator, operand);
            }
            if (state.repeatOperator !== null && state.repeatOperand !== null)
                return resultState(state, calculate(current, state.repeatOperand, state.repeatOperator), state.repeatOperator, state.repeatOperand);
            return state;
        }
    }
    return state;
}
