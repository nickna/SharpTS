export type AngleUnit = "deg" | "rad" | "grad";

export interface ExpressionContext {
    readonly angleUnit: AngleUnit;
    readonly variables?: Readonly<Record<string, number>>;
}

type TokenKind = "number" | "identifier" | "operator" | "left" | "right" | "comma" | "end";
interface Token { readonly kind: TokenKind; readonly text: string; readonly value: number; }

function tokenize(source: string): Token[] {
    const tokens: Token[] = [];
    let index = 0;
    while (index < source.length) {
        const character = source[index];
        if (character === " " || character === "\t" || character === "\r" || character === "\n") { index++; continue; }
        if ((character >= "0" && character <= "9") || character === ".") {
            const start = index;
            let hasPoint = false;
            while (index < source.length) {
                const current = source[index];
                if (current === "." && !hasPoint) { hasPoint = true; index++; continue; }
                if (current < "0" || current > "9") break;
                index++;
            }
            if (index < source.length && source[index].toLowerCase() === "e") {
                index++;
                if (source[index] === "+" || source[index] === "-") index++;
                while (index < source.length && source[index] >= "0" && source[index] <= "9") index++;
            }
            const text = source.slice(start, index);
            const value = Number.parseFloat(text);
            if (!Number.isFinite(value)) throw new Error("Invalid number: " + text);
            tokens.push({ kind: "number", text, value });
            continue;
        }
        if ((character >= "a" && character <= "z") || (character >= "A" && character <= "Z") || character === "π") {
            const start = index;
            index++;
            while (index < source.length) {
                const current = source[index];
                if (!((current >= "a" && current <= "z") || (current >= "A" && current <= "Z") || (current >= "0" && current <= "9") || current === "_")) break;
                index++;
            }
            tokens.push({ kind: "identifier", text: source.slice(start, index).toLowerCase(), value: 0 });
            continue;
        }
        if (character === "(") tokens.push({ kind: "left", text: character, value: 0 });
        else if (character === ")") tokens.push({ kind: "right", text: character, value: 0 });
        else if (character === ",") tokens.push({ kind: "comma", text: character, value: 0 });
        else if ("+-*/^%!".indexOf(character) >= 0) tokens.push({ kind: "operator", text: character, value: 0 });
        else if (character === "×") tokens.push({ kind: "operator", text: "*", value: 0 });
        else if (character === "÷") tokens.push({ kind: "operator", text: "/", value: 0 });
        else throw new Error("Unexpected character: " + character);
        index++;
    }
    tokens.push({ kind: "end", text: "", value: 0 });
    return tokens;
}

function factorial(value: number): number {
    if (value < 0 || value !== Math.floor(value) || value > 170) throw new Error("Factorial requires an integer from 0 to 170");
    let result = 1;
    for (let current = 2; current <= value; current++) result *= current;
    return result;
}

function toRadians(value: number, unit: AngleUnit): number {
    return unit === "deg" ? value * Math.PI / 180 : unit === "grad" ? value * Math.PI / 200 : value;
}

function fromRadians(value: number, unit: AngleUnit): number {
    return unit === "deg" ? value * 180 / Math.PI : unit === "grad" ? value * 200 / Math.PI : value;
}

function hyperbolicSine(value: number): number { return (Math.exp(value) - Math.exp(-value)) / 2; }
function hyperbolicCosine(value: number): number { return (Math.exp(value) + Math.exp(-value)) / 2; }
function hyperbolicTangent(value: number): number {
    const positive = Math.exp(value);
    const negative = Math.exp(-value);
    return (positive - negative) / (positive + negative);
}

function callFunction(name: string, args: number[], angleUnit: AngleUnit): number {
    const value = args[0];
    switch (name) {
        case "sin": return Math.sin(toRadians(value, angleUnit));
        case "cos": return Math.cos(toRadians(value, angleUnit));
        case "tan": return Math.tan(toRadians(value, angleUnit));
        case "asin": return fromRadians(Math.asin(value), angleUnit);
        case "acos": return fromRadians(Math.acos(value), angleUnit);
        case "atan": return fromRadians(Math.atan(value), angleUnit);
        case "sinh": return hyperbolicSine(value);
        case "cosh": return hyperbolicCosine(value);
        case "tanh": return hyperbolicTangent(value);
        case "asinh": return Math.log(value + Math.sqrt(value * value + 1));
        case "acosh": return Math.log(value + Math.sqrt(value * value - 1));
        case "atanh": return Math.log((1 + value) / (1 - value)) / 2;
        case "sqrt": return Math.sqrt(value);
        case "cbrt": return value < 0 ? -Math.pow(-value, 1 / 3) : Math.pow(value, 1 / 3);
        case "abs": return Math.abs(value);
        case "floor": return Math.floor(value);
        case "ceil": return Math.ceil(value);
        case "ln": return Math.log(value);
        case "log": return Math.log(value) / Math.log(10);
        case "log2": return Math.log(value) / Math.log(2);
        case "exp": return Math.exp(value);
        case "pow": return Math.pow(value, args[1]);
        case "root": return Math.pow(args[1], 1 / value);
        case "min": return Math.min(...args);
        case "max": return Math.max(...args);
    }
    throw new Error("Unknown function: " + name);
}

class Parser {
    private index: number = 0;
    constructor(private readonly tokens: Token[], private readonly context: ExpressionContext) {}
    private current(): Token { return this.tokens[this.index]; }
    private take(): Token { const token = this.current(); this.index++; return token; }

    parse(): number {
        const result = this.additive();
        if (this.current().kind !== "end") throw new Error("Unexpected token: " + this.current().text);
        if (!Number.isFinite(result)) throw new Error("Result is undefined");
        return result;
    }

    private additive(): number {
        let value = this.multiplicative();
        while (this.current().kind === "operator" && (this.current().text === "+" || this.current().text === "-")) {
            const operator = this.take().text;
            const right = this.multiplicative();
            value = operator === "+" ? value + right : value - right;
        }
        return value;
    }

    private multiplicative(): number {
        let value = this.power();
        while (this.current().kind === "operator" && "*/%".indexOf(this.current().text) >= 0) {
            const operator = this.take().text;
            const right = this.power();
            value = operator === "*" ? value * right : operator === "/" ? value / right : value % right;
        }
        return value;
    }

    private power(): number {
        let value = this.unary();
        if (this.current().kind === "operator" && this.current().text === "^") {
            this.take();
            value = Math.pow(value, this.power());
        }
        return value;
    }

    private unary(): number {
        if (this.current().kind === "operator" && (this.current().text === "+" || this.current().text === "-")) {
            const operator = this.take().text;
            const value = this.unary();
            return operator === "-" ? -value : value;
        }
        let value = this.primary();
        while (this.current().kind === "operator" && this.current().text === "!") {
            this.take();
            value = factorial(value);
        }
        return value;
    }

    private primary(): number {
        const token = this.take();
        if (token.kind === "number") return token.value;
        if (token.kind === "left") {
            const value = this.additive();
            if (this.take().kind !== "right") throw new Error("Missing closing parenthesis");
            return value;
        }
        if (token.kind !== "identifier") throw new Error("Expected a number or function");
        if (token.text === "pi" || token.text === "π") return Math.PI;
        if (token.text === "e") return Math.E;
        if (this.current().kind === "left") {
            this.take();
            const args: number[] = [];
            if (this.current().kind !== "right") {
                args.push(this.additive());
                while (this.current().kind === "comma") { this.take(); args.push(this.additive()); }
            }
            if (this.take().kind !== "right") throw new Error("Missing closing parenthesis");
            return callFunction(token.text, args, this.context.angleUnit);
        }
        const variables = this.context.variables;
        if (variables !== undefined && variables[token.text] !== undefined) return variables[token.text];
        throw new Error("Unknown variable: " + token.text);
    }
}

export function evaluateExpression(source: string, context: ExpressionContext = { angleUnit: "rad" }): number {
    return new Parser(tokenize(source), context).parse();
}

export function formatApproximate(value: number, scientific: boolean = false): string {
    if (!Number.isFinite(value)) return "Error";
    if (Object.is(value, -0) || Math.abs(value) < 1e-15) return "0";
    return scientific ? value.toExponential(12).replace(/\.0+e/, "e") : Number.parseFloat(value.toPrecision(15)).toString();
}
