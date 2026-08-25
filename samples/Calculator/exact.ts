export interface ExactNumber {
    readonly numerator: bigint;
    readonly denominator: bigint;
}

const ZERO: bigint = 0n;
const ONE: bigint = 1n;
const TEN: bigint = 10n;

function absolute(value: bigint): bigint { return value < ZERO ? -value : value; }

function gcd(left: bigint, right: bigint): bigint {
    left = absolute(left);
    right = absolute(right);
    while (right !== ZERO) {
        const remainder = left % right;
        left = right;
        right = remainder;
    }
    return left === ZERO ? ONE : left;
}

export function exact(numerator: bigint, denominator: bigint = ONE): ExactNumber {
    if (denominator === ZERO) throw new Error("Division by zero");
    if (denominator < ZERO) {
        numerator = -numerator;
        denominator = -denominator;
    }
    const divisor = gcd(numerator, denominator);
    return { numerator: numerator / divisor, denominator: denominator / divisor };
}

export function parseExact(text: string): ExactNumber {
    let source = text.trim().toLowerCase();
    if (source === "" || source === "+" || source === "-") return exact(ZERO);
    let sign = ONE;
    if (source.startsWith("-")) { sign = -ONE; source = source.slice(1); }
    else if (source.startsWith("+")) source = source.slice(1);

    let exponent = 0;
    const exponentIndex = source.indexOf("e");
    if (exponentIndex >= 0) {
        exponent = Number.parseInt(source.slice(exponentIndex + 1), 10);
        source = source.slice(0, exponentIndex);
    }
    const point = source.indexOf(".");
    const scale = point < 0 ? 0 : source.length - point - 1;
    const digits = point < 0 ? source : source.slice(0, point) + source.slice(point + 1);
    let numerator = BigInt(digits === "" ? "0" : digits) * sign;
    let denominator = ONE;
    const adjustedScale = scale - exponent;
    if (adjustedScale > 0) denominator = TEN ** BigInt(adjustedScale);
    else if (adjustedScale < 0) numerator = numerator * (TEN ** BigInt(-adjustedScale));
    return exact(numerator, denominator);
}

export function addExact(left: ExactNumber, right: ExactNumber): ExactNumber {
    return exact(left.numerator * right.denominator + right.numerator * left.denominator,
        left.denominator * right.denominator);
}

export function subtractExact(left: ExactNumber, right: ExactNumber): ExactNumber {
    return exact(left.numerator * right.denominator - right.numerator * left.denominator,
        left.denominator * right.denominator);
}

export function multiplyExact(left: ExactNumber, right: ExactNumber): ExactNumber {
    return exact(left.numerator * right.numerator, left.denominator * right.denominator);
}

export function divideExact(left: ExactNumber, right: ExactNumber): ExactNumber {
    if (right.numerator === ZERO) throw new Error("Division by zero");
    return exact(left.numerator * right.denominator, left.denominator * right.numerator);
}

export function negateExact(value: ExactNumber): ExactNumber {
    return { numerator: -value.numerator, denominator: value.denominator };
}

export function exactToNumber(value: ExactNumber): number {
    return Number(value.numerator) / Number(value.denominator);
}

export function formatExact(value: ExactNumber, maximumFractionDigits: number = 32): string {
    if (value.numerator === ZERO) return "0";
    const negative = value.numerator < ZERO;
    let numerator = absolute(value.numerator);
    const integer = numerator / value.denominator;
    let remainder = numerator % value.denominator;
    if (remainder === ZERO) return (negative ? "-" : "") + integer.toString();

    let fraction = "";
    let count = 0;
    while (remainder !== ZERO && count < maximumFractionDigits) {
        remainder = remainder * TEN;
        fraction += (remainder / value.denominator).toString();
        remainder = remainder % value.denominator;
        count++;
    }
    while (fraction.endsWith("0")) fraction = fraction.slice(0, fraction.length - 1);
    return (negative ? "-" : "") + integer.toString() + "." + fraction;
}

export function compareExact(left: ExactNumber, right: ExactNumber): number {
    const difference = left.numerator * right.denominator - right.numerator * left.denominator;
    return difference < ZERO ? -1 : difference > ZERO ? 1 : 0;
}
