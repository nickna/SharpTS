export type ProgrammerBase = 2 | 8 | 10 | 16;
export type WordSize = 8 | 16 | 32 | 64;
export type ProgrammerBinaryOperator = "+" | "-" | "*" | "/" | "mod" | "and" | "or" | "xor" | "lsh" | "rsh" | "rol" | "ror";

export interface ProgrammerState {
    readonly value: bigint;
    readonly entry: string;
    readonly base: ProgrammerBase;
    readonly wordSize: WordSize;
    readonly accumulator: bigint | null;
    readonly pendingOperator: ProgrammerBinaryOperator | null;
    readonly waitingForOperand: boolean;
    readonly error: string;
}

export type ProgrammerAction =
    | { type: "digit"; digit: string }
    | { type: "base"; base: ProgrammerBase }
    | { type: "wordSize"; wordSize: WordSize }
    | { type: "operator"; operator: ProgrammerBinaryOperator }
    | { type: "not" }
    | { type: "toggleBit"; bit: number }
    | { type: "equals" }
    | { type: "backspace" }
    | { type: "clearEntry" }
    | { type: "clear" };

export const initialProgrammerState: ProgrammerState = {
    value: 0n, entry: "0", base: 10, wordSize: 64, accumulator: null,
    pendingOperator: null, waitingForOperand: false, error: "",
};

function unsigned(value: bigint, bits: WordSize): bigint { return BigInt.asUintN(bits, value); }
function signed(value: bigint, bits: WordSize): bigint { return BigInt.asIntN(bits, value); }
function wrap(value: bigint, bits: WordSize): bigint { return signed(value, bits); }

function parseEntry(entry: string, base: ProgrammerBase, bits: WordSize): bigint {
    let source = entry.toUpperCase();
    let negative = false;
    if (source.startsWith("-")) { negative = true; source = source.slice(1); }
    let value = 0n;
    const radix = BigInt(base);
    for (const character of source) {
        const digit = character >= "0" && character <= "9" ? character.charCodeAt(0) - 48 : character.charCodeAt(0) - 55;
        if (digit < 0 || digit >= base) throw new Error("Digit is not valid for the selected base");
        value = value * radix + BigInt(digit);
    }
    return wrap(negative ? -value : value, bits);
}

export function formatProgrammer(value: bigint, base: ProgrammerBase, bits: WordSize): string {
    const normalized = base === 10 ? signed(value, bits) : unsigned(value, bits);
    return normalized.toString(base).toUpperCase();
}

export interface ProgrammerReadouts { readonly HEX: string; readonly DEC: string; readonly OCT: string; readonly BIN: string; }
export function programmerReadouts(state: ProgrammerState): ProgrammerReadouts {
    return {
        HEX: formatProgrammer(state.value, 16, state.wordSize),
        DEC: formatProgrammer(state.value, 10, state.wordSize),
        OCT: formatProgrammer(state.value, 8, state.wordSize),
        BIN: formatProgrammer(state.value, 2, state.wordSize),
    };
}

function normalizedShiftCount(count: bigint, bits: WordSize): number {
    const width = BigInt(bits);
    return Number(((count % width) + width) % width);
}

function powerOfTwo(exponent: number): bigint {
    let value = 1n;
    for (let index = 0; index < exponent; index++) value = value * 2n;
    return value;
}

function arithmeticShiftRight(value: bigint, count: number): bigint {
    const divisor = powerOfTwo(count);
    if (value >= 0n) return value / divisor;
    return -((-value + divisor - 1n) / divisor);
}

function rotateLeft(value: bigint, count: bigint, bits: WordSize): bigint {
    const shift = normalizedShiftCount(count, bits);
    if (shift === 0) return wrap(value, bits);
    const source = unsigned(value, bits);
    const modulus = powerOfTwo(bits);
    const low = (source * powerOfTwo(shift)) % modulus;
    const high = source / powerOfTwo(bits - shift);
    return wrap(low + high, bits);
}

function rotateRight(value: bigint, count: bigint, bits: WordSize): bigint {
    const shift = normalizedShiftCount(count, bits);
    if (shift === 0) return wrap(value, bits);
    const source = unsigned(value, bits);
    const divisor = powerOfTwo(shift);
    const low = source / divisor;
    const high = (source % divisor) * powerOfTwo(bits - shift);
    return wrap(low + high, bits);
}

function calculateProgrammer(left: bigint, right: bigint, operator: ProgrammerBinaryOperator, bits: WordSize): bigint {
    switch (operator) {
        case "+": return wrap(left + right, bits);
        case "-": return wrap(left - right, bits);
        case "*": return wrap(left * right, bits);
        case "/": if (right === 0n) throw new Error("Cannot divide by zero"); return wrap(left / right, bits);
        case "mod": if (right === 0n) throw new Error("Cannot divide by zero"); return wrap(left % right, bits);
        case "and": return wrap(left & right, bits);
        case "or": return wrap(left | right, bits);
        case "xor": return wrap(left ^ right, bits);
        case "lsh": return wrap(left * powerOfTwo(normalizedShiftCount(right, bits)), bits);
        case "rsh": return wrap(arithmeticShiftRight(left, normalizedShiftCount(right, bits)), bits);
        case "rol": return rotateLeft(left, right, bits);
        case "ror": return rotateRight(left, right, bits);
    }
    throw new Error("Unknown programmer operator");
}

function withValue(state: ProgrammerState, value: bigint, waitingForOperand: boolean): ProgrammerState {
    value = wrap(value, state.wordSize);
    return { ...state, value, entry: formatProgrammer(value, state.base, state.wordSize), waitingForOperand, error: "" };
}

export function programmerReducer(state: ProgrammerState, action: ProgrammerAction): ProgrammerState {
    try {
        switch (action.type) {
            case "clear": return { ...initialProgrammerState, base: state.base, wordSize: state.wordSize };
            case "clearEntry": return withValue(state, 0n, false);
            case "base": {
                const base: ProgrammerBase = action.base === undefined ? 10 : action.base;
                return { ...state, base, entry: formatProgrammer(state.value, base, state.wordSize) };
            }
            case "wordSize": {
                const wordSize: WordSize = action.wordSize === undefined ? 64 : action.wordSize;
                const value = wrap(state.value, wordSize);
                return { ...state, wordSize, value, entry: formatProgrammer(value, state.base, wordSize) };
            }
            case "digit": {
                const digit = action.digit === undefined ? "0" : action.digit;
                const digitValue = digit >= "0" && digit <= "9" ? digit.charCodeAt(0) - 48 : digit.toUpperCase().charCodeAt(0) - 55;
                if (digitValue < 0 || digitValue >= state.base) return state;
                const entry = state.waitingForOperand || state.entry === "0" ? digit.toUpperCase() : state.entry + digit.toUpperCase();
                return { ...state, entry, value: parseEntry(entry, state.base, state.wordSize), waitingForOperand: false, error: "" };
            }
            case "backspace": {
                if (state.waitingForOperand) return state;
                const entry = state.entry.length <= 1 ? "0" : state.entry.slice(0, state.entry.length - 1);
                return { ...state, entry, value: parseEntry(entry, state.base, state.wordSize) };
            }
            case "not": return withValue(state, wrap(~state.value, state.wordSize), true);
            case "toggleBit": {
                const bit = action.bit === undefined ? 0 : action.bit;
                if (bit < 0 || bit >= state.wordSize) return state;
                return withValue(state, state.value ^ powerOfTwo(bit), false);
            }
            case "operator": {
                const operator: ProgrammerBinaryOperator = action.operator === undefined ? "+" : action.operator;
                if (state.pendingOperator !== null && state.accumulator !== null && !state.waitingForOperand) {
                    const left = state.accumulator === null ? 0n : state.accumulator;
                    const pending = state.pendingOperator === null ? "+" : state.pendingOperator;
                    const value = calculateProgrammer(left, state.value, pending, state.wordSize);
                    return { ...withValue(state, value, true), accumulator: value, pendingOperator: operator };
                }
                return { ...state, accumulator: state.value, pendingOperator: operator, waitingForOperand: true, error: "" };
            }
            case "equals": {
                if (state.pendingOperator === null || state.accumulator === null) return state;
                const left = state.accumulator === null ? 0n : state.accumulator;
                const pending = state.pendingOperator === null ? "+" : state.pendingOperator;
                const value = calculateProgrammer(left, state.value, pending, state.wordSize);
                return { ...withValue(state, value, true), accumulator: value, pendingOperator: null };
            }
        }
    } catch (error) {
        return { ...state, error: String(error), waitingForOperand: true };
    }
    return state;
}
